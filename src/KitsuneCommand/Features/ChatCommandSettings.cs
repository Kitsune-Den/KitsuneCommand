namespace KitsuneCommand.Features
{
    /// <summary>
    /// Configuration settings for the in-game chat commands feature.
    /// Persisted as JSON in the settings table (key: "ChatCommands").
    /// </summary>
    public class ChatCommandSettings
    {
        /// <summary>Master toggle for all chat commands.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Command prefix (default "/").</summary>
        public string Prefix { get; set; } = "/";

        /// <summary>Default cooldown in seconds for commands without a specific cooldown.</summary>
        public int DefaultCooldownSeconds { get; set; } = 5;

        // ── Home Commands ──────────────────────────────────────────

        /// <summary>Enable /home, /sethome, /delhome, /homes commands.</summary>
        public bool HomeEnabled { get; set; } = true;

        /// <summary>Maximum number of home locations per player.</summary>
        public int MaxHomesPerPlayer { get; set; } = 3;

        /// <summary>Cooldown in seconds for /home teleport.</summary>
        public int HomeCooldownSeconds { get; set; } = 30;

        // ── Teleport Commands ──────────────────────────────────────

        /// <summary>Enable /tp and /cities commands.</summary>
        public bool TeleportEnabled { get; set; } = true;

        /// <summary>Cooldown in seconds for /tp teleport.</summary>
        public int TeleportCooldownSeconds { get; set; } = 30;

        // ── Points Commands ────────────────────────────────────────

        /// <summary>Enable /points and /signin commands.</summary>
        public bool PointsEnabled { get; set; } = true;

        // ── Store Commands ─────────────────────────────────────────

        /// <summary>Enable /shop and /buy commands.</summary>
        public bool StoreEnabled { get; set; } = true;

        // ── VIP Commands ──────────────────────────────────────────

        /// <summary>Enable /vip command for claiming VIP gifts.</summary>
        public bool VipEnabled { get; set; } = true;

        // ── Ticket Commands ─────────────────────────────────────────

        /// <summary>Enable /ticket and /tickets commands.</summary>
        public bool TicketEnabled { get; set; } = true;

        /// <summary>Cooldown in seconds for creating tickets.</summary>
        public int TicketCooldownSeconds { get; set; } = 60;

        // ── Vote Reward Commands ──────────────────────────────────

        /// <summary>Enable /vote command for on-demand vote-reward claims.</summary>
        public bool VoteEnabled { get; set; } = true;

        /// <summary>Cooldown in seconds for /vote (default 30 — listing-site APIs are rate-limited).</summary>
        public int VoteCooldownSeconds { get; set; } = 30;
    }
}
