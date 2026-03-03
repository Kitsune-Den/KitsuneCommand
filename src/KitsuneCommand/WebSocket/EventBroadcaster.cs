using KitsuneCommand.Core;
using WebSocketSharp.Server;

namespace KitsuneCommand.WebSocket
{
    /// <summary>
    /// Subscribes to all ModEventBus events and broadcasts them to connected
    /// WebSocket clients as JSON messages.
    /// </summary>
    public static class EventBroadcaster
    {
        private static WebSocketServer _server;

        public static void Initialize(WebSocketServer server, ModEventBus eventBus)
        {
            _server = server;

            // Subscribe to all event types and broadcast them
            eventBus.Subscribe<GameAwakeEvent>(e => Broadcast("GameAwake", e));
            eventBus.Subscribe<GameStartDoneEvent>(e => Broadcast("GameStartDone", e));
            eventBus.Subscribe<GameShutdownEvent>(e => Broadcast("GameShutdown", e));
            eventBus.Subscribe<PlayerLoginEvent>(e => Broadcast("PlayerLogin", e));
            eventBus.Subscribe<PlayerSpawnedEvent>(e => Broadcast("PlayerSpawnedInWorld", e));
            eventBus.Subscribe<PlayerDisconnectedEvent>(e => Broadcast("PlayerDisconnected", e));
            eventBus.Subscribe<PlayerSpawningEvent>(e => Broadcast("PlayerSpawning", e));
            eventBus.Subscribe<SavePlayerDataEvent>(e => Broadcast("SavePlayerData", e));
            eventBus.Subscribe<EntitySpawnedEvent>(e => Broadcast("EntitySpawned", e));
            eventBus.Subscribe<EntityKilledEvent>(e => Broadcast("EntityKilled", e));
            eventBus.Subscribe<ChatMessageEvent>(e => Broadcast("ChatMessage", e));
            eventBus.Subscribe<SkyChangedEvent>(e => Broadcast("SkyChanged", e));
            eventBus.Subscribe<LogCallbackEvent>(e => Broadcast("LogCallback", e));
            eventBus.Subscribe<PlayersPositionUpdateEvent>(e => Broadcast("PlayersPositionUpdate", e));
            eventBus.Subscribe<PointsUpdateEvent>(e => Broadcast("PointsUpdate", e));
        }

        private static void Broadcast<T>(string eventType, T data)
        {
            if (_server == null) return;

            try
            {
                var message = new WebSocketMessage<T>
                {
                    EventType = eventType,
                    Data = data
                };

                var json = JsonConvert.SerializeObject(message);
                _server.WebSocketServices["/ws"]?.Sessions?.Broadcast(json);
            }
            catch (Exception ex)
            {
                // Don't let broadcast failures crash the game
                Log.Warning($"[KitsuneCommand] Broadcast error for {eventType}: {ex.Message}");
            }
        }
    }
}
