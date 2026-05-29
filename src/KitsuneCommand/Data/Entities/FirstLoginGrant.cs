namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// Maps to the first_login_grants table. One row per player, ever — its
    /// presence means the first-time-login item pack has already been delivered.
    /// </summary>
    public class FirstLoginGrant
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string GrantedAt { get; set; }
    }
}
