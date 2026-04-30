using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features.VoteRewards;
using KitsuneCommand.Features.VoteRewards.Providers;
// VipGift template-clone is deferred to v1.5; the dispatch case currently throws
// NotImplementedException, so we don't carry the entity import yet.
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Pulls votes from configured listing-site APIs (7daystodie-servers.com, etc.)
    /// and grants the configured reward to each voter.
    ///
    /// Two trigger paths:
    ///   • Background sweep — every 60 seconds the timer ticks; per-provider
    ///     poll cadence is honored via a per-provider lastSweepAt watermark.
    ///   • Player-initiated /vote — the chat command calls TryClaimForPlayerAsync
    ///     directly so the player sees the reward immediately without waiting
    ///     for the next sweep window.
    ///
    /// Both paths share the same TryGrantOnceAsync core, which is idempotent
    /// against the vote_grants unique index — racing sweeps and /vote commands
    /// can't double-grant.
    /// </summary>
    public class VoteRewardsFeature : FeatureBase<VoteRewardsSettings>
    {
        private readonly ISettingsRepository _settingsRepo;
        private readonly IVoteGrantRepository _grantRepo;
        private readonly IPointsRepository _pointsRepo;
        private readonly LivePlayerManager _playerManager;
        private const string SettingsKey = "VoteRewards";

        // Sweep loop state ────────────────────────────────────────────────
        private Timer _sweepTimer;
        private int _sweepInFlight; // 0/1 guard, manipulated via Interlocked
        private readonly Dictionary<string, DateTime> _lastSweepAt = new Dictionary<string, DateTime>();

        // Provider registry — adapters are stateless, constructed once on enable.
        // Adding a new provider = add an entry here (and its class). Future-Ada
        // can extract this to a discovery pattern when the list grows past ~5.
        private readonly Dictionary<string, IVoteSiteProvider> _providers
            = new Dictionary<string, IVoteSiteProvider>(StringComparer.OrdinalIgnoreCase);

        public VoteRewardsFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ISettingsRepository settingsRepo,
            IVoteGrantRepository grantRepo,
            IPointsRepository pointsRepo,
            LivePlayerManager playerManager)
            : base(eventBus, config)
        {
            _settingsRepo = settingsRepo;
            _grantRepo = grantRepo;
            _pointsRepo = pointsRepo;
            _playerManager = playerManager;
        }

        protected override void OnEnable()
        {
            LoadPersistedSettings();
            RegisterProviders();

            // 60-second tick. Per-provider PollIntervalMinutes gates the actual sweeps.
            _sweepTimer = new Timer(OnSweepTick, null, 30_000, 60_000);

            var enabledCount = Settings.Providers.Count(p => p.Enabled);
            Log.Out($"[KitsuneCommand] VoteRewards feature enabled. Master={Settings.Enabled}, providers configured={Settings.Providers.Count}, enabled={enabledCount}.");
        }

        protected override void OnDisable()
        {
            _sweepTimer?.Dispose();
            _sweepTimer = null;
            Log.Out("[KitsuneCommand] VoteRewards feature disabled.");
        }

        /// <summary>
        /// Updates settings in memory, persists to database, and re-applies provider toggles.
        /// No restart required; the next sweep tick will see the new config.
        /// </summary>
        public void UpdateSettings(VoteRewardsSettings newSettings)
        {
            Settings = newSettings ?? new VoteRewardsSettings();

            try
            {
                var json = JsonConvert.SerializeObject(Settings);
                _settingsRepo.Set(SettingsKey, json);

                var enabledCount = Settings.Providers.Count(p => p.Enabled);
                Log.Out($"[KitsuneCommand] VoteRewards settings updated. Master={Settings.Enabled}, enabled providers={enabledCount}.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to persist vote-rewards settings: {ex.Message}");
            }
        }

        // ─── Public claim entry point (used by /vote chat command) ────────

        /// <summary>
        /// Result envelope for the /vote command's per-provider attempt.
        /// </summary>
        public class ClaimResult
        {
            public string ProviderKey { get; set; }
            public string ProviderDisplayName { get; set; }
            public ClaimOutcome Outcome { get; set; }
            public string Message { get; set; }
        }

        public enum ClaimOutcome
        {
            FeatureDisabled,
            ProviderDisabled,
            NotConfigured,
            NoVote,
            AlreadyClaimed,
            Granted,
            Error
        }

        /// <summary>
        /// Synchronously checks all enabled providers for a player and grants any
        /// pending reward. Designed for the /vote command — small N and the
        /// player is staring at their chat waiting for a reply.
        /// </summary>
        public async Task<List<ClaimResult>> TryClaimForPlayerAsync(string steamId, string playerName)
        {
            var results = new List<ClaimResult>();
            if (!Settings.Enabled)
            {
                results.Add(new ClaimResult { Outcome = ClaimOutcome.FeatureDisabled, Message = "Vote rewards are disabled on this server." });
                return results;
            }

            foreach (var cfg in Settings.Providers)
            {
                if (!cfg.Enabled)
                {
                    results.Add(new ClaimResult
                    {
                        ProviderKey = cfg.Key,
                        Outcome = ClaimOutcome.ProviderDisabled,
                        Message = $"{cfg.Key}: provider disabled"
                    });
                    continue;
                }

                if (!_providers.TryGetValue(cfg.Key, out var provider))
                {
                    results.Add(new ClaimResult
                    {
                        ProviderKey = cfg.Key,
                        Outcome = ClaimOutcome.NotConfigured,
                        Message = $"{cfg.Key}: no adapter registered"
                    });
                    continue;
                }

                results.Add(await TryGrantOnceAsync(provider, cfg, steamId, playerName, source: "command").ConfigureAwait(false));
            }

            return results;
        }

        // ─── Sweep loop ───────────────────────────────────────────────────

        private void OnSweepTick(object _)
        {
            if (!Settings.Enabled) return;

            // Re-entrancy guard. If a previous sweep is still running (e.g. the
            // listing site's API stalled past 60s), skip this tick rather than
            // pile on overlapping work. Idempotency would catch double-grants
            // anyway, but skipping is cheaper.
            if (Interlocked.CompareExchange(ref _sweepInFlight, 1, 0) != 0) return;

            // Don't block the timer thread on async I/O — kick off the work
            // and let it complete on its own. Errors are caught inside RunSweepAsync.
            _ = Task.Run(async () =>
            {
                try
                {
                    await RunSweepAsync().ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _sweepInFlight, 0);
                }
            });
        }

        private async Task RunSweepAsync()
        {
            var now = DateTime.UtcNow;

            foreach (var cfg in Settings.Providers)
            {
                if (!cfg.Enabled) continue;
                if (!_providers.TryGetValue(cfg.Key, out var provider)) continue;

                // Per-provider cadence: skip if not enough time has elapsed since
                // the last sweep for this specific provider. Default 5 min.
                var minutes = cfg.PollIntervalMinutes <= 0 ? 5 : cfg.PollIntervalMinutes;
                if (_lastSweepAt.TryGetValue(cfg.Key, out var last)
                    && (now - last).TotalMinutes < minutes)
                {
                    continue;
                }
                _lastSweepAt[cfg.Key] = now;

                try
                {
                    var voters = await provider.ListRecentVotersAsync(cfg).ConfigureAwait(false);
                    foreach (var voter in voters)
                    {
                        if (string.IsNullOrWhiteSpace(voter.SteamId)) continue;

                        // Cheap pre-check before hitting the claim-status endpoint.
                        // If we've already granted today, skip without a network call.
                        var voteDate = string.IsNullOrEmpty(voter.VoteDate)
                            ? now.ToString("yyyy-MM-dd")
                            : voter.VoteDate;

                        if (_grantRepo.HasGrantForDate(provider.Key, voter.SteamId, voteDate))
                            continue;

                        await TryGrantOnceAsync(provider, cfg, voter.SteamId, voter.Nickname, source: "sweep").ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    // Network blips are expected. Log at WARN, leave _lastSweepAt
                    // updated so we don't hammer a flapping endpoint, and let the
                    // next interval retry.
                    Log.Warning($"[KitsuneCommand] VoteRewards: sweep error for {cfg.Key}: {ex.Message}");
                }
            }
        }

        // ─── Core grant primitive (shared by sweep + /vote) ───────────────

        /// <summary>
        /// Single-vote grant attempt. Idempotent against vote_grants. Returns a
        /// ClaimResult describing what happened, suitable for logging or for
        /// reporting back to a chat-command caller.
        /// </summary>
        private async Task<ClaimResult> TryGrantOnceAsync(
            IVoteSiteProvider provider,
            VoteProviderSettings cfg,
            string steamId,
            string playerName,
            string source)
        {
            var result = new ClaimResult
            {
                ProviderKey = provider.Key,
                ProviderDisplayName = provider.DisplayName
            };

            try
            {
                var status = await provider.GetClaimStatusAsync(cfg, steamId).ConfigureAwait(false);
                switch (status)
                {
                    case VoteClaimStatus.Unvoted:
                        result.Outcome = ClaimOutcome.NoVote;
                        result.Message = $"{provider.DisplayName}: no recent vote on file";
                        return result;

                    case VoteClaimStatus.VotedClaimed:
                        result.Outcome = ClaimOutcome.AlreadyClaimed;
                        result.Message = $"{provider.DisplayName}: vote already claimed";
                        return result;

                    case VoteClaimStatus.VotedUnclaimed:
                        // fallthrough into grant
                        break;
                }

                // STEP 1: insert the audit/idempotency row FIRST. If this fails
                // because another sweep beat us to it, abort — we MUST NOT also
                // grant the reward, even if the API still says "unclaimed."
                var voteDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var grant = new VoteGrant
                {
                    Provider = provider.Key,
                    SteamId = steamId,
                    PlayerName = playerName,
                    VoteDate = voteDate,
                    RewardType = cfg.RewardType ?? VoteRewardType.Points,
                    RewardValue = SerializeRewardValue(cfg),
                    Notes = source
                };
                if (!_grantRepo.TryInsert(grant))
                {
                    result.Outcome = ClaimOutcome.AlreadyClaimed;
                    result.Message = $"{provider.DisplayName}: already granted by an earlier sweep";
                    return result;
                }

                // STEP 2: grant the actual reward. If this fails the audit row
                // is still in place — admin can see the failure in the log and
                // intervene manually rather than the player getting a silent
                // double-grant on retry.
                var grantedDescription = DispatchReward(steamId, playerName, cfg);

                // STEP 3: tell the listing site we delivered. Failure here is
                // not catastrophic — next status check returns "unclaimed" again,
                // but our vote_grants row will short-circuit a duplicate grant.
                var marked = await provider.MarkClaimedAsync(cfg, steamId).ConfigureAwait(false);
                if (!marked)
                {
                    Log.Warning($"[KitsuneCommand] VoteRewards: failed to mark claimed at {provider.DisplayName} for {steamId}; will retry next poll.");
                }

                // STEP 4: optional in-game broadcast (only if the player is online).
                MaybeBroadcast(cfg, steamId, playerName, grantedDescription);

                result.Outcome = ClaimOutcome.Granted;
                result.Message = $"{provider.DisplayName}: granted {grantedDescription}";

                Log.Out($"[KitsuneCommand] VoteRewards: granted to {steamId} ({playerName ?? "?"}) via {provider.Key} — {grantedDescription} [{source}]");
                return result;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] VoteRewards: grant error ({provider.Key}, steamId={steamId}): {ex.Message}");
                result.Outcome = ClaimOutcome.Error;
                result.Message = $"{provider.DisplayName}: error — {ex.Message}";
                return result;
            }
        }

        // ─── Reward dispatch ──────────────────────────────────────────────

        /// <summary>
        /// Hands the configured reward to the player. Returns a short human-readable
        /// description for log messages and the {reward} broadcast token.
        /// </summary>
        private string DispatchReward(string steamId, string playerName, VoteProviderSettings cfg)
        {
            var playerId = ToPlayerId(steamId);
            var resolvedName = string.IsNullOrWhiteSpace(playerName) ? "(voter)" : playerName;

            switch (cfg.RewardType)
            {
                case VoteRewardType.Points:
                    {
                        // Make sure they have a row before adjusting; UpsertPlayer is a no-op
                        // if one exists. Players who've never connected will have a row
                        // pre-seeded with their best-known name.
                        _pointsRepo.UpsertPlayer(playerId, resolvedName);
                        var newBalance = _pointsRepo.AdjustPoints(playerId, cfg.PointsAmount);
                        return $"{cfg.PointsAmount} points (balance: {newBalance})";
                    }

                case VoteRewardType.VipGift:
                    // Reserved for v1.5 — needs a template-clone path: look up the
                    // template VIP gift by name, insert a copy for this voter, and
                    // mirror its item + command junctions into the new row. The
                    // shape and storage are ready; the clone wiring is pending.
                    // Admins selecting this today get an explicit error rather
                    // than a silent no-op.
                    throw new NotImplementedException("VIP-gift vote rewards are not yet wired up. Use Points for now.");

                case VoteRewardType.CdKey:
                    // Reserved — needs CdKey template lookup wiring. Leaving the
                    // pathway in place so admins choosing this in settings get a
                    // clear error rather than silent skip.
                    throw new NotImplementedException("CD-key vote rewards are not yet wired up. Use Points for now.");

                default:
                    throw new InvalidOperationException($"Unknown reward type: {cfg.RewardType}");
            }
        }

        /// <summary>
        /// 7D2D's internal player id is "Steam_76561198..." form (CrossplatformId),
        /// while listing-site APIs deal in raw 76-digit SteamID64s. Bridge them
        /// by prepending the platform prefix. Epic-only voters aren't supported
        /// for v1; the listing site sends Steam IDs.
        /// </summary>
        private static string ToPlayerId(string steamId) => $"Steam_{steamId}";

        /// <summary>
        /// Serializes the reward config's payload to a string for the audit log.
        /// We don't store the full settings blob — just enough that an admin
        /// reading the log can tell what was given.
        /// </summary>
        private static string SerializeRewardValue(VoteProviderSettings cfg)
        {
            switch (cfg.RewardType)
            {
                case VoteRewardType.Points: return cfg.PointsAmount.ToString();
                case VoteRewardType.VipGift: return cfg.VipGiftTemplateName ?? "";
                case VoteRewardType.CdKey: return cfg.CdKeyTemplateId.ToString();
                default: return cfg.RewardType ?? "";
            }
        }

        /// <summary>
        /// If the broadcast template is set and the voter happens to be online,
        /// say something to the whole server. Silent if either condition fails.
        /// We never block on this — failure is logged but doesn't unwind the grant.
        /// </summary>
        private void MaybeBroadcast(VoteProviderSettings cfg, string steamId, string playerName, string rewardDescription)
        {
            if (string.IsNullOrWhiteSpace(cfg.BroadcastTemplate)) return;

            try
            {
                var platformId = $"Steam_{steamId}";
                var online = _playerManager.GetAllOnline()
                    .FirstOrDefault(p => string.Equals(p.PlatformId, platformId, StringComparison.OrdinalIgnoreCase));
                if (online == null) return;

                var msg = cfg.BroadcastTemplate
                    .Replace("{player}", string.IsNullOrWhiteSpace(playerName) ? online.PlayerName : playerName)
                    .Replace("{reward}", rewardDescription);

                // Server-wide broadcast via `say`. Marshal to game thread.
                ModEntry.MainThreadContext.Post(_ =>
                {
                    try
                    {
                        SdtdConsole.Instance.ExecuteSync($"say \"{msg.Replace("\"", "'")}\"", null);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[KitsuneCommand] VoteRewards: broadcast failed: {ex.Message}");
                    }
                }, null);
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] VoteRewards: broadcast lookup failed: {ex.Message}");
            }
        }

        // ─── Bootstrap helpers ────────────────────────────────────────────

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<VoteRewardsSettings>(json);
                    if (loaded != null)
                    {
                        // If a saved blob predates a provider being added, ensure the
                        // default entry is still present so the UI has something to render.
                        EnsureDefaultProviders(loaded);
                        Settings = loaded;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load vote-rewards settings, using defaults: {ex.Message}");
            }
        }

        private static void EnsureDefaultProviders(VoteRewardsSettings settings)
        {
            if (settings.Providers == null) settings.Providers = new List<VoteProviderSettings>();
            if (!settings.Providers.Any(p => string.Equals(p.Key, "7daystodie-servers", StringComparison.OrdinalIgnoreCase)))
            {
                settings.Providers.Add(new VoteProviderSettings { Key = "7daystodie-servers", Enabled = false });
            }
        }

        /// <summary>
        /// Constructs adapter instances for each provider key we know about.
        /// The provider list is hardcoded here — adding a new provider means
        /// adding its class and one line in this method.
        /// </summary>
        private void RegisterProviders()
        {
            _providers.Clear();
            _providers["7daystodie-servers"] = new SevenDtdServersProvider();
            // Future: _providers["gtop100"] = new Gtop100Provider();
            // Future: _providers["top-7daystodieservers"] = new TopSevenDtdServersProvider();
        }
    }
}
