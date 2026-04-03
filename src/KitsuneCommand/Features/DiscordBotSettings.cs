namespace KitsuneCommand.Features
{
    public class DiscordBotSettings
    {
        public bool Enabled { get; set; } = false;
        public string BotToken { get; set; } = "";

        // Chat bridge
        public bool ChatBridgeEnabled { get; set; } = true;
        public ulong ChatBridgeChannelId { get; set; }

        // Event notifications
        public bool EventNotificationsEnabled { get; set; } = true;
        public ulong EventChannelId { get; set; }
        public bool NotifyPlayerJoin { get; set; } = true;
        public bool NotifyPlayerLeave { get; set; } = true;
        public bool NotifyServerStart { get; set; } = true;
        public bool NotifyServerStop { get; set; } = true;
        public bool NotifyBloodMoon { get; set; } = true;

        // Slash commands
        public bool SlashCommandsEnabled { get; set; } = true;

        // Display
        public string ServerName { get; set; } = "7 Days to Die Server";
        public bool ShowPlayerCountInStatus { get; set; } = true;
    }
}
