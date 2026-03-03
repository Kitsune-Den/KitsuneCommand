using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Feature module for in-game chat commands.
    /// Handles command dispatch and settings persistence.
    /// </summary>
    public class ChatCommandFeature : FeatureBase<ChatCommandSettings>
    {
        private readonly ChatCommandService _commandService;
        private readonly ISettingsRepository _settingsRepo;

        private const string SettingsKey = "ChatCommands";

        public ChatCommandFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ChatCommandService commandService,
            ISettingsRepository settingsRepo)
            : base(eventBus, config)
        {
            _commandService = commandService;
            _settingsRepo = settingsRepo;
        }

        protected override void OnEnable()
        {
            // Load persisted settings from database
            LoadPersistedSettings();

            Log.Out($"[KitsuneCommand] Chat commands enabled. Prefix='{Settings.Prefix}', " +
                    $"Home={Settings.HomeEnabled}, Teleport={Settings.TeleportEnabled}, " +
                    $"Points={Settings.PointsEnabled}, Store={Settings.StoreEnabled}");
        }

        protected override void OnDisable()
        {
            // Nothing to unsubscribe — commands are dispatched directly from ModLifecycle
        }

        /// <summary>
        /// Called directly from ModLifecycle.OnChatMessage (on the game thread).
        /// </summary>
        public void HandleCommand(string playerId, int entityId, string playerName, string message)
        {
            if (!IsRunning || !Settings.Enabled) return;

            _commandService.TryHandleCommand(playerId, entityId, playerName, message, Settings);
        }

        /// <summary>
        /// Updates settings in memory and persists to database.
        /// Called from the settings API controller.
        /// </summary>
        public void UpdateSettings(ChatCommandSettings newSettings)
        {
            Settings = newSettings;
            try
            {
                var json = JsonConvert.SerializeObject(newSettings);
                _settingsRepo.Set(SettingsKey, json);
                Log.Out($"[KitsuneCommand] Chat command settings updated and saved.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to persist chat command settings: {ex.Message}");
            }
        }

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<ChatCommandSettings>(json);
                    if (loaded != null)
                    {
                        Settings = loaded;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load chat command settings, using defaults: {ex.Message}");
            }

            // Settings remains as default (from FeatureBase.LoadSettings -> new TSettings())
        }
    }
}
