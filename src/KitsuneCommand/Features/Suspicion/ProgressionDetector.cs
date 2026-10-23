using System;
using System.Collections.Generic;

namespace KitsuneCommand.Features.Suspicion
{
    /// <summary>
    /// Watches player level and unspent skill points over a sliding window.
    /// Flags levels gained too fast, and skill points that appear without the
    /// levels to explain them (spending points never counts against anyone).
    /// The server's copy of a player's progression arrives some time after
    /// they spawn, so each spawn starts a warm-up during which nothing is judged.
    /// </summary>
    public class ProgressionDetector
    {
        private struct Point
        {
            public DateTime At;
            public int Level;
            public int SkillPoints;
        }

        private class State
        {
            public readonly List<Point> Points = new List<Point>();
            public DateTime WarmupUntil;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<int, State> _states = new Dictionary<int, State>();

        /// <param name="skillPointsPerLevel">Skill points the game awards per level (vanilla 1).</param>
        public List<SuspicionFlag> Observe(PlayerSample sample, ProgressionDetectorSettings settings, int skillPointsPerLevel)
        {
            var flags = new List<SuspicionFlag>();
            if (sample?.Level == null || sample.SkillPoints == null) return flags;

            lock (_lock)
            {
                if (!_states.TryGetValue(sample.EntityId, out var st))
                    _states[sample.EntityId] = st = new State();
                if (sample.At < st.WarmupUntil) return flags;

                var cur = new Point { At = sample.At, Level = sample.Level.Value, SkillPoints = sample.SkillPoints.Value };
                var points = st.Points;

                // Values are step functions, so keep the newest point at or before the window
                // start: it's what the player had when the window opened.
                var cutoff = sample.At.AddMinutes(-Math.Max(1, settings.WindowMinutes));
                while (points.Count >= 2 && points[1].At <= cutoff) points.RemoveAt(0);

                if (points.Count == 0)
                {
                    points.Add(cur);
                    return flags;
                }

                var spl = Math.Max(0, skillPointsPerLevel);
                int levelGain = 0, excessPoints = 0;
                Point levelBase = points[0], pointsBase = points[0];
                foreach (var p in points)
                {
                    var gained = cur.Level - p.Level;
                    if (gained > levelGain) { levelGain = gained; levelBase = p; }

                    var excess = (cur.SkillPoints - p.SkillPoints) - Math.Max(0, gained) * spl;
                    if (excess > excessPoints) { excessPoints = excess; pointsBase = p; }
                }

                var flagged = false;
                if (settings.MaxLevelGain > 0 && levelGain > settings.MaxLevelGain)
                {
                    flagged = true;
                    var minutes = (cur.At - levelBase.At).TotalMinutes;
                    flags.Add(Flag("level_jump",
                        $"Gained {levelGain} levels ({levelBase.Level} to {cur.Level}) in {minutes:F0} min",
                        new Dictionary<string, object>
                        {
                            ["fromLevel"] = levelBase.Level,
                            ["toLevel"] = cur.Level,
                            ["minutes"] = Math.Round(minutes, 1),
                            ["limit"] = settings.MaxLevelGain,
                        }));
                }

                if (settings.MaxUnexplainedSkillPoints > 0 && excessPoints > settings.MaxUnexplainedSkillPoints)
                {
                    flagged = true;
                    var minutes = (cur.At - pointsBase.At).TotalMinutes;
                    flags.Add(Flag("skill_points",
                        $"{excessPoints} skill points appeared without the levels to earn them (in {minutes:F0} min)",
                        new Dictionary<string, object>
                        {
                            ["fromPoints"] = pointsBase.SkillPoints,
                            ["toPoints"] = cur.SkillPoints,
                            ["fromLevel"] = pointsBase.Level,
                            ["toLevel"] = cur.Level,
                            ["unexplained"] = excessPoints,
                            ["minutes"] = Math.Round(minutes, 1),
                            ["limit"] = settings.MaxUnexplainedSkillPoints,
                        }));
                }

                if (flagged)
                {
                    // Re-baseline so the same gain isn't reported again on every sample.
                    points.Clear();
                    points.Add(cur);
                }
                else
                {
                    var last = points[points.Count - 1];
                    if (last.Level != cur.Level || last.SkillPoints != cur.SkillPoints)
                        points.Add(cur);
                }
            }

            return flags;
        }

        /// <summary>The player (re)spawned: drop their history and wait <paramref name="warmup"/> before judging.</summary>
        public void Restart(int entityId, DateTime now, TimeSpan warmup)
        {
            lock (_lock)
            {
                if (!_states.TryGetValue(entityId, out var st))
                    _states[entityId] = st = new State();
                st.Points.Clear();
                st.WarmupUntil = now + warmup;
            }
        }

        public void Forget(int entityId)
        {
            lock (_lock) _states.Remove(entityId);
        }

        private static SuspicionFlag Flag(string rule, string summary, Dictionary<string, object> details) => new SuspicionFlag
        {
            Detector = SuspicionDetectorNames.Progression,
            Rule = rule,
            Severity = SuspicionSeverity.High,
            Summary = summary,
            Details = details,
        };
    }
}
