using System;
using System.Collections.Generic;

namespace KitsuneCommand.Diagnostics
{
    /// <summary>
    /// In-memory ring buffer for the most recent
    /// <see cref="JoinAttemptEvent"/>s captured by the
    /// AuthWrapperServerDiagnostics Harmony patches.
    ///
    /// Why a ring buffer and not the SQLite DB:
    /// <list type="bullet">
    ///   <item>Events fire at LiteNetLib speeds — a single failed-join burst
    ///   can produce 30+ events in 5 seconds. Writing each one to SQLite from
    ///   inside a Harmony Postfix on the network thread would push contention
    ///   onto a path that's hot during the exact moments operators care
    ///   about.</item>
    ///   <item>The use case is diagnostic, not audit. Operators want "what's
    ///   happening RIGHT NOW" not "what happened three months ago." Memory
    ///   is the right tier; restart-on-failure is acceptable.</item>
    ///   <item>A bounded buffer also caps the memory footprint regardless of
    ///   how aggressively a bad actor or broken router hammers the
    ///   server.</item>
    /// </list>
    ///
    /// Capacity defaults to 500 events. At ~250 bytes per event that's ~125 KB
    /// — trivial. A typical bad-join burst is 10-15 events, so 500 holds the
    /// last ~30 distinct join attempts.
    ///
    /// Thread safety: <see cref="Record"/> is called from the LiteNetLib
    /// network thread (where Harmony Postfixes execute); <see cref="Snapshot"/>
    /// is called from the OWIN HTTP thread. A single lock protects both —
    /// contention is minimal because both paths are short, and the snapshot
    /// copies the data out of the buffer before returning so the lock window
    /// is just the copy, not the network IO.
    /// </summary>
    public static class JoinAttemptRing
    {
        public const int Capacity = 500;

        private static readonly object _lock = new object();
        private static readonly JoinAttemptEvent[] _buffer = new JoinAttemptEvent[Capacity];

        /// <summary>Next slot to write. Wraps modulo Capacity.</summary>
        private static int _next = 0;

        /// <summary>Total events ever recorded since process start. Exposed for stats / debug.</summary>
        private static long _totalRecorded = 0;

        /// <summary>Total events captured since process start (monotonically increasing).</summary>
        public static long TotalRecorded
        {
            get { lock (_lock) { return _totalRecorded; } }
        }

        /// <summary>
        /// Record an event. Cheap, non-allocating beyond the event object the
        /// caller already constructed. Silently no-ops on null to keep the
        /// patches forgiving (a malformed event from some edge case won't
        /// crash the auth wrapper).
        /// </summary>
        public static void Record(JoinAttemptEvent ev)
        {
            if (ev == null) return;
            lock (_lock)
            {
                _buffer[_next] = ev;
                _next = (_next + 1) % Capacity;
                _totalRecorded++;
            }
        }

        /// <summary>
        /// Get up to <paramref name="limit"/> most-recent events, optionally
        /// filtered to events at or after <paramref name="sinceUtc"/>. Returns
        /// newest-first order — the same order operators want in a "live
        /// activity" panel.
        /// </summary>
        public static List<JoinAttemptEvent> Snapshot(int limit = 100, DateTime? sinceUtc = null)
        {
            if (limit <= 0) return new List<JoinAttemptEvent>();
            if (limit > Capacity) limit = Capacity;

            var result = new List<JoinAttemptEvent>(limit);
            lock (_lock)
            {
                // Walk backward from _next (one past most recent) up to Capacity slots.
                for (int i = 0; i < Capacity && result.Count < limit; i++)
                {
                    int idx = ((_next - 1 - i) + Capacity) % Capacity;
                    var ev = _buffer[idx];
                    if (ev == null) continue;
                    if (sinceUtc.HasValue && ev.Timestamp < sinceUtc.Value) break;
                    result.Add(ev);
                }
            }
            return result;
        }

        /// <summary>
        /// Drop everything. Operator-triggered reset useful for "start fresh
        /// before reproducing the bug" debugging flows.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _next = 0;
                // _totalRecorded intentionally NOT reset — it's a monotonic
                // counter representing process lifetime activity, useful even
                // after a clear.
            }
        }
    }
}
