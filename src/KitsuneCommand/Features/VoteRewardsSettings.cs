using System.Collections.Generic;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Reward delivery shape. Player-visible enum, persisted as a string.
    /// </summary>
    public static class VoteRewardType
    {
        public const string Points = "points";
        public const string VipGift = "vip_gift";
        public const string CdKey = "cd_key";
    }

    /// <summary>
    /// Per-provider configuration. The shape is identical across providers because
    /// the underlying APIs are nearly identical — only the URL/key changes. New
    /// providers just append their own settings instance to ProviderSettings.
    /// </summary>
    public class VoteProviderSettings
    {
        /// <summary>Provider key, e.g. "7daystodie-servers". Must match IVoteSiteProvider.Key.</summary>
        public string Key { get; set; }

        /// <summary>Per-provider enable toggle (lets admin keep config but pause polling).</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>API key issued by the listing site.</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>Server ID on the listing site, if needed (some sites scope their API by serverId).</summary>
        public string ServerId { get; set; } = "";

        /// <summary>How often the sweep task polls this provider, in minutes. Default 5.</summary>
        public int PollIntervalMinutes { get; set; } = 5;

        // ─── Reward config ────────────────────────────────────────────

        /// <summary>One of VoteRewardType.* constants.</summary>
        public string RewardType { get; set; } = VoteRewardType.Points;

        /// <summary>For RewardType=points, the number of points to grant per vote.</summary>
        public int PointsAmount { get; set; } = 100;

        /// <summary>
        /// For RewardType=vip_gift, the gift template name. The feature looks
        /// for a VIP gift with this Name attached to a sentinel "_template_"
        /// player_id, clones it for the voter, and lets them claim with /vip.
        /// </summary>
        public string VipGiftTemplateName { get; set; } = "";

        /// <summary>
        /// For RewardType=cd_key, the CD key template id. Reserved for future
        /// implementation — providers default to Points until CD-key dispatch lands.
        /// </summary>
        public int CdKeyTemplateId { get; set; } = 0;

        // ─── Broadcast ────────────────────────────────────────────────

        /// <summary>
        /// Optional in-game broadcast emitted on grant. Tokens: {player}, {reward}.
        /// Empty = silent grant. Only fires when the player is online.
        /// </summary>
        public string BroadcastTemplate { get; set; } = "";
    }

    /// <summary>
    /// Top-level settings for the VoteRewards feature. Persisted as a JSON blob
    /// in the `settings` table under key "VoteRewards" (same pattern as TraderProtection).
    /// </summary>
    public class VoteRewardsSettings
    {
        /// <summary>Master toggle. When off, no provider polls run regardless of per-provider Enabled.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// One entry per known provider. The feature constructs adapters by Key,
        /// so unknown keys are skipped at runtime with a warning.
        ///
        /// IMPORTANT: this list defaults to empty, NOT to a list with the default
        /// provider entry. Newtonsoft.Json's default deserialization behavior for
        /// a property whose initializer returns a non-empty collection is to
        /// APPEND the JSON-loaded entries instead of replacing — so initializing
        /// here with [{ "7daystodie-servers", disabled }] meant every server boot
        /// loaded the saved entry on top of the constructor-default, doubling
        /// the list. Defaults are now backfilled by VoteRewardsFeature.EnsureDefaultProviders
        /// after deserialization, which is idempotent.
        /// </summary>
        public List<VoteProviderSettings> Providers { get; set; } = new List<VoteProviderSettings>();
    }
}
