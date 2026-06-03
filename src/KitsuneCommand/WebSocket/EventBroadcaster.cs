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
        internal static readonly JsonSerializerSettings CamelCase = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        };

        /// <summary>
        /// Thread-local re-entrancy guard for <see cref="LogCallbackEvent"/> only.
        ///
        /// 7DTD routes its Log.* writes through the same logging chain that fires
        /// LogCallbackEvent. If Broadcast() throws while handling a LogCallbackEvent
        /// (typical during shutdown once the WebSocketServer manager has stopped:
        /// "The current state of the manager is not Start.") and we then call
        /// Log.Warning(...) to report it, that Log.Warning fires another
        /// LogCallbackEvent, re-enters this subscriber, throws again, logs again,
        /// and recurses until the stack overflows and KC crashes.
        ///
        /// Observed live on a Windows prod box: a botched GracefulRestart left the
        /// WS manager stopped while the event bus was still alive, and KC logged
        /// "Error in event handler for LogCallbackEvent: The requested operation
        /// caused a stack overflow." dozens of times before the service died.
        ///
        /// Scope is intentionally narrow: other event types don't loop back into
        /// the logger from their failure path, so they keep the normal
        /// Log.Warning-on-failure behavior to surface real broadcast bugs.
        /// </summary>
        [ThreadStatic]
        private static bool _inLogBroadcast;

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
            eventBus.Subscribe<LogCallbackEvent>(BroadcastLogCallback);
            eventBus.Subscribe<PlayersPositionUpdateEvent>(e => Broadcast("PlayersPositionUpdate", e));
            eventBus.Subscribe<PointsUpdateEvent>(e => Broadcast("PointsUpdate", e));
            eventBus.Subscribe<BloodMoonVoteUpdateEvent>(e => Broadcast("BloodMoonVoteUpdate", e));
            eventBus.Subscribe<TicketCreatedEvent>(e => Broadcast("TicketCreated", e));
            eventBus.Subscribe<TicketUpdatedEvent>(e => Broadcast("TicketUpdated", e));
        }

        /// <summary>
        /// Re-entrancy-safe LogCallbackEvent broadcaster.
        ///
        /// If we're already inside a LogCallbackEvent broadcast on this thread,
        /// drop the event silently — anything we'd log here would just fire
        /// another LogCallbackEvent and we'd be back to the recursion bug this
        /// guard exists to prevent. Likewise, if the inner broadcast throws,
        /// we deliberately do NOT route the failure through Log.Warning.
        /// Console.Error.WriteLine is the closest we'll get to surfacing it,
        /// and even that is wasted noise during shutdown — but it's bounded.
        /// </summary>
        internal static void BroadcastLogCallback(LogCallbackEvent e)
        {
            if (_inLogBroadcast) return;

            _inLogBroadcast = true;
            try
            {
                Broadcast("LogCallback", e, suppressFailureLogging: true);
            }
            finally
            {
                _inLogBroadcast = false;
            }
        }

        private static void Broadcast<T>(string eventType, T data, bool suppressFailureLogging = false)
        {
            if (_server == null) return;

            try
            {
                var message = new WebSocketMessage<T>
                {
                    EventType = eventType,
                    Data = data
                };

                var json = JsonConvert.SerializeObject(message, CamelCase);
                _server.WebSocketServices["/kctunnel"]?.Sessions?.Broadcast(json);
            }
            catch (Exception ex)
            {
                // Don't let broadcast failures crash the game
                if (suppressFailureLogging)
                {
                    // LogCallbackEvent path: Log.* feeds back into the broadcaster,
                    // so any logger call here is a recursion hazard. Use the bare
                    // Console.Error channel instead — bounded and won't re-enter.
                    try
                    {
                        Console.Error.WriteLine(
                            $"[KitsuneCommand] Broadcast error for {eventType}: {ex.Message}");
                    }
                    catch
                    {
                        // Last-resort: stderr itself failed. Nothing safe left to do.
                    }
                }
                else
                {
                    Log.Warning($"[KitsuneCommand] Broadcast error for {eventType}: {ex.Message}");
                }
            }
        }
    }
}
