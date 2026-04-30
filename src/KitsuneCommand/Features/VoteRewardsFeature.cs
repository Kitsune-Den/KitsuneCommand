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
        private readonly IVipGiftRepository _vipGiftRepo;
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
            IVipGiftRepository vipGiftRepo,
            LivePlayerManager playerManager)
            : base(eventBus, config)
        {
            _settingsRepo = settingsRepo;
            _grantRepo = grantRepo;
            _pointsRepo = pointsRepo;
            _vipGiftRepo = vipGiftRepo;
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

                // STEP 2: grant the actual reward. If dispatch throws (e.g. an
                // admin typo'd the VIP-gift template name), we MUST roll back
                // the audit row — otherwise HasGrantForDate will short-circuit
                // every future sweep and the player is permanently locked out
                // of claiming, even after the admin fixes the misconfiguration.
                //
                // The narrow try/catch here ensures we only roll back on a
                // dispatch failure, not on the broader try below (which catches
                // network errors from STEP 0 / STEP 3 — those don't leave an
                // orphaned audit row).
                string grantedDescription;
                try
                {
                    grantedDescription = DispatchReward(steamId, playerName, cfg);
                }
                catch (Exception dispatchEx)
                {
                    _grantRepo.DeleteByKey(provider.Key, steamId, voteDate);
                    Log.Warning($"[KitsuneCommand] VoteRewards: dispatch failed for {steamId} via {provider.Key} — rolled back audit row so next sweep can retry. Cause: {dispatchEx.Message}");
                    result.Outcome = ClaimOutcome.Error;
                    result.Message = $"{provider.DisplayName}: dispatch failed — {dispatchEx.Message}";
                    return result;
                }

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
                    {
                        // Clone a pre-built template gift for this voter. The admin
                        // creates the template via the regular VIP Gifts admin UI
                        // by typing the sentinel player_id "_template_" — that keeps
                        // it out of any real player's pending-gift list, but lets
                        // VoteRewards find it by name and copy it.
                        if (string.IsNullOrWhiteSpace(cfg.VipGiftTemplateName))
                        {
                            throw new InvalidOperationException(
                                "VipGiftTemplateName is empty — set the template name in the Vote Rewards settings.");
                        }

                        var template = _vipGiftRepo.GetTemplateByName(cfg.VipGiftTemplateName);
                        if (template == null)
                        {
                            throw new InvalidOperationException(
                                $"No VIP gift template named '{cfg.VipGiftTemplateName}' (player_id = '{VipGiftSentinels.TemplatePlayerId}'). " +
                                $"Create it via the VIP Gifts admin UI first.");
                        }

                        // Read the template's items + commands BEFORE insert, so a
                        // partial failure doesn't leave a half-built gift in the
                        // voter's pending list (the items would be missing).
                        var templateItems = _vipGiftRepo.GetItemsForGift(template.Id).Select(i => i.Id).ToList();
                        var templateCommands = _vipGiftRepo.GetCommandsForGift(template.Id).Select(c => c.Id).ToList();

                        var voterGift = new VipGift
                        {
                            PlayerId = playerId,
                            PlayerName = resolvedName,
                            Name = template.Name,
                            Description = string.IsNullOrWhiteSpace(template.Description)
                                ? $"Vote reward from {cfg.Key}"
                                : template.Description,
                            // One-time gift — claim_period stays null. If a server
                            // wants repeatable vote rewards, that's a per-vote
                            // grant, not a per-claim period.
                            ClaimPeriod = null,
                        };
                        var newId = _vipGiftRepo.Insert(voterGift);
                        _vipGiftRepo.SetGiftItems(newId, templateItems);
                        _vipGiftRepo.SetGiftCommands(newId, templateCommands);

                        var itemCount = templateItems.Count;
                        var commandCount = templateCommands.Count;
                        var summary = itemCount == 0 && commandCount == 0
                            ? "(empty template — fix the template before next vote)"
                            : $"{itemCount} item(s) + {commandCount} command(s)";
                        return $"VIP gift '{template.Name}' [{summary}] (claim with /vip)";
                    }

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
                        Settings = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load vote-rewards settings, using defaults: {ex.Message}");
            }

            // Always run AFTER deserialization, regardless of whether persisted state
            // existed. This is what backfills the default 7daystodie-servers entry
            // for first-time users (whose Settings.Providers starts empty per the
            // VoteRewardsSettings ctor) and is idempotent for users with saved state.
            EnsureDefaultProviders(Settings);
        }

        private static void EnsureDefaultProviders(VoteRewardsSettings settings)
        {
            if (settings.Providers == null) settings.Providers = new List<VoteProviderSettings>();

            // Deduplicate by Key. PR #50 prevented NEW duplicates forming (the
            // Newtonsoft-append bug), but admins whose state was poisoned while
            // that bug was active still carry the doubled list in their saved
            // JSON. Strip duplicates here on every load — the entry with the
            // most "configured" signal wins (enabled + has-api-key > enabled-only
            // > has-api-key > anything else) so a real config is never displaced
            // by an empty stub.
            var beforeCount = settings.Providers.Count;
            settings.Providers = settings.Providers
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Key))
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(p => p.Enabled && !string.IsNullOrEmpty(p.ApiKey))
                    .ThenByDescending(p => p.Enabled)
                    .ThenByDescending(p => !string.IsNullOrEmpty(p.ApiKey))
                    .First())
                .ToList();
            var afterCount = settings.Providers.Count;
            if (afterCount < beforeCount)
            {
                Log.Out($"[KitsuneCommand] VoteRewards: deduped {beforeCount - afterCount} duplicate provider entry/entries on load.");
            }

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
