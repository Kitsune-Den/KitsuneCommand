using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Schedules graceful daily restarts with in-game warning broadcasts.
    /// At the configured wall-clock time (in the configured IANA timezone), the
    /// feature plays a warning ladder over the say channel and then issues
    /// `shutdown` so 7D2D exits cleanly with a save. The systemd unit's
    /// Restart=always brings the service back up automatically.
    ///
    /// Two trigger paths share the same countdown core:
    ///   • Daily timer — once per scheduled wall-clock minute
    ///   • Manual TriggerAsync(leadMinutes, actor) — used by the krestart console
    ///     command and the REST endpoint when an admin wants to restart now
    ///
    /// A re-entrancy guard prevents overlapping countdowns: if a manual restart
    /// fires while one's already in flight (or vice versa), the second caller
    /// is told "already pending" and bails. We never double-broadcast.
    /// </summary>
    public class GracefulRestartFeature : FeatureBase<GracefulRestartSettings>
    {
        private readonly ISettingsRepository _settingsRepo;
        private const string SettingsKey = "GracefulRestart";

        // Tick every 30 seconds so we never miss the scheduled minute even if
        // the host clock skews. Cheap.
        private Timer _tickTimer;

        // 0 = idle, 1 = countdown in progress. Set via Interlocked so the
        // schedule timer and a manual TriggerAsync can't race in.
        private int _countdownInFlight;

        // Tracks the last calendar date (YYYY-MM-DD in the schedule's timezone)
        // we kicked off a scheduled countdown for. Prevents the same daily
        // restart firing more than once if the tick happens to land twice
        // inside the same scheduled minute.
        private string _lastFiredDate = "";

        public GracefulRestartFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ISettingsRepository settingsRepo)
            : base(eventBus, config)
        {
            _settingsRepo = settingsRepo;
        }

        protected override void OnEnable()
        {
            LoadPersistedSettings();
            _tickTimer = new Timer(OnTick, null, 30_000, 30_000);

            var enabled = Settings.Enabled ? "ON" : "OFF";
            Log.Out($"[KitsuneCommand] GracefulRestart feature loaded. Schedule={enabled} {Settings.ScheduledTime} {Settings.ScheduledTimezone}, ladder steps={Settings.WarningLadder?.Count ?? 0}.");
        }

        protected override void OnDisable()
        {
            _tickTimer?.Dispose();
            _tickTimer = null;
        }

        /// <summary>
        /// Updates settings in memory, persists to database. The next tick will
        /// see the new schedule; no restart needed.
        /// </summary>
        public void UpdateSettings(GracefulRestartSettings newSettings)
        {
            Settings = newSettings ?? new GracefulRestartSettings();

            try
            {
                var json = JsonConvert.SerializeObject(Settings);
                _settingsRepo.Set(SettingsKey, json);

                var enabled = Settings.Enabled ? "ON" : "OFF";
                Log.Out($"[KitsuneCommand] GracefulRestart settings updated. Schedule={enabled} {Settings.ScheduledTime} {Settings.ScheduledTimezone}.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to persist graceful-restart settings: {ex.Message}");
            }
        }

        // ─── Manual trigger entry point ───────────────────────────────────

        public class TriggerResult
        {
            public bool Started { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        /// Kicks off a graceful-restart countdown using a SHORTER warning ladder
        /// truncated to the requested lead time. Lead time of 0 issues an
        /// immediate shutdown (still says "Restarting now..." once first).
        /// </summary>
        public TriggerResult TriggerNow(int leadMinutes, string actor = "admin")
        {
            if (Interlocked.CompareExchange(ref _countdownInFlight, 1, 0) != 0)
            {
                return new TriggerResult { Started = false, Message = "A graceful-restart countdown is already in progress." };
            }

            // Build a ladder pruned to the requested lead time.
            var fullLadder = (Settings.WarningLadder ?? new System.Collections.Generic.List<RestartWarning>())
                .Where(w => w != null)
                .OrderByDescending(w => w.MinutesBefore)
                .ToList();

            var pruned = fullLadder.Where(w => w.MinutesBefore <= leadMinutes).ToList();
            if (pruned.Count == 0 || pruned[0].MinutesBefore != leadMinutes)
            {
                // Inject a synthetic head step at the requested lead so the player
                // hears the right number even if it's not in the configured ladder.
                pruned.Insert(0, new RestartWarning
                {
                    MinutesBefore = Math.Max(0, leadMinutes),
                    Message = leadMinutes == 0
                        ? "Restarting now..."
                        : "Server restarting in {minutes} minute" + (leadMinutes == 1 ? "" : "s") + ". Please save and disconnect.",
                    ColorHex = leadMinutes <= 1 ? "FF0000" : "FFAA00",
                });
            }

            Log.Out($"[KitsuneCommand] GracefulRestart: manual trigger by {actor}, lead={leadMinutes}min, steps={pruned.Count}.");
            _ = Task.Run(() => RunCountdownAsync(pruned, source: $"manual:{actor}"));

            return new TriggerResult { Started = true, Message = $"Graceful restart starting. Lead time: {leadMinutes} minute(s)." };
        }

        // ─── Schedule tick ────────────────────────────────────────────────

        private void OnTick(object _)
        {
            if (!Settings.Enabled) return;
            if (_countdownInFlight != 0) return;

            DateTime nowInTz;
            TimeSpan scheduledTimeOfDay;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(Settings.ScheduledTimezone ?? "America/Los_Angeles");
                nowInTz = TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz);
                scheduledTimeOfDay = ParseScheduledTime(Settings.ScheduledTime ?? "04:00");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] GracefulRestart: bad schedule config ({Settings.ScheduledTimezone} / {Settings.ScheduledTime}): {ex.Message}");
                return;
            }

            // We need to start the countdown LongestLeadMinutes before the
            // scheduled wall-clock moment, so the final step lands at exactly
            // the configured time of day. e.g. schedule=04:00, ladder head=10,
            // → fire at 03:50.
            var ladder = (Settings.WarningLadder ?? new System.Collections.Generic.List<RestartWarning>())
                .Where(w => w != null)
                .OrderByDescending(w => w.MinutesBefore)
                .ToList();
            if (ladder.Count == 0) return;

            var headLead = TimeSpan.FromMinutes(ladder[0].MinutesBefore);
            var fireWindowStart = scheduledTimeOfDay - headLead;
            // 1-minute fire window so a 30s tick interval can't miss it.
            var fireWindowEnd = fireWindowStart + TimeSpan.FromMinutes(1);

            var todayKey = nowInTz.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (_lastFiredDate == todayKey) return;

            var nowTimeOfDay = nowInTz.TimeOfDay;
            if (nowTimeOfDay < fireWindowStart || nowTimeOfDay >= fireWindowEnd) return;

            // Lock + record.
            if (Interlocked.CompareExchange(ref _countdownInFlight, 1, 0) != 0) return;
            _lastFiredDate = todayKey;

            Log.Out($"[KitsuneCommand] GracefulRestart: scheduled trigger at {nowInTz:HH:mm:ss} {Settings.ScheduledTimezone} (target={Settings.ScheduledTime}).");
            _ = Task.Run(() => RunCountdownAsync(ladder, source: "scheduled"));
        }

        // ─── Countdown core ───────────────────────────────────────────────

        /// <summary>
        /// Plays through a sorted-descending ladder, sleeping between steps so
        /// each step lands the right number of minutes before the actual
        /// shutdown. Final step is always followed by ~10s of grace and then
        /// the `shutdown` console command.
        /// </summary>
        private async Task RunCountdownAsync(System.Collections.Generic.List<RestartWarning> ladder, string source)
        {
            try
            {
                ladder = ladder.OrderByDescending(w => w.MinutesBefore).ToList();

                for (int i = 0; i < ladder.Count; i++)
                {
                    var step = ladder[i];
                    Broadcast(step);

                    int sleepSec;
                    if (i + 1 < ladder.Count)
                    {
                        var thisLead = step.MinutesBefore;
                        var nextLead = ladder[i + 1].MinutesBefore;
                        sleepSec = Math.Max(0, (thisLead - nextLead) * 60);
                    }
                    else
                    {
                        // Final step → wait the remainder of MinutesBefore, then
                        // an extra ~10s for players to see the message before
                        // the world unloads.
                        sleepSec = step.MinutesBefore * 60 + 10;
                    }

                    if (sleepSec > 0)
                        await Task.Delay(TimeSpan.FromSeconds(sleepSec)).ConfigureAwait(false);
                }

                Log.Out($"[KitsuneCommand] GracefulRestart: issuing shutdown ({source}).");
                ExecOnGameThread("shutdown");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] GracefulRestart: countdown error ({source}): {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _countdownInFlight, 0);
            }
        }

        private static void Broadcast(RestartWarning step)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.Message)) return;

            var minutesText = Math.Max(0, step.MinutesBefore).ToString(CultureInfo.InvariantCulture);
            var rendered = step.Message.Replace("{minutes}", minutesText);

            // Wrap the rendered text in a color tag if one is configured.
            var color = NormalizeColorHex(step.ColorHex);
            if (!string.IsNullOrEmpty(color))
            {
                rendered = $"[{color}]{rendered}[-]";
            }

            // 7D2D's say command needs the message double-quoted; escape any
            // embedded double quotes so an admin can't break the command with
            // a literal " in their template.
            var safe = rendered.Replace("\"", "'");
            ExecOnGameThread($"say \"{safe}\"");
        }

        /// <summary>
        /// Strips a leading '#' if present and returns the hex string in upper
        /// case if it's the right length, otherwise empty (= no color wrap).
        /// </summary>
        private static string NormalizeColorHex(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var trimmed = raw.Trim().TrimStart('#');
            if (trimmed.Length != 6) return "";
            foreach (var c in trimmed)
            {
                if (!Uri.IsHexDigit(c)) return "";
            }
            return trimmed.ToUpperInvariant();
        }

        /// <summary>
        /// Marshal a single console command to the Unity main thread. We never
        /// block the caller — fire-and-forget is fine for say + shutdown.
        /// </summary>
        private static void ExecOnGameThread(string command)
        {
            try
            {
                ModEntry.MainThreadContext.Post(_ =>
                {
                    try { SdtdConsole.Instance.ExecuteSync(command, null); }
                    catch (Exception ex) { Log.Warning($"[KitsuneCommand] GracefulRestart: exec '{command}' failed: {ex.Message}"); }
                }, null);
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] GracefulRestart: post '{command}' failed: {ex.Message}");
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────

        private static TimeSpan ParseScheduledTime(string hhmm)
        {
            // Accept "HH:mm" or "H:mm". Anything else throws (caught by caller).
            var parts = (hhmm ?? "").Split(':');
            if (parts.Length != 2) throw new FormatException($"Expected HH:mm, got '{hhmm}'.");
            var hour = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var minute = int.Parse(parts[1], CultureInfo.InvariantCulture);
            if (hour < 0 || hour > 23) throw new FormatException("Hour must be 0–23.");
            if (minute < 0 || minute > 59) throw new FormatException("Minute must be 0–59.");
            return new TimeSpan(hour, minute, 0);
        }

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<GracefulRestartSettings>(json);
                    if (loaded != null)
                    {
                        Settings = loaded;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load graceful-restart settings, using defaults: {ex.Message}");
            }
        }
    }
}
