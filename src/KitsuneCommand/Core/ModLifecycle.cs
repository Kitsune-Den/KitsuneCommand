using Autofac;
using HarmonyLib;
using KitsuneCommand.Configuration;
using KitsuneCommand.Data;
using KitsuneCommand.Data.Repositories;
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
        private IPlayerMetadataRepository _metadataRepo;

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

                // PaintIndexWidenerPatch is NOT auto-discovered (no [HarmonyPatch] class attribute).
                // Only apply it when PaintUnlocked is installed — without it, vanilla clients sending
                // paint packets would crash the server's network deserializer.
                ApplyPaintPatchIfNeeded();
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Failed to apply Harmony patches: {ex.Message}");
                Log.Exception(ex);
            }
        }

        private void ApplyPaintPatchIfNeeded()
        {
            var modsPath = Path.Combine(ModEntry.ModPath, "..");
            var paintUnlockedExists = Directory.Exists(Path.Combine(modsPath, "0_PaintUnlocked"))
                                   || Directory.Exists(Path.Combine(modsPath, "PaintUnlocked"));

            if (!paintUnlockedExists)
            {
                Log.Out("[KitsuneCommand] PaintUnlocked not detected — PaintIndexWidenerPatch skipped.");
                return;
            }

            try
            {
                var targetType = typeof(NetPackageSetBlockTexture);
                var patchType = typeof(GameIntegration.Harmony.PaintIndexWidenerPatch);

                _harmony.Patch(
                    AccessTools.Method(targetType, "Setup"),
                    postfix: new HarmonyMethod(AccessTools.Method(patchType, "SetupPostfix")));

                _harmony.Patch(
                    AccessTools.Method(targetType, "write"),
                    prefix: new HarmonyMethod(AccessTools.Method(patchType, "WritePrefix")));

                _harmony.Patch(
                    AccessTools.Method(targetType, "read"),
                    prefix: new HarmonyMethod(AccessTools.Method(patchType, "ReadPrefix")));

                Log.Out("[KitsuneCommand] PaintIndexWidenerPatch applied (PaintUnlocked detected).");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to apply PaintIndexWidenerPatch: {ex.Message}");
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

            // Initialize game item catalog (reads all ItemClass entries)
            var itemCatalog = _container.Resolve<GameItemCatalog>();
            itemCatalog.Initialize();

            // Initialize item icon service (serves icon PNGs from Data/ItemIcons)
            var iconService = _container.Resolve<ItemIconService>();
            iconService.Initialize();

            // Initialize backup service (loads settings and starts scheduler if enabled)
            var backupService = _container.Resolve<BackupService>();
            backupService.Initialize();

            // Resolve chat command feature for direct command dispatch
            _chatCommandFeature = _container.Resolve<ChatCommandFeature>();

            // Resolve player metadata repo for chat name colors
            _metadataRepo = _container.Resolve<IPlayerMetadataRepository>();

            _eventBus.Publish(new GameStartDoneEvent());
        }

        private void OnGameShutdown(ref ModEvents.SGameShutdownData _data)
        {
            Log.Out("[KitsuneCommand] Shutting down...");

            _eventBus.Publish(new GameShutdownEvent());

            // Shutdown services
            try { _container?.Resolve<LivePlayerManager>()?.Shutdown(); } catch { }
            try { _container?.Resolve<BackupService>()?.Shutdown(); } catch { }

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
            var senderName = _data.MainName;

            // Apply name color from player metadata if configured
            if (!string.IsNullOrEmpty(playerId) && _metadataRepo != null)
            {
                try
                {
                    var metadata = _metadataRepo.GetByPlayerId(playerId);
                    if (metadata?.NameColor != null)
                    {
                        senderName = $"[{metadata.NameColor}]{_data.MainName}[-]";
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KitsuneCommand] Failed to apply name color: {ex.Message}");
                }
            }

            // Check if this is a chat command before publishing
            var isCommand = _chatCommandFeature != null
                            && _chatCommandFeature.IsRunning
                            && _chatCommandFeature.Settings.Enabled
                            && !string.IsNullOrEmpty(message)
                            && !string.IsNullOrEmpty(playerId)
                            && entityId > 0
                            && message.StartsWith(_chatCommandFeature.Settings.Prefix);

            // Always publish the chat event (for chat log / WebSocket broadcast)
            // senderName may include color codes from player metadata
            _eventBus.Publish(new ChatMessageEvent
            {
                PlayerId = playerId,
                EntityId = entityId,
                SenderName = senderName,
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
