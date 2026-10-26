using System.Collections.Generic;
using System.Linq;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Blood moon vote skip feature. Players vote via chat command to skip the blood moon night.
    /// When enough players vote (configurable threshold), the game time is advanced to dawn.
    /// Settings are persisted to the database under the "BloodMoonVote" key.
    /// </summary>
    public class BloodMoonVoteFeature : FeatureBase<BloodMoonVoteSettings>
    {
        private readonly ISettingsRepository _settingsRepo;
        private readonly LivePlayerManager _playerManager;

        private const string SettingsKey = "BloodMoonVote";

        // In-memory vote state (ephemeral — resets on mod reload or blood moon end)
        private readonly HashSet<string> _currentVoters = new HashSet<string>();
        private bool _voteSessionActive;
        private int _bloodMoonDay;
        private DateTime _lastSkipTimeUtc = DateTime.MinValue;

        public BloodMoonVoteFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ISettingsRepository settingsRepo,
            LivePlayerManager playerManager)
            : base(eventBus, config)
        {
            _settingsRepo = settingsRepo;
            _playerManager = playerManager;
        }

        protected override void OnEnable()
        {
            LoadPersistedSettings();

            EventBus.Subscribe<SkyChangedEvent>(OnSkyChanged);
            EventBus.Subscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected);

            Log.Out($"[KitsuneCommand] Blood moon vote feature enabled. " +
                    $"Threshold={Settings.ThresholdType}:{Settings.ThresholdValue}, " +
                    $"VoteHoursBefore={Settings.AllowVoteHoursBefore}");
        }

        protected override void OnDisable()
        {
            EventBus.Unsubscribe<SkyChangedEvent>(OnSkyChanged);
            EventBus.Unsubscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected);
            ResetVoteSession();
        }

        // ─── Event Handlers ─────────────────────────────────────

        private void OnSkyChanged(SkyChangedEvent e)
        {
            if (!Settings.Enabled) return;

            var bmFrequency = GameStats.GetInt(GameIntegration.GameCompat.Stat(nameof(EnumGameStats.BloodMoonDay)));
            if (bmFrequency <= 0) return;

            // Calculate the next blood moon day
            var nextBmDay = e.Day <= 0 ? bmFrequency : (int)Math.Ceiling((double)e.Day / bmFrequency) * bmFrequency;
            var isBloodMoonDay = e.Day == nextBmDay;

            var shouldBeActive = false;

            if (isBloodMoonDay)
            {
                // Vote window: from (22 - AllowVoteHoursBefore) until end of blood moon night
                var voteStartHour = 22 - Settings.AllowVoteHoursBefore;
                if (voteStartHour < 0) voteStartHour = 0;

                if (e.Hour >= voteStartHour)
                    shouldBeActive = true;
            }

            // Also allow during active blood moon if configured
            if (e.IsBloodMoon && Settings.AllowVoteDuringBloodMoon)
                shouldBeActive = true;

            // Check cooldown
            if (shouldBeActive && CheckCooldown())
                shouldBeActive = false;

            // Session state transitions
            if (_voteSessionActive && !shouldBeActive)
            {
                // Vote window closed (blood moon ended or day changed)
                ResetVoteSession();
                BroadcastVoteState();
            }
            else if (shouldBeActive && !_voteSessionActive)
            {
                // Vote window opened
                _voteSessionActive = true;
                _bloodMoonDay = isBloodMoonDay ? e.Day : nextBmDay;
                BroadcastVoteState();
            }
        }

        private void OnPlayerDisconnected(PlayerDisconnectedEvent e)
        {
            if (!_voteSessionActive || string.IsNullOrEmpty(e.PlayerId)) return;

            // Keep votes from disconnected players — they already expressed intent.
            // The required vote count recalculates dynamically based on current online count,
            // which may cause the vote to pass if enough players leave.
            var required = GetRequiredVotes();
            if (_currentVoters.Count >= required && required > 0)
            {
                ExecuteSkip();
            }
            else
            {
                BroadcastVoteState();
            }
        }

        // ─── Public API ─────────────────────────────────────────

        /// <summary>
        /// Cast a vote to skip the blood moon. Called from ChatCommandService.
        /// </summary>
        public VoteResult CastVote(string playerId, string playerName, int entityId)
        {
            if (!Settings.Enabled)
                return VoteResult.Disabled;

            if (!_voteSessionActive)
                return VoteResult.NotActive;

            if (CheckCooldown())
                return VoteResult.OnCooldown;

            if (_currentVoters.Contains(playerId))
                return VoteResult.AlreadyVoted;

            _currentVoters.Add(playerId);

            var required = GetRequiredVotes();
            var current = _currentVoters.Count;

            BroadcastVoteState();

            if (current >= required)
            {
                ExecuteSkip();
                return VoteResult.Passed;
            }

            return VoteResult.Registered;
        }

        /// <summary>
        /// Returns the current vote status for the API.
        /// </summary>
        public BloodMoonVoteStatus GetVoteStatus()
        {
            return new BloodMoonVoteStatus
            {
                IsActive = _voteSessionActive,
                CurrentVotes = _currentVoters.Count,
                RequiredVotes = GetRequiredVotes(),
                TotalOnline = _playerManager.OnlineCount,
                Voters = _currentVoters.ToList(),
                BloodMoonDay = _bloodMoonDay,
                IsEnabled = Settings.Enabled
            };
        }

        /// <summary>
        /// Admin force-skip: immediately skips the blood moon.
        /// Called from the API controller (runs on OWIN thread).
        /// </summary>
        public void ForceSkip()
        {
            ExecuteSkip();
        }

        /// <summary>
        /// Updates settings in memory and persists to database.
        /// Called from the settings API controller.
        /// </summary>
        public void UpdateSettings(BloodMoonVoteSettings newSettings)
        {
            Settings = newSettings;
            try
            {
                var json = JsonConvert.SerializeObject(newSettings);
                _settingsRepo.Set(SettingsKey, json);
                Log.Out("[KitsuneCommand] Blood moon vote settings updated and saved.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to persist blood moon vote settings: {ex.Message}");
            }
        }

        // ─── Private Helpers ────────────────────────────────────

        private int GetRequiredVotes()
        {
            var online = _playerManager.OnlineCount;
            if (online <= 0) return 1;

            if (Settings.ThresholdType == "percentage")
                return Math.Max(1, (int)Math.Ceiling(online * Settings.ThresholdValue / 100.0));
            else // "count"
                return Math.Max(1, Math.Min(Settings.ThresholdValue, online));
        }

        private void ExecuteSkip()
        {
            var voteCount = _currentVoters.Count;

            // Advance game time to next day 6:00 AM via main thread
            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    // Get current world time to calculate next day
                    var world = GameManager.Instance?.World;
                    if (world == null)
                    {
                        Log.Warning("[KitsuneCommand] BloodMoonVote: Cannot skip — world is null.");
                        return;
                    }

                    var currentDay = GameUtils.WorldTimeToDays(world.worldTime);
                    var nextDay = currentDay + 1;

                    SdtdConsole.Instance.ExecuteSync($"st {nextDay} 6 0", null);

                    // Announce to all players
                    SdtdConsole.Instance.ExecuteSync(
                        $"say \"[FF6600]Blood moon skipped! ({voteCount} vote{(voteCount != 1 ? "s" : "")})\"", null);

                    Log.Out($"[KitsuneCommand] Blood moon skipped by vote. Votes: {voteCount}");
                }
                catch (Exception ex)
                {
                    Log.Error($"[KitsuneCommand] BloodMoonVote: Failed to execute skip: {ex.Message}");
                }
            }, null);

            _lastSkipTimeUtc = DateTime.UtcNow;
            ResetVoteSession();
            BroadcastVoteState();
        }

        private bool CheckCooldown()
        {
            if (Settings.CooldownMinutes <= 0) return false;
            return (DateTime.UtcNow - _lastSkipTimeUtc).TotalMinutes < Settings.CooldownMinutes;
        }

        private void ResetVoteSession()
        {
            _currentVoters.Clear();
            _voteSessionActive = false;
        }

        private void BroadcastVoteState()
        {
            EventBus.Publish(new BloodMoonVoteUpdateEvent
            {
                IsActive = _voteSessionActive,
                CurrentVotes = _currentVoters.Count,
                RequiredVotes = GetRequiredVotes(),
                TotalOnline = _playerManager.OnlineCount,
                BloodMoonDay = _bloodMoonDay
            });
        }

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<BloodMoonVoteSettings>(json);
                    if (loaded != null)
                    {
                        Settings = loaded;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load blood moon vote settings, using defaults: {ex.Message}");
            }

            // Settings remains as default (from FeatureBase.LoadSettings -> new TSettings())
        }
    }

    public enum VoteResult
    {
        Registered,
        AlreadyVoted,
        NotActive,
        Passed,
        Disabled,
        OnCooldown
    }

    public class BloodMoonVoteStatus
    {
        public bool IsActive { get; set; }
        public int CurrentVotes { get; set; }
        public int RequiredVotes { get; set; }
        public int TotalOnline { get; set; }
        public List<string> Voters { get; set; }
        public int BloodMoonDay { get; set; }
        public bool IsEnabled { get; set; }
    }
}
