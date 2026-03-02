using KitsuneCommand.Configuration;
using KitsuneCommand.Core;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Base class for feature modules with typed settings.
    /// </summary>
    public abstract class FeatureBase<TSettings> : IFeature where TSettings : class, new()
    {
        protected readonly ModEventBus EventBus;
        protected readonly ConfigManager Config;

        public string Name => GetType().Name.Replace("Feature", "");
        public bool IsRunning { get; private set; }
        public TSettings Settings { get; private set; }

        protected FeatureBase(ModEventBus eventBus, ConfigManager config)
        {
            EventBus = eventBus;
            Config = config;
        }

        public void LoadSettings()
        {
            // Settings are loaded from the database settings table
            Settings = new TSettings();
            OnSettingsLoaded(Settings);
        }

        public void Start()
        {
            if (IsRunning) return;

            try
            {
                OnEnable();
                IsRunning = true;
                Log.Out($"[KitsuneCommand] Feature '{Name}' started.");
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Failed to start feature '{Name}': {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            try
            {
                OnDisable();
                IsRunning = false;
                Log.Out($"[KitsuneCommand] Feature '{Name}' stopped.");
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Error stopping feature '{Name}': {ex.Message}");
            }
        }

        protected abstract void OnEnable();
        protected abstract void OnDisable();
        protected virtual void OnSettingsLoaded(TSettings settings) { }
    }
}
