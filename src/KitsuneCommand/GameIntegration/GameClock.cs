using System;

namespace KitsuneCommand.GameIntegration
{
    /// <summary>
    /// The in-game day and time, and when the next blood moon is, worked out
    /// from the raw world time the same way the game does it
    /// (GameUtils.WorldTimeToElements, CalcDuskDawnHours, IsBloodMoonTime).
    ///
    /// Everything except <see cref="ReadLive"/> is plain arithmetic, so it can
    /// be tested without a running world. Copying the game's formulas instead
    /// of calling them also keeps this from binding to GameUtils signatures
    /// that may move between the 7D2D builds KC supports.
    /// </summary>
    public readonly struct GameClock
    {
        /// <summary>World-time ticks in one in-game day (24 hours of 1000 ticks).</summary>
        public const ulong TicksPerDay = 24000;

        /// <summary>The current day, counted from 1 like the game's HUD.</summary>
        public int Day { get; }
        public int Hour { get; }
        public int Minute { get; }

        /// <summary>
        /// The day the next (or current) blood moon falls on, from the game's
        /// BloodMoonDay stat. 0 or less when blood moons are turned off.
        /// </summary>
        public int BloodMoonDay { get; }

        /// <summary>The hour the blood moon starts on its day.</summary>
        public int DuskHour { get; }

        /// <summary>The hour the blood moon ends on the morning after.</summary>
        public int DawnHour { get; }

        private GameClock(int day, int hour, int minute, int bloodMoonDay, int duskHour, int dawnHour)
        {
            Day = day;
            Hour = hour;
            Minute = minute;
            BloodMoonDay = bloodMoonDay;
            DuskHour = duskHour;
            DawnHour = dawnHour;
        }

        /// <param name="worldTime">World.worldTime.</param>
        /// <param name="bloodMoonDay">The BloodMoonDay game stat: the day the next blood moon falls on.</param>
        /// <param name="dayLightLength">The DayLightLength game stat, in hours.</param>
        public static GameClock FromWorldTime(ulong worldTime, int bloodMoonDay, int dayLightLength)
        {
            var (dusk, dawn) = DuskDawnHours(dayLightLength);
            return new GameClock(
                day: (int)(worldTime / TicksPerDay + 1),
                hour: (int)(worldTime / 1000 % 24),
                minute: (int)(worldTime % 1000 * 60 / 1000),
                bloodMoonDay: bloodMoonDay,
                duskHour: dusk,
                dawnHour: dawn);
        }

        /// <summary>
        /// The running game's clock, or null while there's no world (the
        /// server is still starting or shutting down).
        /// </summary>
        public static GameClock? ReadLive()
        {
            var world = GameManager.Instance?.World;
            if (world == null) return null;

            return FromWorldTime(
                world.worldTime,
                GameStats.GetInt(GameCompat.Stat(nameof(EnumGameStats.BloodMoonDay))),
                GameStats.GetInt(GameCompat.Stat(nameof(EnumGameStats.DayLightLength))));
        }

        /// <summary>The time of day as the HUD shows it, e.g. "07:05".</summary>
        public string TimeText => $"{Hour:D2}:{Minute:D2}";

        /// <summary>
        /// True from dusk on the blood moon day until dawn the next morning,
        /// matching GameUtils.IsBloodMoonTime.
        /// </summary>
        public bool IsBloodMoonNow =>
            Day == BloodMoonDay
                ? Hour >= DuskHour
                : Day > 1 && Day == BloodMoonDay + 1 && Hour < DawnHour;

        /// <summary>
        /// A short line about the next blood moon, for players: whether it's
        /// on now, tonight, or how many days away.
        /// </summary>
        public string DescribeBloodMoon()
        {
            if (IsBloodMoonNow)
                return $"Happening now! It ends at {DawnHour:D2}:00.";
            if (BloodMoonDay <= 0)
                return "Off on this server";

            var daysUntil = BloodMoonDay - Day;
            if (daysUntil == 0)
                return $"Tonight at {DuskHour:D2}:00";
            if (daysUntil == 1)
                return $"Tomorrow night (Day {BloodMoonDay})";
            if (daysUntil > 1)
                return $"Day {BloodMoonDay}, in {daysUntil} days";

            // The stat lags for a moment after a blood moon while the game
            // picks the next day.
            return "Not scheduled yet";
        }

        /// <summary>
        /// The game's GameUtils.CalcDuskDawnHours: dusk is 22:00 unless the
        /// day is long enough or short enough to move it.
        /// </summary>
        public static (int Dusk, int Dawn) DuskDawnHours(int dayLightLength)
        {
            if (dayLightLength == 0 || dayLightLength == 24)
                return (22, 4);

            var dusk = 22;
            if (dayLightLength > 22)
                dusk = Clamp(dayLightLength, 0, 23);
            if (dayLightLength < 18)
                dusk = 12 + dayLightLength / 2;

            return (dusk, Clamp(dusk - dayLightLength, 0, 23));
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    }
}
