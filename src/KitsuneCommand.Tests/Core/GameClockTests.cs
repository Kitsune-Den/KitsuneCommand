using NUnit.Framework;
using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Tests.Core
{
    /// <summary>
    /// GameClock copies the game's world-time formulas (GameUtils) so the
    /// Discord /time command can report the day, time and next blood moon.
    /// These pin it to what the game's HUD and blood moon director use.
    /// </summary>
    [TestFixture]
    public class GameClockTests
    {
        private const int DefaultDayLight = 18;

        /// <summary>World time for a day and time, rounded up to the tick that reads back as that minute.</summary>
        private static ulong At(int day, int hour, int minute) =>
            (ulong)((day - 1) * 24000 + hour * 1000) + (ulong)(minute * 1000 + 59) / 60;

        private static GameClock Clock(int day, int hour, int minute, int bloodMoonDay, int dayLight = DefaultDayLight) =>
            GameClock.FromWorldTime(At(day, hour, minute), bloodMoonDay, dayLight);

        [Test]
        public void TheWorldStartsOnDayOneAtMidnight()
        {
            var clock = GameClock.FromWorldTime(0, 7, DefaultDayLight);

            Assert.That(clock.Day, Is.EqualTo(1));
            Assert.That(clock.TimeText, Is.EqualTo("00:00"));
        }

        [Test]
        public void SplitsWorldTimeIntoDayHourAndMinute()
        {
            var clock = GameClock.FromWorldTime(13 * 24000 + 21 * 1000 + 750, 14, DefaultDayLight);

            Assert.That(clock.Day, Is.EqualTo(14));
            Assert.That(clock.Hour, Is.EqualTo(21));
            Assert.That(clock.Minute, Is.EqualTo(45));
            Assert.That(clock.TimeText, Is.EqualTo("21:45"));
        }

        [Test]
        public void MatchesTheGamesOwnFormula()
        {
            // GameUtils.WorldTimeToElements, as decompiled from the game.
            for (ulong t = 0; t < 3 * 24000; t += 7)
            {
                var clock = GameClock.FromWorldTime(t, 0, DefaultDayLight);
                Assert.That(clock.Day, Is.EqualTo((int)(t / 24000 + 1)), $"day at {t}");
                Assert.That(clock.Hour, Is.EqualTo((int)(t / 1000) % 24), $"hour at {t}");
                Assert.That(clock.Minute, Is.EqualTo((int)((double)t * 0.06) % 60), $"minute at {t}");
            }
        }

        [TestCase(0, 22, 4)]
        [TestCase(24, 22, 4)]
        [TestCase(18, 22, 4)]
        [TestCase(20, 22, 2)]
        [TestCase(12, 18, 6)]
        [TestCase(23, 23, 0)]
        public void DuskAndDawnFollowDayLightLength(int dayLight, int dusk, int dawn)
        {
            Assert.That(GameClock.DuskDawnHours(dayLight), Is.EqualTo((dusk, dawn)));
        }

        [TestCase(14, 21, 59, false)]
        [TestCase(14, 22, 0, true)]
        [TestCase(15, 3, 59, true)]
        [TestCase(15, 4, 0, false)]
        [TestCase(13, 23, 0, false)]
        public void BloodMoonRunsFromDuskToDawn(int day, int hour, int minute, bool expected)
        {
            Assert.That(Clock(day, hour, minute, bloodMoonDay: 14).IsBloodMoonNow, Is.EqualTo(expected));
        }

        [Test]
        public void BloodMoonFollowsAShorterDay()
        {
            // 12 hours of daylight: dusk at 18:00, dawn at 06:00.
            Assert.That(Clock(14, 18, 0, 14, dayLight: 12).IsBloodMoonNow, Is.True);
            Assert.That(Clock(15, 5, 59, 14, dayLight: 12).IsBloodMoonNow, Is.True);
            Assert.That(Clock(14, 10, 0, 14, dayLight: 12).DescribeBloodMoon(), Is.EqualTo("Tonight at 18:00"));
        }

        [Test]
        public void DescribesABloodMoonLaterToday()
        {
            Assert.That(Clock(14, 10, 0, 14).DescribeBloodMoon(), Is.EqualTo("Tonight at 22:00"));
        }

        [Test]
        public void DescribesABloodMoonTomorrow()
        {
            Assert.That(Clock(13, 10, 0, 14).DescribeBloodMoon(), Is.EqualTo("Tomorrow night (Day 14)"));
        }

        [Test]
        public void DescribesABloodMoonDaysAway()
        {
            Assert.That(Clock(10, 10, 0, 14).DescribeBloodMoon(), Is.EqualTo("Day 14, in 4 days"));
        }

        [Test]
        public void DescribesABloodMoonInProgress()
        {
            Assert.That(Clock(14, 23, 0, 14).DescribeBloodMoon(), Is.EqualTo("Happening now! It ends at 04:00."));
            Assert.That(Clock(15, 2, 0, 14).DescribeBloodMoon(), Is.EqualTo("Happening now! It ends at 04:00."));
        }

        [Test]
        public void SaysWhenBloodMoonsAreOff()
        {
            // BloodMoonFrequency 0 makes the game set the BloodMoonDay stat to 0.
            var clock = Clock(1, 1, 0, bloodMoonDay: 0);

            Assert.That(clock.IsBloodMoonNow, Is.False);
            Assert.That(clock.DescribeBloodMoon(), Is.EqualTo("Off on this server"));
        }

        [Test]
        public void HandlesAStaleBloodMoonDay()
        {
            Assert.That(Clock(16, 10, 0, 14).DescribeBloodMoon(), Is.EqualTo("Not scheduled yet"));
        }
    }
}
