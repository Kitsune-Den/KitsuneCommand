namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// Maps to the cd_keys SQLite table. Redeemable codes that deliver items/commands.
    /// </summary>
    public class CdKey
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public string Key { get; set; }
        public int MaxRedeemCount { get; set; }
        public string ExpiryAt { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Maps to the cd_key_redeem_records SQLite table. Tracks which players redeemed which keys.
    /// </summary>
    public class CdKeyRedeemRecord
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; }
        public int CdKeyId { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
    }
}
