using Moq;
using NUnit.Framework;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Tests.Features
{
    /// <summary>
    /// The blood moon vote opens from the game's own BloodMoonDay stat. It used
    /// to read that stat as the blood moon frequency and round the current day
    /// up to a multiple of it, which only matched by luck on day 7 of a
    /// 7-day frequency and never with BloodMoonRange.
    /// </summary>
    [TestFixture]
    public class BloodMoonVoteFeatureTests
    {
        private ModEventBus _bus;
        private BloodMoonVoteFeature _feature;

        [SetUp]
        public void SetUp()
        {
            _bus = new ModEventBus();
            _feature = new BloodMoonVoteFeature(
                _bus, new ConfigManager(new AppSettings()), new Mock<ISettingsRepository>().Object, new LivePlayerManager(_bus));
            _feature.LoadSettings();
            _feature.Start();
        }

        [TearDown]
        public void TearDown() => _feature.Stop();

        private static SkyChangedEvent Sky(int day, int hour, int bloodMoonDay, bool isBloodMoon = false, int dusk = 22, int dawn = 4) =>
            new SkyChangedEvent
            {
                Day = day,
                Hour = hour,
                BloodMoonDay = bloodMoonDay,
                IsBloodMoon = isBloodMoon,
                DuskHour = dusk,
                DawnHour = dawn
            };

        private static GameClock Clock(int day, int hour, int bloodMoonDay, int dayLight = 18) =>
            GameClock.FromWorldTime((ulong)((day - 1) * 24000 + hour * 1000), bloodMoonDay, dayLight);

        // --- When the window is open (default settings: 2 hours before dusk, voting during it allowed) ---

        [TestCase(9, 19, 9, false, ExpectedResult = false, TestName = "Closed three hours before dusk")]
        [TestCase(9, 20, 9, false, ExpectedResult = true, TestName = "Opens two hours before dusk")]
        [TestCase(9, 22, 9, true, ExpectedResult = true, TestName = "Open while the blood moon runs")]
        [TestCase(10, 3, 9, true, ExpectedResult = true, TestName = "Open past midnight while the stat still names the blood moon")]
        [TestCase(10, 4, 16, false, ExpectedResult = false, TestName = "Closed at dawn")]
        [TestCase(7, 21, 9, false, ExpectedResult = false, TestName = "Closed on day 7 when BloodMoonRange moved it to day 9")]
        [TestCase(14, 21, 0, false, ExpectedResult = false, TestName = "Closed when blood moons are off")]
        public bool VoteWindow(int day, int hour, int bloodMoonDay, bool isBloodMoon) =>
            BloodMoonVoteFeature.IsVoteWindowOpen(Sky(day, hour, bloodMoonDay, isBloodMoon), new BloodMoonVoteSettings());

        [Test]
        public void VoteWindowFollowsAnEarlierDusk()
        {
            var settings = new BloodMoonVoteSettings { AllowVoteHoursBefore = 2 };

            Assert.That(BloodMoonVoteFeature.IsVoteWindowOpen(Sky(9, 15, 9, dusk: 18), settings), Is.False);
            Assert.That(BloodMoonVoteFeature.IsVoteWindowOpen(Sky(9, 16, 9, dusk: 18), settings), Is.True);
        }

        [Test]
        public void NoHoursBeforeMeansOnlyDuringTheBloodMoon()
        {
            var settings = new BloodMoonVoteSettings { AllowVoteHoursBefore = 0 };

            Assert.That(BloodMoonVoteFeature.IsVoteWindowOpen(Sky(9, 21, 9), settings), Is.False);
            Assert.That(BloodMoonVoteFeature.IsVoteWindowOpen(Sky(9, 22, 9, isBloodMoon: true), settings), Is.True);
        }

        [Test]
        public void ClosesAtDuskWhenVotingDuringTheBloodMoonIsOff()
        {
            var settings = new BloodMoonVoteSettings { AllowVoteDuringBloodMoon = false };

            Assert.That(BloodMoonVoteFeature.IsVoteWindowOpen(Sky(9, 21, 9), settings), Is.True);
            Assert.That(BloodMoonVoteFeature.IsVoteWindowOpen(Sky(9, 22, 9, isBloodMoon: true), settings), Is.False);
        }

        // --- The feature following the sky ---

        [Test]
        public void OpensForTheGamesBloodMoonDayAndClosesAtDawn()
        {
            _bus.Publish(Sky(7, 21, 9));
            Assert.That(_feature.GetVoteStatus().IsActive, Is.False, "day 7 isn't the blood moon when the game picked day 9");

            _bus.Publish(Sky(9, 20, 9));
            Assert.That(_feature.GetVoteStatus().IsActive, Is.True);
            Assert.That(_feature.GetVoteStatus().BloodMoonDay, Is.EqualTo(9));

            _bus.Publish(Sky(10, 2, 9, isBloodMoon: true));
            Assert.That(_feature.GetVoteStatus().IsActive, Is.True);

            _bus.Publish(Sky(10, 4, 16));
            Assert.That(_feature.GetVoteStatus().IsActive, Is.False);
        }

        [Test]
        public void TheSkyWatcherDrivesTheVote()
        {
            var watcher = new SkyWatcher(_bus);

            watcher.Observe(Clock(9, 19, 9));
            Assert.That(_feature.GetVoteStatus().IsActive, Is.False);

            watcher.Observe(Clock(9, 20, 9));
            Assert.That(_feature.GetVoteStatus().IsActive, Is.True);
            Assert.That(_feature.GetVoteStatus().BloodMoonDay, Is.EqualTo(9));

            watcher.Observe(Clock(10, 4, 16));
            Assert.That(_feature.GetVoteStatus().IsActive, Is.False);
        }

        [Test]
        public void DoesNothingWhileDisabled()
        {
            _feature.UpdateSettings(new BloodMoonVoteSettings { Enabled = false });

            _bus.Publish(Sky(9, 22, 9, isBloodMoon: true));

            Assert.That(_feature.GetVoteStatus().IsActive, Is.False);
        }

        // --- Where a passed vote moves the clock ---

        [TestCase(9, 21, 9, 18, 10, 6, TestName = "Passed before dusk, next morning")]
        [TestCase(9, 23, 9, 18, 10, 6, TestName = "Passed before midnight, next morning")]
        [TestCase(10, 2, 9, 18, 10, 6, TestName = "Passed after midnight, this morning, not a day later")]
        [TestCase(10, 7, 9, 6, 10, 9, TestName = "Dawn later than six, skips to dawn")]
        [TestCase(12, 10, 14, 18, 13, 6, TestName = "Forced outside a blood moon, next morning")]
        public void SkipTarget(int day, int hour, int bloodMoonDay, int dayLight, int expectedDay, int expectedHour)
        {
            var target = BloodMoonVoteFeature.SkipTarget(Clock(day, hour, bloodMoonDay, dayLight));

            Assert.That(target, Is.EqualTo((expectedDay, expectedHour)));
        }
    }
}
