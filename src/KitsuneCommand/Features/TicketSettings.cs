namespace KitsuneCommand.Features
{
    public class TicketSettings
    {
        public bool Enabled { get; set; } = true;
        public int MaxOpenTicketsPerPlayer { get; set; } = 3;
        public int CooldownSeconds { get; set; } = 60;
        public string DiscordWebhookUrl { get; set; } = "";
        public bool DiscordNotifyOnCreate { get; set; } = true;
        public bool DiscordNotifyOnReply { get; set; } = true;
        public bool DiscordNotifyOnClose { get; set; } = true;
    }
}
