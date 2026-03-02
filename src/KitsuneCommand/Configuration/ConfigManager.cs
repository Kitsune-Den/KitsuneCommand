using Microsoft.Extensions.Configuration;

namespace KitsuneCommand.Configuration
{
    /// <summary>
    /// Manages loading and accessing configuration from appsettings.json
    /// and the settings database table.
    /// </summary>
    public class ConfigManager
    {
        private readonly AppSettings _appSettings;

        public ConfigManager(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        /// <summary>
        /// Loads AppSettings from the mod's Config/ directory, with an optional
        /// production override from the server's data directory.
        /// </summary>
        public static AppSettings LoadAppSettings(string modPath)
        {
            var defaultConfigPath = Path.Combine(modPath, "Config", "appsettings.json");

            // Production config lives outside the mod folder so it survives updates
            var dataDir = Path.Combine(GameIO.GetSaveGameDir(), "KitsuneCommand");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            var productionConfigPath = Path.Combine(dataDir, "appsettings.json");

            // Copy default config to production path if it doesn't exist
            if (!File.Exists(productionConfigPath) && File.Exists(defaultConfigPath))
            {
                File.Copy(defaultConfigPath, productionConfigPath);
            }

            var builder = new ConfigurationBuilder();

            if (File.Exists(defaultConfigPath))
            {
                builder.AddJsonFile(defaultConfigPath, optional: true, reloadOnChange: false);
            }

            if (File.Exists(productionConfigPath))
            {
                builder.AddJsonFile(productionConfigPath, optional: true, reloadOnChange: false);
            }

            var configuration = builder.Build();
            var settings = new AppSettings();
            configuration.Bind(settings);

            // Resolve relative database path
            if (!Path.IsPathRooted(settings.DatabasePath))
            {
                settings.DatabasePath = Path.Combine(dataDir, settings.DatabasePath);
            }

            return settings;
        }

        /// <summary>
        /// Gets the current app settings.
        /// </summary>
        public AppSettings AppSettings => _appSettings;
    }
}
