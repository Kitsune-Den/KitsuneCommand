namespace KitsuneCommand.Abstractions.Models
{
    public class PlayerInfo
    {
        public string PlayerId { get; set; }
        public int EntityId { get; set; }
        public string PlayerName { get; set; }
        public string PlatformId { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public int Level { get; set; }
        public float Health { get; set; }
        public float Stamina { get; set; }
        public int ZombieKills { get; set; }
        public int PlayerKills { get; set; }
        public int Deaths { get; set; }
        public float TotalPlayTime { get; set; }
        public string Ip { get; set; }
        public bool IsOnline { get; set; }
    }
}
