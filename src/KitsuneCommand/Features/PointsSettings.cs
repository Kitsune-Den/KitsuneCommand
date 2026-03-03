namespace KitsuneCommand.Features
{
    /// <summary>
    /// Configuration settings for the Points economy feature.
    /// </summary>
    public class PointsSettings
    {
        /// <summary>Points awarded for killing a zombie.</summary>
        public int ZombieKillPoints { get; set; } = 5;

        /// <summary>Points awarded for killing another player (PvP).</summary>
        public int PlayerKillPoints { get; set; } = 10;

        /// <summary>Points awarded for daily sign-in bonus.</summary>
        public int SignInBonus { get; set; } = 100;

        /// <summary>Points awarded per hour of playtime.</summary>
        public int PlaytimePointsPerHour { get; set; } = 20;

        /// <summary>Interval in minutes between playtime point awards.</summary>
        public int PlaytimeIntervalMinutes { get; set; } = 10;
    }
}
