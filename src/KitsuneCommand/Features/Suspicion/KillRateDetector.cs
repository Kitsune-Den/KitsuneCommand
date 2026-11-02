using System;
using System.Collections.Generic;

namespace KitsuneCommand.Features.Suspicion
{
    /// <summary>
    /// Counts each player's zombie kills over the last minute. Going over the
    /// ceiling is flagged once (medium); reaching twice the ceiling in the same
    /// burst is flagged once more (high). The burst ends when the rate drops
    /// back under the ceiling.
    /// </summary>
    public class KillRateDetector
    {
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

        private class State
        {
            public readonly Queue<DateTime> Kills = new Queue<DateTime>();
            public int Reported; // 0 nothing yet, 1 medium, 2 high
        }

        private readonly object _lock = new object();
        private readonly Dictionary<int, State> _states = new Dictionary<int, State>();

        public SuspicionFlag Observe(int killerEntityId, DateTime at, KillRateDetectorSettings settings)
        {
            var limit = settings.MaxZombieKillsPerMinute;
            if (limit <= 0) return null;

            lock (_lock)
            {
                if (!_states.TryGetValue(killerEntityId, out var st))
                    _states[killerEntityId] = st = new State();

                st.Kills.Enqueue(at);
                while (st.Kills.Count > 0 && st.Kills.Peek() <= at - Window) st.Kills.Dequeue();

                var count = st.Kills.Count;
                if (count <= limit)
                {
                    st.Reported = 0;
                    return null;
                }

                var level = count >= limit * 2 ? 2 : 1;
                if (level <= st.Reported) return null;
                st.Reported = level;

                return new SuspicionFlag
                {
                    Detector = SuspicionDetectorNames.KillRate,
                    Rule = "kill_rate",
                    Severity = level == 2 ? SuspicionSeverity.High : SuspicionSeverity.Medium,
                    // Distinct keys so the escalation isn't swallowed as a repeat of the first flag.
                    DedupKey = level == 2 ? "kill_rate:high" : "kill_rate",
                    Summary = $"{count} zombie kills in one minute (ceiling {limit})",
                    Details = new Dictionary<string, object>
                    {
                        ["kills"] = count,
                        ["limit"] = limit,
                    },
                };
            }
        }

        public void Forget(int entityId)
        {
            lock (_lock) _states.Remove(entityId);
        }
    }
}
