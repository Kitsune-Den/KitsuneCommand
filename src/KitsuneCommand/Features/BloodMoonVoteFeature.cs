using System.Collections.Generic;
using System.Linq;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.GameIntegration;
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

            var shouldBeActive = IsVoteWindowOpen(e, Settings) && !CheckCooldown();

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
                _bloodMoonDay = e.BloodMoonDay;
                BroadcastVoteState();
            }
        }

        /// <summary>
        /// Whether players can vote right now: from AllowVoteHoursBefore hours
        /// before dusk on the blood moon day, and while the blood moon runs if
        /// AllowVoteDuringBloodMoon is on.
        ///
        /// The blood moon day is the game's own BloodMoonDay stat, not worked
        /// out from BloodMoonFrequency, so a BloodMoonRange that adds random
        /// days moves the vote with it.
        /// </summary>
        public static bool IsVoteWindowOpen(SkyChangedEvent sky, BloodMoonVoteSettings settings)
        {
            if (sky.IsBloodMoon) return settings.AllowVoteDuringBloodMoon;
            if (sky.BloodMoonDay <= 0 || sky.Day != sky.BloodMoonDay) return false;

            // Before dusk on the day itself (from dusk on, IsBloodMoon is true).
            return sky.Hour >= Math.Max(0, sky.DuskHour - settings.AllowVoteHoursBefore);
        }

        /// <summary>
        /// Where a skip moves the clock: the morning after the blood moon, at
        /// 06:00 or at dawn if that's later. After midnight the blood moon is
        /// already on its second day, so that morning is the current day.
        /// </summary>
        public static (int Day, int Hour) SkipTarget(GameClock clock)
        {
            var hour = Math.Max(6, clock.DawnHour);
            var afterMidnight = clock.IsBloodMoonNow && clock.Day == clock.BloodMoonDay + 1;
            return (afterMidnight ? clock.Day : clock.Day + 1, hour);
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
        /// Admin force-skip: skips the blood moon now, whether or not a vote is open.
        /// The web API calls this from its own thread, so the skip runs on the main
        /// thread, where chat votes and disconnects already change the vote state.
        /// Returns false when the main thread didn't get to it in time; the skip
        /// then never happens.
        /// </summary>
        public bool ForceSkip()
        {
            return RunOnMainThread(() =>
            {
                ExecuteSkip();
                return true;
            });
        }

        /// <summary>
        /// Runs <paramref name="work"/> on the main thread and returns its answer, or false
        /// if the main thread didn't get to it in time (it then never runs). Virtual so
        /// tests can supply a main thread: the real one lives in the game.
        /// </summary>
        protected virtual bool RunOnMainThread(Func<bool> work)
        {
            return GameThread.Run("Blood moon force-skip", work, false);
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

        /// <summary>
        /// Ends the vote and moves the clock past the blood moon. Runs on the main
        /// thread: chat votes and disconnects arrive there, and ForceSkip goes
        /// there first, so the vote state only ever changes on one thread.
        /// </summary>
        private void ExecuteSkip()
        {
            SkipToMorning(_currentVoters.Count);

            _lastSkipTimeUtc = DateTime.UtcNow;
            ResetVoteSession();
            BroadcastVoteState();
        }

        /// <summary>
        /// Moves the game clock to the morning after and announces the skip, on the
        /// next frame rather than inside the chat or disconnect handler that passed
        /// the vote. Virtual so tests can stand in for the game.
        /// </summary>
        protected virtual void SkipToMorning(int voteCount)
        {
            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    var clock = GameClock.ReadLive();
                    if (clock == null)
                    {
                        Log.Warning("[KitsuneCommand] BloodMoonVote: Cannot skip — world is null.");
                        return;
                    }

                    var (day, hour) = SkipTarget(clock.Value);
                    SdtdConsole.Instance.ExecuteSync($"st {day} {hour} 0", null);

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
