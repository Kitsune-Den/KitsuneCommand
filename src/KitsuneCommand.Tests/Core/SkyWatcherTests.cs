using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Core;
using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Tests.Core
{
    /// <summary>
    /// SkyWatcher turns the game clock into SkyChangedEvent, which the blood
    /// moon vote, the Discord blood moon alert and the panel's live clock all
    /// listen for. Nothing published it before, so all three were dead.
    /// </summary>
    [TestFixture]
    public class SkyWatcherTests
    {
        private const int DefaultDayLight = 18; // dusk 22:00, dawn 04:00

        private List<SkyChangedEvent> _published;
        private SkyWatcher _watcher;

        [SetUp]
        public void SetUp()
        {
            var bus = new ModEventBus();
            _published = new List<SkyChangedEvent>();
            bus.Subscribe<SkyChangedEvent>(_published.Add);
            _watcher = new SkyWatcher(bus);
        }

        /// <summary>World time for a day and time, rounded up to the tick that reads back as that minute.</summary>
        private static ulong At(int day, int hour, int minute) =>
            (ulong)((day - 1) * 24000 + hour * 1000) + (ulong)(minute * 1000 + 59) / 60;

        private static GameClock Clock(ulong worldTime, int bloodMoonDay, int dayLight = DefaultDayLight) =>
            GameClock.FromWorldTime(worldTime, bloodMoonDay, dayLight);

        private static (int Day, int Hour, int Minute, bool IsBloodMoon) Reading(SkyChangedEvent e) =>
            (e.Day, e.Hour, e.Minute, e.IsBloodMoon);

        [Test]
        public void PublishesTheFirstReading()
        {
            Assert.That(_watcher.Observe(Clock(At(3, 12, 30), 7)), Is.True);

            Assert.That(_published, Has.Count.EqualTo(1));
            Assert.That(Reading(_published[0]), Is.EqualTo((3, 12, 30, false)));
        }

        [Test]
        public void PublishesOncePerInGameMinute()
        {
            // An hour is 1000 ticks, so a minute is 16 or 17: these first three all read 12:00.
            _watcher.Observe(Clock(At(3, 12, 0), 7));
            _watcher.Observe(Clock(At(3, 12, 0) + 5, 7));
            _watcher.Observe(Clock(At(3, 12, 0) + 10, 7));
            _watcher.Observe(Clock(At(3, 12, 1), 7));
            _watcher.Observe(Clock(At(3, 12, 1) + 3, 7));

            Assert.That(_published.Select(e => e.Minute), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void ADayOfTicksPublishesOncePerMinute()
        {
            for (var t = At(2, 0, 0); t < At(3, 0, 0); t++)
                _watcher.Observe(Clock(t, 7));

            Assert.That(_published, Has.Count.EqualTo(24 * 60));
        }

        [Test]
        public void PublishesWhenTheGamePicksANewBloodMoonDayWithinAMinute()
        {
            _watcher.Observe(Clock(At(15, 4, 0), 14));
            _watcher.Observe(Clock(At(15, 4, 0) + 1, 21));

            Assert.That(_published.Select(e => e.BloodMoonDay), Is.EqualTo(new[] { 14, 21 }));
        }

        [Test]
        public void PublishesWhenAnAdminTurnsTheClockBack()
        {
            _watcher.Observe(Clock(At(5, 9, 0), 7));
            _watcher.Observe(Clock(At(2, 9, 0), 7));

            Assert.That(_published.Select(e => e.Day), Is.EqualTo(new[] { 5, 2 }));
        }

        [Test]
        public void CarriesTheBloodMoonDayAndHours()
        {
            // 12 hours of daylight: dusk at 18:00, dawn at 06:00.
            _watcher.Observe(Clock(At(3, 12, 0), 9, dayLight: 12));

            var e = _published.Single();
            Assert.That(e.BloodMoonDay, Is.EqualTo(9));
            Assert.That(e.DuskHour, Is.EqualTo(18));
            Assert.That(e.DawnHour, Is.EqualTo(6));
        }

        [Test]
        public void BloodMoonStartsAtDuskAndEndsAtDawn()
        {
            // Tick through day 14 into day 15 with the stat behaving like the game's:
            // it stays on the blood moon until that ends at dawn, then moves to the next.
            var dawn = At(15, 4, 0);
            for (var t = At(14, 6, 0); t <= At(15, 10, 0); t++)
                _watcher.Observe(Clock(t, t < dawn ? 14 : 21));

            Assert.That(Flips(), Is.EqualTo(new[]
            {
                (14, 6, 0, false),
                (14, 22, 0, true),
                (15, 4, 0, false),
            }));
            Assert.That(_published.Last().BloodMoonDay, Is.EqualTo(21));
        }

        [Test]
        public void BloodMoonFollowsAShorterDay()
        {
            // 12 hours of daylight: dusk at 18:00, dawn at 06:00.
            var dawn = At(8, 6, 0);
            for (var t = At(7, 12, 0); t <= At(8, 12, 0); t++)
                _watcher.Observe(Clock(t, t < dawn ? 7 : 14, dayLight: 12));

            Assert.That(Flips(), Is.EqualTo(new[]
            {
                (7, 12, 0, false),
                (7, 18, 0, true),
                (8, 6, 0, false),
            }));
        }

        [Test]
        public void NoBloodMoonWhenTheyAreOff()
        {
            for (var t = At(7, 0, 0); t < At(9, 0, 0); t += 100)
                _watcher.Observe(Clock(t, 0));

            Assert.That(_published, Is.Not.Empty);
            Assert.That(_published.Any(e => e.IsBloodMoon), Is.False);
        }

        /// <summary>Each published reading where IsBloodMoon changed, starting with the first.</summary>
        private List<(int, int, int, bool)> Flips()
        {
            var flips = new List<(int, int, int, bool)>();
            bool? was = null;
            foreach (var e in _published)
            {
                if (was == e.IsBloodMoon) continue;
                flips.Add(Reading(e));
                was = e.IsBloodMoon;
            }
            return flips;
        }
    }
}
