using Autofac;
using HarmonyLib;
using KitsuneCommand.Configuration;
using KitsuneCommand.Data;
using KitsuneCommand.Features;
using KitsuneCommand.Plugins;
using KitsuneCommand.Services;
using KitsuneCommand.Web;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.WebSocket;

namespace KitsuneCommand.Core
{
    /// <summary>
    /// Orchestrates the mod's boot sequence and manages the full lifecycle.
    /// </summary>
    public class ModLifecycle
    {
        private IContainer _container;
        private Harmony _harmony;
        private WebServerHost _webServer;
        private WebSocketHost _wsServer;
        private ModEventBus _eventBus;
        private ChatCommandFeature _chatCommandFeature;

        public void Initialize()
        {
            // 1. Load configuration
            var settings = ConfigManager.LoadAppSettings(ModEntry.ModPath);
            Log.Out($"[KitsuneCommand] Configuration loaded. Web URL: {settings.WebUrl}");

            // 2. Initialize database and run migrations
            DatabaseBootstrap.Initialize(settings.DatabasePath, ModEntry.ModPath);
            Log.Out("[KitsuneCommand] Database initialized.");

            // 3. Build DI container
            _container = ServiceRegistry.Build(settings);
            Log.Out("[KitsuneCommand] Service container built.");

            // 4. Apply Harmony patches
            PatchByHarmony();

            // 5. Load plugins
            PluginManager.LoadPlugins(Path.Combine(ModEntry.ModPath, "Plugins"));
            Log.Out($"[KitsuneCommand] {PluginManager.LoadedPlugins.Count} plugin(s) loaded.");

            // 6. Register game event handlers
            _eventBus = _container.Resolve<ModEventBus>();
            RegisterModEventHandlers();
            Log.Out("[KitsuneCommand] Game event handlers registered.");

            // 7. Initialize token validator for WebSocket auth
            TokenValidator.Initialize("KitsuneCommand");

            // 8. Start web server (deferred until GameStartDone for most endpoints)
            _webServer = new WebServerHost(settings, _container);
            _webServer.Start();
            Log.Out($"[KitsuneCommand] Web server started on {settings.WebUrl}");

            // 8. Start WebSocket server
            _wsServer = new WebSocketHost(settings, _container);
            _wsServer.Start();
            Log.Out($"[KitsuneCommand] WebSocket server started on port {settings.WebSocketPort}");
        }

        private void PatchByHarmony()
        {
            try
            {
                _harmony = new Harmony("com.kitsunecommand.mod");
                _harmony.PatchAll(typeof(ModLifecycle).Assembly);
                Log.Out("[KitsuneCommand] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Failed to apply Harmony patches: {ex.Message}");
                Log.Exception(ex);
            }
        }

        private void RegisterModEventHandlers()
        {
            // Game lifecycle events
            ModEvents.GameAwake.RegisterHandler(OnGameAwake);
            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.GameShutdown.RegisterHandler(OnGameShutdown);

            // Player events
            ModEvents.PlayerLogin.RegisterHandler(OnPlayerLogin);
            ModEvents.PlayerSpawnedInWorld.RegisterHandler(OnPlayerSpawnedInWorld);
            ModEvents.PlayerDisconnected.RegisterHandler(OnPlayerDisconnected);
            ModEvents.PlayerSpawning.RegisterHandler(OnPlayerSpawning);
            ModEvents.SavePlayerData.RegisterHandler(OnSavePlayerData);

            // World events
            ModEvents.EntityKilled.RegisterHandler(OnEntityKilled);
            ModEvents.ChatMessage.RegisterHandler(OnChatMessage);

            // Log callback
            Log.LogCallbacks += OnLogCallback;
        }

        // --- V2.5 API: All handlers use ref struct data params ---

        private void OnGameAwake(ref ModEvents.SGameAwakeData _data)
        {
            _eventBus.Publish(new GameAwakeEvent());
        }

        private void OnGameStartDone(ref ModEvents.SGameStartDoneData _data)
        {
            ModEntry.IsGameStartDone = true;
            Log.Out("[KitsuneCommand] Game start complete. All systems active.");

            // Initialize player tracking
            var playerManager = _container.Resolve<LivePlayerManager>();
            playerManager.Initialize();

            // Initialize map tile renderer
            var mapRenderer = _container.Resolve<MapTileRenderer>();
            mapRenderer.Initialize();

            // Initialize chat persistence
            var chatPersistence = _container.Resolve<ChatPersistenceService>();
            chatPersistence.Initialize();

            // Initialize feature modules
            var featureManager = _container.Resolve<FeatureManager>();
            featureManager.InitializeAll();

            // Resolve chat command feature for direct command dispatch
            _chatCommandFeature = _container.Resolve<ChatCommandFeature>();

            _eventBus.Publish(new GameStartDoneEvent());
        }

