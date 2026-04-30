namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// Maps to the vote_grants SQLite table. One row per reward grant from a
    /// vote-listing site. Doubles as audit log and idempotency record — the
    /// (provider, steam_id, vote_date) unique index prevents double-grants
    /// when sweeps overlap or a /vote command races a scheduled sweep.
    /// </summary>
    public class VoteGrant
    {
        public int Id { get; set; }

        /// <summary>Provider key, e.g. "7daystodie-servers". Matches IVoteSiteProvider.Key.</summary>
        public string Provider { get; set; }

        /// <summary>SteamID64 the listing site reported.</summary>
        public string SteamId { get; set; }

        /// <summary>Best-effort player name; may be null if the player has never connected.</summary>
        public string PlayerName { get; set; }

        /// <summary>YYYY-MM-DD UTC of the vote (idempotency component).</summary>
        public string VoteDate { get; set; }

        /// <summary>"points" | "vip_gift" | "cd_key"</summary>
        public string RewardType { get; set; }

        /// <summary>Type-specific payload — e.g. points amount, vip_gift name template, cd_key template name.</summary>
        public string RewardValue { get; set; }

        public string GrantedAt { get; set; }

        /// <summary>Free-form, e.g. "queued for offline player" or "manual /vote claim".</summary>
        public string Notes { get; set; }
    }
}
