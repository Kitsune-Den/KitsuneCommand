namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// Maps to the chat_records SQLite table.
    /// </summary>
    public class ChatRecord
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string PlayerId { get; set; }
        public int EntityId { get; set; }
        public string SenderName { get; set; }
        public int ChatType { get; set; }
        public string Message { get; set; }
    }
}
