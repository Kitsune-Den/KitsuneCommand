namespace KitsuneCommand.Features
{
    /// <summary>
    /// Configuration settings for the blood moon vote skip feature.
    /// Persisted as JSON in the settings table (key: "BloodMoonVote").
    /// </summary>
    public class BloodMoonVoteSettings
    {
        /// <summary>Master toggle for blood moon vote skip.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Threshold type: "percentage" or "count".</summary>
        public string ThresholdType { get; set; } = "percentage";

        /// <summary>
        /// Threshold value. For "percentage": 1-100 (percent of online players).
        /// For "count": minimum number of votes needed.
        /// </summary>
        public int ThresholdValue { get; set; } = 60;

        /// <summary>Real-time minutes to wait after a successful skip before another vote can start.</summary>
        public int CooldownMinutes { get; set; } = 0;

        /// <summary>
        /// How many in-game hours before dusk on the blood moon day the vote
        /// opens. Dusk is when the blood moon starts: 22:00 unless
        /// DayLightLength moves it.
        /// </summary>
        public int AllowVoteHoursBefore { get; set; } = 2;

        /// <summary>Whether players can still vote after the blood moon has started at dusk.</summary>
        public bool AllowVoteDuringBloodMoon { get; set; } = true;

        /// <summary>Chat command name (without prefix).</summary>
        public string CommandName { get; set; } = "skipbm";

        // ── Chat Response Templates ──────────────────────────────

        public string VoteRegisteredMessage { get; set; } = "Vote registered to skip blood moon! ({current}/{required})";
        public string AlreadyVotedMessage { get; set; } = "You have already voted to skip this blood moon.";
        public string VoteNotActiveMessage { get; set; } = "No blood moon vote is active right now.";
        public string VoteSuccessMessage { get; set; } = "Vote passed! Skipping the blood moon...";
        public string FeatureDisabledMessage { get; set; } = "Blood moon skip voting is disabled.";
        public string OnCooldownMessage { get; set; } = "Blood moon skip is on cooldown.";
    }
}
