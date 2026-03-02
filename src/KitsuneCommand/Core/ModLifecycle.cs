using Autofac;
using HarmonyLib;
using KitsuneCommand.Configuration;
using KitsuneCommand.Data;
using KitsuneCommand.Features;
using KitsuneCommand.Plugins;
using KitsuneCommand.Web;
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

            // 7. Start web server (deferred until GameStartDone for most endpoints)
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

        private void OnGameAwake()
        {
            _eventBus.Publish(new GameAwakeEvent());
        }

        private void OnGameStartDone()
        {
            ModEntry.IsGameStartDone = true;
            Log.Out("[KitsuneCommand] Game start complete. All systems active.");

            // Initialize feature modules
            var featureManager = _container.Resolve<FeatureManager>();
            featureManager.InitializeAll();

            _eventBus.Publish(new GameStartDoneEvent());
        }

        private void OnGameShutdown()
        {
            Log.Out("[KitsuneCommand] Shutting down...");

            _eventBus.Publish(new GameShutdownEvent());

            _wsServer?.Stop();
            _webServer?.Stop();
            _harmony?.UnpatchAll("com.kitsunecommand.mod");
            _container?.Dispose();

            Log.Out("[KitsuneCommand] Shutdown complete.");
        }

        private void OnPlayerLogin(ClientInfo clientInfo, string compatibilityVersion, StringBuilder rejectReason)
        {
            if (clientInfo == null) return;

            _eventBus.Publish(new PlayerLoginEvent
            {
                PlayerId = clientInfo.CrossplatformId?.CombinedString,
                PlayerName = clientInfo.playerName,
                EntityId = clientInfo.entityId,
                PlatformId = clientInfo.PlatformId?.CombinedString,
                Ip = clientInfo.ip
            });
        }

        private void OnPlayerSpawnedInWorld(ClientInfo clientInfo, RespawnType respawnReason, Vector3i pos)
        {
            if (clientInfo == null) return;

            _eventBus.Publish(new PlayerSpawnedEvent
            {
                PlayerId = clientInfo.CrossplatformId?.CombinedString,
                PlayerName = clientInfo.playerName,
                EntityId = clientInfo.entityId,
                RespawnType = (Abstractions.Models.RespawnType)(int)respawnReason,
                PositionX = pos.x,
                PositionY = pos.y,
                PositionZ = pos.z
            });
        }

        private void OnPlayerDisconnected(ClientInfo clientInfo, bool shutdown)
        {
            if (clientInfo == null) return;

            _eventBus.Publish(new PlayerDisconnectedEvent
            {
                PlayerId = clientInfo.CrossplatformId?.CombinedString,
                PlayerName = clientInfo.playerName,
                EntityId = clientInfo.entityId
            });
        }

        private void OnPlayerSpawning(ClientInfo clientInfo, int chunkViewDim, PlayerProfile playerProfile)
        {
            if (clientInfo == null) return;

            _eventBus.Publish(new PlayerSpawningEvent
            {
                PlayerId = clientInfo.CrossplatformId?.CombinedString,
                PlayerName = clientInfo.playerName,
                EntityId = clientInfo.entityId
            });
        }

        private void OnSavePlayerData(ClientInfo clientInfo, PlayerDataFile playerDataFile)
        {
            if (clientInfo == null) return;

            _eventBus.Publish(new SavePlayerDataEvent
            {
                PlayerId = clientInfo.CrossplatformId?.CombinedString,
                PlayerName = clientInfo.playerName,
                EntityId = clientInfo.entityId
            });
        }

        private void OnEntityKilled(Entity killed, Entity killer)
        {
            _eventBus.Publish(new EntityKilledEvent
            {
                DeadEntityId = killed?.entityId ?? -1,
                DeadEntityName = killed?.EntityName,
                KillerEntityId = killer?.entityId ?? -1,
                KillerName = killer?.EntityName
            });
        }

        private bool OnChatMessage(ClientInfo clientInfo, EChatType chatType, int senderId,
            string message, string mainName, bool localizeMain, List<int> recipientEntityIds)
        {
            _eventBus.Publish(new ChatMessageEvent
            {
                PlayerId = clientInfo?.CrossplatformId?.CombinedString,
                EntityId = senderId,
                SenderName = mainName,
                ChatType = (ChatType)(int)chatType,
                Message = message
            });

            return true; // Allow message to pass through
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
