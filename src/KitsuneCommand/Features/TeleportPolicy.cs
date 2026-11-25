using KitsuneCommand.Data.Repositories;
using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Applies the Teleport settings to a teleport a player asks for in chat (/home, /tp).
    /// The panel's admin and moderator teleports don't go through this, so staff can still
    /// move a player during a blood moon.
    /// </summary>
    public static class TeleportPolicy
    {
        /// <summary>Settings table key the Teleport settings are saved under.</summary>
        public const string SettingsKey = "Teleport";

        /// <summary>
        /// Reads the persisted TeleportSettings. Falls back to defaults if not found or on error.
        /// </summary>
        public static TeleportSettings LoadSettings(ISettingsRepository settingsRepo)
        {
            try
            {
                var json = settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<TeleportSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch { /* fall through to defaults */ }

            return new TeleportSettings();
        }

        /// <summary>
        /// Why a player can't teleport right now because of the blood moon, or null when they
        /// can. Refuses only when AllowTeleportDuringBloodMoon is off and the clock says a blood
        /// moon is running (dusk on the blood moon day until dawn the next morning).
        /// <paramref name="clock"/> is null when there's no world to read it from; that never
        /// refuses.
        /// </summary>
        public static string BloodMoonRefusal(TeleportSettings settings, GameClock? clock)
        {
            if (settings == null || settings.AllowTeleportDuringBloodMoon) return null;
            if (clock == null || !clock.Value.IsBloodMoonNow) return null;

            return $"Teleporting is turned off during the blood moon. Try again after dawn ({clock.Value.DawnHour:D2}:00).";
        }
    }
}
