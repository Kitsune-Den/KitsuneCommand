namespace KitsuneCommand.Features
{
    /// <summary>
    /// Configuration settings for the Teleport feature.
    /// </summary>
    public class TeleportSettings
    {
        /// <summary>
        /// Countdown in seconds before a player's own /home or /tp moves them (0 = instant).
        /// The panel's admin and moderator teleports are always instant. See TeleportCountdown.
        /// </summary>
        public int TeleportDelaySeconds { get; set; } = 5;

        /// <summary>Default points cost for newly created city waypoints.</summary>
        public int DefaultPointsCost { get; set; } = 0;

        /// <summary>Whether players can teleport during blood moon events.</summary>
        public bool AllowTeleportDuringBloodMoon { get; set; } = true;
    }
}
