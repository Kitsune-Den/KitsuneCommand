using KitsuneCommand.Core;

namespace KitsuneCommand.GameIntegration
{
    /// <summary>
    /// Publishes <see cref="SkyChangedEvent"/>, which the blood moon vote, the
    /// Discord blood moon alert and the panel's live clock all listen for.
    ///
    /// The game has no mod event for its clock, so a timer looks at it every
    /// couple of seconds, on the main thread like the safe zone sweep, and
    /// publishes only when the in-game minute or the blood moon day has moved.
    /// At the default 60-minute day that's one event every 2.5 seconds; on a
    /// short day minutes are skipped rather than sent faster. Never per frame.
    ///
    /// Publishing from the main thread keeps the blood moon vote's state on the
    /// same thread as the chat commands and disconnects that change it.
    /// </summary>
    public class SkyWatcher
    {
        /// <summary>How often the clock is looked at, in real milliseconds.</summary>
        public const int CheckIntervalMs = 2000;

        private readonly ModEventBus _eventBus;
        private Timer _timer;
        private int _checkQueued;
        private bool _warned;
        private GameClock? _lastPublished;

        public SkyWatcher(ModEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Initialize()
        {
            _timer?.Dispose();
            _timer = new Timer(_ => QueueCheck(), null, CheckIntervalMs, CheckIntervalMs);
        }

        public void Shutdown()
        {
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>
        /// Publishes the clock if it reads differently from the last one
        /// published: a new minute, or the game picked a new blood moon day.
        /// Main thread only. Returns true if it published.
        /// </summary>
        public bool Observe(GameClock clock)
        {
            if (_lastPublished is GameClock last && SameReading(last, clock)) return false;

            _lastPublished = clock;
            _eventBus.Publish(ToEvent(clock));
            return true;
        }

        public static SkyChangedEvent ToEvent(GameClock clock) => new SkyChangedEvent
        {
            Day = clock.Day,
            Hour = clock.Hour,
            Minute = clock.Minute,
            IsBloodMoon = clock.IsBloodMoonNow,
            BloodMoonDay = clock.BloodMoonDay,
            DuskHour = clock.DuskHour,
            DawnHour = clock.DawnHour
        };

        // IsBloodMoonNow follows from these, so it can't change on its own.
        private static bool SameReading(GameClock a, GameClock b) =>
            a.Day == b.Day && a.Hour == b.Hour && a.Minute == b.Minute &&
            a.BloodMoonDay == b.BloodMoonDay && a.DuskHour == b.DuskHour && a.DawnHour == b.DawnHour;

        private void QueueCheck()
        {
            var main = ModEntry.MainThreadContext;
            if (main == null) return;
            // One check at a time: if the main thread is busy, ticks wait rather than pile up.
            if (Interlocked.Exchange(ref _checkQueued, 1) == 1) return;

            main.Post(_ =>
            {
                try
                {
                    var clock = GameClock.ReadLive();
                    if (clock != null) Observe(clock.Value);
                }
                catch (Exception ex)
                {
                    // Once: this runs every two seconds and the cause won't go away by itself.
                    if (!_warned)
                    {
                        _warned = true;
                        Log.Warning($"[KitsuneCommand] Couldn't read the game clock; the blood moon alert, " +
                                    $"blood moon vote and the panel's live clock won't update: {ex.Message}");
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _checkQueued, 0);
                }
            }, null);
        }
    }
}
