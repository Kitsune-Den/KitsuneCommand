namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// One row of the suspicion log: something a detector saw that an admin
    /// should look at. Never acted on automatically.
    /// </summary>
    public class SuspicionEvent
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int? EntityId { get; set; }
        public string Detector { get; set; } // inventory, movement, progression, killrate
        public string Rule { get; set; }
        public string Severity { get; set; } // low, medium, high
        public string Summary { get; set; }
        public string Details { get; set; } // JSON
        public double? PosX { get; set; }
        public double? PosY { get; set; }
        public double? PosZ { get; set; }
        public bool Reviewed { get; set; }
        public string ReviewedBy { get; set; }
        public string ReviewedAt { get; set; }
        public string ReviewNote { get; set; }
    }

    /// <summary>Filters for listing the suspicion log. Null/empty means "any".</summary>
    public class SuspicionEventFilter
    {
        public string Search { get; set; } // player name or id
        public string Detector { get; set; }
        public string Severity { get; set; }
        public bool? Reviewed { get; set; }
    }
}
