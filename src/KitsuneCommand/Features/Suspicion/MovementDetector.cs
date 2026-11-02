using System;
using System.Collections.Generic;

namespace KitsuneCommand.Features.Suspicion
{
    /// <summary>
    /// Judges consecutive position samples: on-foot speed, single-sample jumps
    /// (teleports), and hanging in the air well above the highest block. Riding
    /// anything clears the slate. Spawns, respawns and teleports KC knows about
    /// open a grace window and drop the previous sample, so the move they cause
    /// is never measured.
    /// </summary>
    public class MovementDetector
    {
        /// <summary>Samples further apart than this (a main-thread stall) aren't trusted for jumps.</summary>
        public const double MaxJumpSampleGapSeconds = 10;

        /// <summary>Dropping at least this fast (m/s) is falling, not flying.</summary>
        public const double FallingSpeed = 2;

        private class State
        {
            public PlayerSample Last;
            public DateTime GraceUntil;
            public int OverSpeedSamples;
            public int AirborneSamples;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<int, State> _states = new Dictionary<int, State>();

        public List<SuspicionFlag> Observe(PlayerSample sample, MovementDetectorSettings settings)
        {
            var flags = new List<SuspicionFlag>();
            if (sample == null) return flags;

            lock (_lock)
            {
                if (!_states.TryGetValue(sample.EntityId, out var st))
                    _states[sample.EntityId] = st = new State();

                var prev = st.Last;
                st.Last = sample;

                if (sample.At < st.GraceUntil || prev == null)
                {
                    // Inside a grace window, or the first sample we've seen: this is the new baseline.
                    st.OverSpeedSamples = 0;
                    st.AirborneSamples = 0;
                    return flags;
                }

                var dt = (sample.At - prev.At).TotalSeconds;
                if (dt <= 0.5) return flags;

                if (sample.Attached || prev.Attached)
                {
                    st.OverSpeedSamples = 0;
                    st.AirborneSamples = 0;
                    return flags;
                }

                var dx = sample.X - prev.X;
                var dz = sample.Z - prev.Z;
                var horizontal = Math.Sqrt(dx * dx + dz * dz);

                if (settings.JumpDistance > 0 && horizontal >= settings.JumpDistance && dt <= MaxJumpSampleGapSeconds)
                {
                    st.OverSpeedSamples = 0;
                    st.AirborneSamples = 0;
                    flags.Add(Flag("teleport_jump", SuspicionSeverity.Medium, sample,
                        $"Moved {horizontal:F0} m in {dt:F1} s with no known teleport",
                        new Dictionary<string, object>
                        {
                            ["distance"] = Math.Round(horizontal, 1),
                            ["seconds"] = Math.Round(dt, 1),
                            ["fromX"] = Math.Round(prev.X, 1),
                            ["fromY"] = Math.Round(prev.Y, 1),
                            ["fromZ"] = Math.Round(prev.Z, 1),
                        }));
                    return flags;
                }

                var speed = horizontal / dt;
                if (settings.MaxHorizontalSpeed > 0 && speed > settings.MaxHorizontalSpeed)
                {
                    st.OverSpeedSamples++;
                    // Flag once per streak, when it first becomes sustained.
                    if (st.OverSpeedSamples == Math.Max(1, settings.SpeedSamplesRequired))
                    {
                        flags.Add(Flag("speed", SuspicionSeverity.Medium, sample,
                            $"On foot at {speed:F1} m/s for {st.OverSpeedSamples} samples (limit {settings.MaxHorizontalSpeed:F0})",
                            new Dictionary<string, object>
                            {
                                ["speed"] = Math.Round(speed, 1),
                                ["limit"] = settings.MaxHorizontalSpeed,
                                ["samples"] = st.OverSpeedSamples,
                            }));
                    }
                }
                else
                {
                    st.OverSpeedSamples = 0;
                }

                if (settings.FlagFlying && sample.GroundHeight.HasValue && settings.FlyHeight > 0)
                {
                    // You stand on top of the highest block, one above its Y.
                    var height = sample.Y - (sample.GroundHeight.Value + 1);
                    var falling = (prev.Y - sample.Y) / dt >= FallingSpeed;
                    if (height > settings.FlyHeight && !falling)
                    {
                        st.AirborneSamples++;
                        if (st.AirborneSamples == Math.Max(1, settings.FlySamplesRequired))
                        {
                            flags.Add(Flag("flying", SuspicionSeverity.Medium, sample,
                                $"{height:F0} m above the highest block for {st.AirborneSamples} samples without falling",
                                new Dictionary<string, object>
                                {
                                    ["height"] = Math.Round(height, 1),
                                    ["limit"] = settings.FlyHeight,
                                    ["samples"] = st.AirborneSamples,
                                }));
                        }
                    }
                    else
                    {
                        st.AirborneSamples = 0;
                    }
                }
            }

            return flags;
        }

        /// <summary>
        /// The player is about to move for a legitimate reason (spawn, respawn,
        /// teleport). Forget where they were and ignore them until <paramref name="until"/>.
        /// </summary>
        public void Grace(int entityId, DateTime until)
        {
            lock (_lock)
            {
                if (!_states.TryGetValue(entityId, out var st))
                    _states[entityId] = st = new State();
                st.Last = null;
                st.OverSpeedSamples = 0;
                st.AirborneSamples = 0;
                if (until > st.GraceUntil) st.GraceUntil = until;
            }
        }

        public void Forget(int entityId)
        {
            lock (_lock) _states.Remove(entityId);
        }

        private static SuspicionFlag Flag(string rule, string severity, PlayerSample s, string summary,
            Dictionary<string, object> details) => new SuspicionFlag
        {
            Detector = SuspicionDetectorNames.Movement,
            Rule = rule,
            Severity = severity,
            Summary = summary,
            Details = details,
            X = s.X,
            Y = s.Y,
            Z = s.Z,
        };
    }
}
