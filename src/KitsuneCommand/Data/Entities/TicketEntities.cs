namespace KitsuneCommand.Data.Entities
{
    public class Ticket
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string Subject { get; set; }
        public string Status { get; set; } // open, in_progress, closed
        public int Priority { get; set; } // 0=low, 1=normal, 2=high
        public string AssignedTo { get; set; }
    }

    public class TicketMessage
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public int TicketId { get; set; }
        public string SenderType { get; set; } // player, admin
        public string SenderId { get; set; }
        public string SenderName { get; set; }
        public string Message { get; set; }
        public int Delivered { get; set; } // 0=pending, 1=delivered
    }
}
