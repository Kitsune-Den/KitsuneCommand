namespace KitsuneCommand.Abstractions.Models
{
    public class ChatMessageEvent
    {
        public string PlayerId { get; set; }
        public int EntityId { get; set; }
        public string SenderName { get; set; }
        public ChatType ChatType { get; set; }
        public string Message { get; set; }
    }

    public enum ChatType
    {
        Global = 0,
        Friends = 1,
        Party = 2,
        Whisper = 3
    }
}
