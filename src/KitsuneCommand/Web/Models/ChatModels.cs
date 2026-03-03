namespace KitsuneCommand.Web.Models
{
    public class SendChatRequest
    {
        public string Message { get; set; }

        /// <summary>
        /// Optional target player name/id for private messages.
        /// When null, sends a global message.
        /// </summary>
        public string TargetPlayer { get; set; }
    }
}
