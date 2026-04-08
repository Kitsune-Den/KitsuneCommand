using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.GameIntegration;
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// PvP damage rebalancing feature. Configures the PvPDamagePatch Harmony hook
    /// to scale player-vs-player damage by a configurable multiplier.
    /// Settings are persisted to the database and can be changed live via the API.
    /// </summary>
    public class PvPBalanceFeature : FeatureBase<PvPBalanceSettings>
    {
        private readonly ISettingsRepository _settingsRepo;
        private const string SettingsKey = "PvPBalance";

        public PvPBalanceFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ISettingsRepository settingsRepo)
            : base(eventBus, config)
        {
            _settingsRepo = settingsRepo;
        }

        protected override void OnEnable()
        {
            LoadPersistedSettings();
            ApplyToConfig();

            Log.Out($"[KitsuneCommand] PvP balance feature enabled. Multiplier={Settings.DamageMultiplier}, " +
                    $"Headshot={Settings.HeadshotMultiplier}, Enabled={Settings.Enabled}");
        }

        protected override void OnDisable()
        {
            // Reset to vanilla behavior
            PvPDamageConfig.Enabled = false;
        }

        /// <summary>
        /// Updates settings in memory, persists to database, and applies to the live Harmony patch.
        /// No server restart required.
        /// </summary>
        public void UpdateSettings(PvPBalanceSettings newSettings)
        {
            Settings = newSettings;
            ApplyToConfig();

            try
            {
                var json = JsonConvert.SerializeObject(newSettings);
                _settingsRepo.Set(SettingsKey, json);
                Log.Out($"[KitsuneCommand] PvP balance settings updated. Multiplier={newSettings.DamageMultiplier}");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to persist PvP settings: {ex.Message}");
            }
        }

        private void ApplyToConfig()
        {
            PvPDamageConfig.Enabled = Settings.Enabled;
            PvPDamageConfig.DamageMultiplier = Settings.DamageMultiplier;
            PvPDamageConfig.HeadshotMultiplier = Settings.HeadshotMultiplier;
            PvPDamageConfig.LogPvPHits = Settings.LogPvPHits;
        }

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<PvPBalanceSettings>(json);
                    if (loaded != null)
                    {
                        Settings = loaded;
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load PvP settings, using defaults: {ex.Message}");
            }
        }
    }
}
