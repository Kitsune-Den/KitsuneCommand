using System;
using System.Collections.Generic;
using System.Linq;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using Newtonsoft.Json;

namespace KitsuneCommand.Features.Suspicion
{
    /// <summary>
    /// Turns detector flags into suspicion-log rows. Skips exempt players,
    /// suppresses the same player tripping the same check again inside the
    /// repeat cooldown, and forwards to Discord at most once per player per
    /// detector per Discord cooldown. Never takes action against the player.
    /// </summary>
    public class SuspicionRecorder
    {
        private readonly ISuspicionEventRepository _repo;
        private readonly Action<string, SuspicionEvent> _sendDiscord;
        private readonly Func<DateTime> _clock;
        private readonly object _lock = new object();
        private readonly Dictionary<string, DateTime> _lastRecorded = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, DateTime> _lastAlerted = new Dictionary<string, DateTime>();

        public SuspicionRecorder(ISuspicionEventRepository repo, Action<string, SuspicionEvent> sendDiscord, Func<DateTime> clock = null)
        {
            _repo = repo;
            _sendDiscord = sendDiscord;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public static bool IsExempt(SuspectPlayer player, SuspicionSettings settings)
        {
            if (player == null) return true;
            if (settings.IgnoreAdmins && player.IsAdmin) return true;
            var exempt = settings.ExemptPlayerIds;
            if (exempt == null || exempt.Count == 0) return false;
            return exempt.Any(id => !string.IsNullOrWhiteSpace(id)
                && (string.Equals(id.Trim(), player.PlayerId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id.Trim(), player.PlatformId, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Records the flag. Returns the stored row, or null if it was exempt or a repeat.</summary>
        public SuspicionEvent Record(SuspectPlayer player, SuspicionFlag flag, SuspicionSettings settings)
        {
            if (flag == null || IsExempt(player, settings)) return null;

            var now = _clock();
            var who = !string.IsNullOrEmpty(player.PlayerId) ? player.PlayerId : "entity:" + player.EntityId;
            var recordKey = $"{who}|{flag.Detector}|{flag.DedupKey ?? flag.Rule}";
            var alertKey = $"{who}|{flag.Detector}";
            bool alert;

            lock (_lock)
            {
                var cooldown = TimeSpan.FromMinutes(Math.Max(0, settings.RepeatCooldownMinutes));
                if (_lastRecorded.TryGetValue(recordKey, out var last) && now - last < cooldown)
                    return null;
                _lastRecorded[recordKey] = now;

                alert = !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl)
                    && SuspicionSeverity.Rank(flag.Severity) >= SuspicionSeverity.Rank(settings.DiscordMinSeverity);
                if (alert)
                {
                    var discordCooldown = TimeSpan.FromMinutes(Math.Max(0, settings.DiscordCooldownMinutes));
                    if (_lastAlerted.TryGetValue(alertKey, out var lastAlert) && now - lastAlert < discordCooldown)
                        alert = false;
                    else
                        _lastAlerted[alertKey] = now;
                }
            }

            var evt = new SuspicionEvent
            {
                PlayerId = player.PlayerId,
                PlayerName = player.PlayerName,
                EntityId = player.EntityId,
                Detector = flag.Detector,
                Rule = flag.Rule,
                Severity = flag.Severity,
                Summary = flag.Summary,
                Details = flag.Details != null && flag.Details.Count > 0 ? JsonConvert.SerializeObject(flag.Details) : null,
                PosX = flag.X,
                PosY = flag.Y,
                PosZ = flag.Z,
            };
            evt.Id = _repo.Insert(evt);
            evt.CreatedAt = now.ToString("yyyy-MM-dd HH:mm:ss");

            if (alert) _sendDiscord?.Invoke(settings.DiscordWebhookUrl, evt);
            return evt;
        }
    }
}
