using System.Collections.Generic;
using System.Threading.Tasks;

namespace KitsuneCommand.Features.VoteRewards
{
    /// <summary>
    /// Abstraction over a vote-listing site's API. Each adapter knows how to:
    ///   - list recent voters for our server,
    ///   - check whether a specific voter has an unclaimed reward,
    ///   - mark a vote as claimed once we've granted the reward.
    ///
    /// Adapters are stateless — config is passed in per-call. The feature
    /// holds the config and constructs adapters as needed; we don't DI-register
    /// adapters because there's nothing scoped to keep per-instance.
    /// </summary>
    public interface IVoteSiteProvider
    {
        /// <summary>
        /// Stable provider key, matched against VoteProviderSettings.Key and
        /// VoteGrant.Provider. Lower-case, no spaces. Examples: "7daystodie-servers", "gtop100".
        /// </summary>
        string Key { get; }

        /// <summary>Human-readable name for UI / log messages.</summary>
        string DisplayName { get; }

        /// <summary>Recent voters since the provider's window (typically last ~30 days).</summary>
        Task<IReadOnlyList<VoterInfo>> ListRecentVotersAsync(VoteProviderSettings cfg);

        /// <summary>
        /// Checks the claim status for a single voter.
        /// Returns Unvoted / VotedUnclaimed / VotedClaimed.
        /// </summary>
        Task<VoteClaimStatus> GetClaimStatusAsync(VoteProviderSettings cfg, string steamId);

        /// <summary>
        /// Tells the listing site we've delivered the reward — they flip to "claimed"
        /// and won't return this voter as unclaimed on future polls.
        /// </summary>
        Task<bool> MarkClaimedAsync(VoteProviderSettings cfg, string steamId);
    }

    public enum VoteClaimStatus
    {
        Unvoted = 0,
        VotedUnclaimed = 1,
        VotedClaimed = 2
    }

    /// <summary>
    /// What a provider tells us about a recent voter. SteamId is the canonical
    /// identifier — the rest is best-effort metadata for the audit log.
    /// </summary>
    public class VoterInfo
    {
        public string SteamId { get; set; }
        public string Nickname { get; set; }
        public string VoteDate { get; set; } // YYYY-MM-DD UTC
    }
}
