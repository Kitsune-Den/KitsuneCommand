namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// Maps to the points_info SQLite table.
    /// Tracks player economy points and daily sign-in status.
    /// </summary>
    public class PointsInfo
    {
        /// <summary>Player ID (text, primary key — same as game player ID).</summary>
        public string Id { get; set; }
        public string CreatedAt { get; set; }
        public string PlayerName { get; set; }
        public int Points { get; set; }
        public string LastSignInAt { get; set; }
    }
}
