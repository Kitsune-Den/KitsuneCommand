using System.Collections.Generic;

namespace KitsuneCommand.Abstractions.Models
{
    public class GameAwakeEvent { }

    public class GameStartDoneEvent { }

    public class GameShutdownEvent { }

    public class PlayerLoginEvent
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int EntityId { get; set; }
        public string PlatformId { get; set; }
        public string Ip { get; set; }
    }

    public class PlayerSpawnedEvent
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int EntityId { get; set; }
        public RespawnType RespawnType { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
    }

    public enum RespawnType
    {
        NewGame = 0,
        LoadedGame = 1,
        Died = 2,
        Teleport = 3,
        EnterMultiplayer = 4,
        JoinMultiplayer = 5
    }

    public class PlayerDisconnectedEvent
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int EntityId { get; set; }
    }

    public class PlayerSpawningEvent
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int EntityId { get; set; }
    }

    public class SavePlayerDataEvent
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int EntityId { get; set; }
    }

    public class EntitySpawnedEvent
    {
        public int EntityId { get; set; }
        public string EntityName { get; set; }
        public string EntityType { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
    }

    public class EntityKilledEvent
    {
        public int DeadEntityId { get; set; }
        public string DeadEntityName { get; set; }
        public int KillerEntityId { get; set; }
        public string KillerName { get; set; }
    }

    public class SkyChangedEvent
    {
        public int Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
        public bool IsBloodMoon { get; set; }
    }

    public class LogCallbackEvent
    {
        public string Message { get; set; }
        public string LogLevel { get; set; }
    }

    public class PlayersPositionUpdateEvent
    {
        public List<PlayerPositionData> Players { get; set; } = new List<PlayerPositionData>();
    }

    public class PlayerPositionData
    {
        public int EntityId { get; set; }
        public string PlayerName { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public class PointsUpdateEvent
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int Points { get; set; }
        public int Change { get; set; }
        public string Reason { get; set; }
    }

    public class BloodMoonVoteUpdateEvent
    {
        public bool IsActive { get; set; }
        public int CurrentVotes { get; set; }
        public int RequiredVotes { get; set; }
        public int TotalOnline { get; set; }
        public int BloodMoonDay { get; set; }
    }

    public class TicketCreatedEvent
    {
        public int TicketId { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string Subject { get; set; }
    }

    public class TicketUpdatedEvent
    {
        public int TicketId { get; set; }
        public string Status { get; set; }
        public string UpdatedBy { get; set; }
    }
}
