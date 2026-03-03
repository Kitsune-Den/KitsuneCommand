namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// Maps to the city_locations SQLite table. Admin-defined teleport destinations.
    /// </summary>
    public class CityLocation
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string CityName { get; set; }
        public int PointsRequired { get; set; }
        public string Position { get; set; }
        public string ViewDirection { get; set; }
    }

    /// <summary>
    /// Maps to the home_locations SQLite table. Player-saved home teleport points.
    /// </summary>
    public class HomeLocation
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string HomeName { get; set; }
        public string Position { get; set; }
    }

    /// <summary>
    /// Maps to the tele_records SQLite table. Teleport audit log.
    /// </summary>
    public class TeleRecord
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int TargetType { get; set; }
        public string TargetName { get; set; }
        public string OriginPosition { get; set; }
        public string TargetPosition { get; set; }
    }
}
