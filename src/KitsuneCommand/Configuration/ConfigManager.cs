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

            // World-agnostic data dir. Earlier versions of this method used
            // GameIO.GetSaveGameDir() as the base, which returns the *current
            // world's* save folder. The consequence: every new world (or any
            // 7DTD boot that landed on a different save dir for any reason)
            // produced an empty KitsuneCommand DB and re-ran
            // AuthService.EnsureAdminExists, silently rotating the admin
            // password and writing a fresh FIRST_RUN_PASSWORD.txt. Operators
            // saw their saved panel creds stop working with no obvious cause —
            // and the only fingerprint was a recurring "FIRST RUN" block in
            // the nssm log. Fix: anchor KC's data to a path that's stable
            // across worlds, mod updates, and PackRelay re-installs.
            var dataDir = ResolveWorldAgnosticDataDir();
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            // Best-effort one-time copy from the legacy per-world location.
            // Idempotent — safe to call on every boot.
            TryMigrateLegacyDataDir(dataDir);

            // Production config lives outside the mod folder so it survives updates
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
        /// Returns the stable, world-agnostic directory for KitsuneCommand's
        /// persistent data — the SQLite DB, the appsettings.json production
        /// override, the FIRST_RUN_PASSWORD.txt, and the emergency
        /// RESET_PASSWORD.txt drop-file. Anchors to the 7DTD user-data root
        /// (parent of <c>Saves/</c>) so it survives world regen, individual
        /// save deletion, and PackRelay mod re-installs.
        ///
        /// Path shape assumed: <c>&lt;UserDataRoot&gt;/Saves/&lt;World&gt;/&lt;Game&gt;/</c>.
        /// If that walk fails (e.g. 7DTD changes its layout in a future patch),
        /// falls back to the legacy per-world dir with a loud warning rather
        /// than throwing — that preserves the buggy-but-functional old behavior
        /// instead of breaking mod load entirely.
        ///
        /// Public so other components that land files next to the DB
        /// (<see cref="Web.Auth.AuthService"/>'s FIRST_RUN_PASSWORD.txt,
        /// <see cref="Web.WebServerHost"/>'s RESET_PASSWORD.txt) can share
        /// the resolution logic instead of duplicating the path walk.
        /// </summary>
        public static string ResolveWorldAgnosticDataDir()
        {
            var saveGameDir = GameIO.GetSaveGameDir();
            // <UserDataRoot>/Saves/<World>/<Game>/  → walk up 3 levels to land at <UserDataRoot>/.
            var userDataRoot = Directory.GetParent(saveGameDir)?.Parent?.Parent?.FullName;
            if (string.IsNullOrEmpty(userDataRoot))
            {
                Log.Warning(
                    "[KitsuneCommand] Could not resolve user-data root from save dir '" +
                    saveGameDir + "' — falling back to per-world data dir. " +
                    "Admin password may regenerate on world regen until this is fixed.");
                return Path.Combine(saveGameDir, "KitsuneCommand");
            }
            return Path.Combine(userDataRoot, "KitsuneCommand");
        }

        /// <summary>
        /// One-time copy from the legacy per-world data dir to the new
        /// world-agnostic dir. Idempotent: returns immediately if the new dir
        /// already contains a .db or .json file (i.e. a previous migration ran,
        /// or this is a clean install on the new code). Best-effort: any
        /// failure logs a warning and lets boot continue — the new dir just
        /// stays empty and the FIRST RUN flow kicks in, which is the same as
        /// any clean install.
        ///
        /// The legacy files are intentionally left in place rather than moved.
        /// Worst case the operator deletes the old per-world dir manually after
        /// verifying the panel still logs in; cheap insurance against this
        /// migration eating data we needed.
        /// </summary>
        private static void TryMigrateLegacyDataDir(string newDataDir)
        {
            try
            {
                var legacyDir = Path.Combine(GameIO.GetSaveGameDir(), "KitsuneCommand");
                if (!Directory.Exists(legacyDir)) return;
                if (string.Equals(legacyDir, newDataDir, StringComparison.OrdinalIgnoreCase)) return;

                // Skip if the new dir already has meaningful data — don't clobber
                // a previously-migrated or freshly-installed DB.
                if (Directory.Exists(newDataDir))
                {
                    foreach (var f in Directory.GetFiles(newDataDir))
                    {
                        var ext = Path.GetExtension(f);
                        if (ext.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
                }

                var copied = 0;
                foreach (var f in Directory.GetFiles(legacyDir))
                {
                    var dest = Path.Combine(newDataDir, Path.GetFileName(f));
                    if (!File.Exists(dest))
                    {
                        File.Copy(f, dest);
                        copied++;
                    }
                }

                if (copied > 0)
                {
                    Log.Out(
                        "[KitsuneCommand] Migrated " + copied + " file(s) from legacy " +
                        "per-world data dir '" + legacyDir + "' → '" + newDataDir + "'. " +
                        "Legacy files left in place; safe to delete after verifying " +
                        "the panel still logs in.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(
                    "[KitsuneCommand] Legacy data-dir migration failed: " + ex.Message + ". " +
                    "Continuing with empty new data dir — first-run admin will be created fresh.");
            }
        }

        /// <summary>
        /// Gets the current app settings.
        /// </summary>
        public AppSettings AppSettings => _appSettings;
    }
}
