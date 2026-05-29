using System.Collections.Generic;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// One recurring gift granted to a VIP tier. The feature clones the named
    /// VIP-gift template (a vip_gifts row under the sentinel "_template_" player_id)
    /// into a repeatable vip_gifts row for each player in <see cref="Tier"/>, then
    /// the existing /vip claim flow handles the daily/weekly cadence.
    /// </summary>
    public class TierGiftRule
    {
        /// <summary>Tier name this rule applies to. Must match an entry in VipPerksSettings.Tiers.</summary>
        public string Tier { get; set; } = "";

        /// <summary>Name of the VIP-gift template (player_id = "_template_") to clone.</summary>
        public string TemplateName { get; set; } = "";

        /// <summary>Claim cadence for the cloned gift: "daily", "weekly", or "monthly".</summary>
        public string Period { get; set; } = "weekly";
    }

    /// <summary>
    /// Settings for the VipPerks feature — covers both board items:
    ///   #233 a one-time pack auto-delivered on a player's first-ever spawn, and
    ///   #234 recurring daily/weekly gifts gated by an admin-assigned VIP tier.
    ///
    /// Persisted as a JSON blob in the `settings` table under key "VipPerks",
    /// same pattern as VoteRewardsSettings.
    /// </summary>
    public class VipPerksSettings
    {
        /// <summary>Master toggle. When off, neither the first-login pack nor tier gifts run.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Admin-defined tier names (e.g. ["VIP", "VIP+"]). A player whose
        /// player_metadata.vip_tier is null / empty / not in this list is a "pleb"
        /// and receives no tier gifts. Order is display-only.
        /// </summary>
        public List<string> Tiers { get; set; } = new List<string>();

        // ─── #233 first-time-login pack ──────────────────────────────────

        /// <summary>When true, a player's first-ever spawn auto-delivers FirstLoginTemplateName.</summary>
        public bool FirstLoginPackEnabled { get; set; } = false;

        /// <summary>
        /// VIP-gift template (player_id = "_template_") whose items + commands are
        /// delivered directly into the player's inventory on first spawn. Items are
        /// dropped at the player's feet; linked commands run with {entityId}/{playerId}/{playerName}
        /// substitution. Unlike normal VIP gifts there is no /vip claim step.
        /// </summary>
        public string FirstLoginTemplateName { get; set; } = "";

        /// <summary>
        /// Optional private message sent to the player when the pack lands.
        /// Tokens: {player}. Empty = silent.
        /// </summary>
        public string FirstLoginMessage { get; set; } = "";

        // ─── #234 recurring tier gifts ───────────────────────────────────

        /// <summary>
        /// Tier → recurring gift rules. Evaluated when a VIP player spawns; the
        /// matching templates are assigned (once) as repeatable vip_gifts rows.
        /// </summary>
        public List<TierGiftRule> TierGifts { get; set; } = new List<TierGiftRule>();
    }
}