        private void OnGameShutdown(ref ModEvents.SGameShutdownData _data)
        {
            Log.Out("[KitsuneCommand] Shutting down...");

            _eventBus.Publish(new GameShutdownEvent());

            // Shutdown player tracking
            try { _container?.Resolve<LivePlayerManager>()?.Shutdown(); } catch { }

            _wsServer?.Stop();
            _webServer?.Stop();
            _harmony?.UnpatchSelf();
            _container?.Dispose();

            Log.Out("[KitsuneCommand] Shutdown complete.");
        }

        private ModEvents.EModEventResult OnPlayerLogin(ref ModEvents.SPlayerLoginData _data)
        {
            var ci = _data.ClientInfo;
            if (ci == null) return ModEvents.EModEventResult.Continue;

            _eventBus.Publish(new PlayerLoginEvent
            {
                PlayerId = ci.CrossplatformId?.CombinedString,
                PlayerName = ci.playerName,
                EntityId = ci.entityId,
                PlatformId = ci.PlatformId?.CombinedString,
                Ip = ci.ip
            });

            return ModEvents.EModEventResult.Continue;
        }

        private void OnPlayerSpawnedInWorld(ref ModEvents.SPlayerSpawnedInWorldData _data)
        {
            var ci = _data.ClientInfo;
            if (ci == null) return;

            _eventBus.Publish(new PlayerSpawnedEvent
            {
                PlayerId = ci.CrossplatformId?.CombinedString,
                PlayerName = ci.playerName,
                EntityId = ci.entityId,
                RespawnType = (Abstractions.Models.RespawnType)(int)_data.RespawnType,
                PositionX = _data.Position.x,
                PositionY = _data.Position.y,
                PositionZ = _data.Position.z
            });
        }

        private void OnPlayerDisconnected(ref ModEvents.SPlayerDisconnectedData _data)
        {
            var ci = _data.ClientInfo;
            if (ci == null) return;

            _eventBus.Publish(new PlayerDisconnectedEvent
            {
                PlayerId = ci.CrossplatformId?.CombinedString,
                PlayerName = ci.playerName,
                EntityId = ci.entityId
            });
        }

        private void OnPlayerSpawning(ref ModEvents.SPlayerSpawningData _data)
        {
            var ci = _data.ClientInfo;
            if (ci == null) return;

            _eventBus.Publish(new PlayerSpawningEvent
            {
                PlayerId = ci.CrossplatformId?.CombinedString,
                PlayerName = ci.playerName,
                EntityId = ci.entityId
            });
        }

        private void OnSavePlayerData(ref ModEvents.SSavePlayerDataData _data)
        {
            var ci = _data.ClientInfo;
            if (ci == null) return;

            _eventBus.Publish(new SavePlayerDataEvent
            {
                PlayerId = ci.CrossplatformId?.CombinedString,
                PlayerName = ci.playerName,
                EntityId = ci.entityId
            });
        }

        private void OnEntityKilled(ref ModEvents.SEntityKilledData _data)
        {
            // Note: game API has a typo - "KilledEntitiy" not "KilledEntity"
            var killed = _data.KilledEntitiy;
            var killer = _data.KillingEntity;

            _eventBus.Publish(new EntityKilledEvent
            {
                DeadEntityId = killed?.entityId ?? -1,
                DeadEntityName = (killed as EntityAlive)?.EntityName ?? killed?.ToString(),
                KillerEntityId = killer?.entityId ?? -1,
                KillerName = (killer as EntityAlive)?.EntityName ?? killer?.ToString()
            });
        }

        private ModEvents.EModEventResult OnChatMessage(ref ModEvents.SChatMessageData _data)
        {
            var message = _data.Message;
            var playerId = _data.ClientInfo?.CrossplatformId?.CombinedString;
            var entityId = _data.SenderEntityId;

            // Check if this is a chat command before publishing
            var isCommand = _chatCommandFeature != null
                            && _chatCommandFeature.IsRunning
                            && _chatCommandFeature.Settings.Enabled
                            && !string.IsNullOrEmpty(message)
                            && !string.IsNullOrEmpty(playerId)
                            && entityId > 0
                            && message.StartsWith(_chatCommandFeature.Settings.Prefix);

            // Always publish the chat event (for chat log / WebSocket broadcast)
            _eventBus.Publish(new ChatMessageEvent
            {
                PlayerId = playerId,
                EntityId = entityId,
                SenderName = _data.MainName,
                ChatType = (ChatType)(int)_data.ChatType,
                Message = message
            });

            // If it's a command, dispatch it
            if (isCommand)
            {
                try
                {
                    _chatCommandFeature.HandleCommand(playerId, entityId, _data.MainName, message);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KitsuneCommand] Chat command error: {ex.Message}");
                }
            }

            return ModEvents.EModEventResult.Continue;
        }

        private void OnLogCallback(string message, string trace, UnityEngine.LogType logType)
        {
            _eventBus.Publish(new LogCallbackEvent
            {
                Message = message,
                LogLevel = logType.ToString()
            });
        }
    }
}
