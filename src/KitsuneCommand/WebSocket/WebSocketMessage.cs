namespace KitsuneCommand.WebSocket
{
    /// <summary>
    /// Message envelope for WebSocket broadcasts.
    /// </summary>
    public class WebSocketMessage
    {
        public string EventType { get; set; }
    }

    /// <summary>
    /// Generic WebSocket message with a typed data payload.
    /// </summary>
    public class WebSocketMessage<T> : WebSocketMessage
    {
        public T Data { get; set; }
    }
}
