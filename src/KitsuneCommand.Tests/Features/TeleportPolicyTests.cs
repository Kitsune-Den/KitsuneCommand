using System;
using Moq;
using NUnit.Framework;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Tests.Features
{
    /// <summary>
    /// TeleportPolicy decides whether a player's own /home or /tp is refused because the
    /// admin turned off teleporting during the blood moon.
    /// </summary>
    [TestFixture]
    public class TeleportPolicyTests
    {
        private const int BloodMoonDay = 14;
        private const int DefaultDayLight = 18; // dusk 22:00, dawn 04:00

        private static readonly TeleportSettings Blocked = new TeleportSettings { AllowTeleportDuringBloodMoon = false };

        /// <summary>The clock at a day and time, with the next blood moon on day 14 unless told otherwise.</summary>
        private static GameClock Clock(int day, int hour, int minute, int bloodMoonDay = BloodMoonDay, int dayLight = DefaultDayLight)
        {
            var worldTime = (ulong)((day - 1) * 24000 + hour * 1000) + (ulong)(minute * 1000 + 59) / 60;
            return GameClock.FromWorldTime(worldTime, bloodMoonDay, dayLight);
        }

        // ── BloodMoonRefusal ────────────────────────────────────

        [Test]
        public void DefaultSettings_AllowTeleportDuringTheBloodMoon()
        {
            Assert.That(TeleportPolicy.BloodMoonRefusal(new TeleportSettings(), Clock(14, 23, 0)), Is.Null);
        }

        [Test]
        public void SettingOn_AllowsTeleportDuringTheBloodMoon()
        {
            var settings = new TeleportSettings { AllowTeleportDuringBloodMoon = true };
            Assert.That(TeleportPolicy.BloodMoonRefusal(settings, Clock(14, 23, 0)), Is.Null);
        }

        [Test]
        public void SettingOff_RefusesDuringTheBloodMoon_AndSaysWhenItEnds()
        {
            Assert.That(TeleportPolicy.BloodMoonRefusal(Blocked, Clock(14, 23, 0)),
                Is.EqualTo("Teleporting is turned off during the blood moon. Try again after dawn (04:00)."));
        }

        [TestCase(14, 21, 59, false)] // blood moon day, before dusk
        [TestCase(14, 22, 0, true)]   // dusk: the horde starts
        [TestCase(15, 0, 30, true)]   // past midnight, still the same blood moon
        [TestCase(15, 3, 59, true)]   // last minute before dawn
        [TestCase(15, 4, 0, false)]   // dawn: it's over
        [TestCase(13, 23, 0, false)]  // the night before
        [TestCase(10, 23, 0, false)]  // an ordinary night
        public void SettingOff_RefusesOnlyFromDuskToDawn(int day, int hour, int minute, bool refused)
        {
            var refusal = TeleportPolicy.BloodMoonRefusal(Blocked, Clock(day, hour, minute));
            Assert.That(refusal != null, Is.EqualTo(refused), $"day {day} {hour:D2}:{minute:D2}");
        }

        [Test]
        public void SettingOff_FollowsAShorterDay()
        {
            // 12 hours of daylight: dusk at 18:00, dawn at 06:00.
            Assert.That(TeleportPolicy.BloodMoonRefusal(Blocked, Clock(14, 17, 59, dayLight: 12)), Is.Null);
            Assert.That(TeleportPolicy.BloodMoonRefusal(Blocked, Clock(14, 18, 0, dayLight: 12)), Does.EndWith("after dawn (06:00)."));
            Assert.That(TeleportPolicy.BloodMoonRefusal(Blocked, Clock(15, 5, 59, dayLight: 12)), Is.Not.Null);
            Assert.That(TeleportPolicy.BloodMoonRefusal(Blocked, Clock(15, 6, 0, dayLight: 12)), Is.Null);
        }

        [Test]
        public void SettingOff_AllowsWhenBloodMoonsAreOff()
        {
            // BloodMoonFrequency 0 makes the game set the BloodMoonDay stat to 0.
            Assert.That(TeleportPolicy.BloodMoonRefusal(Blocked, Clock(1, 23, 0, bloodMoonDay: 0)), Is.Null);
        }

        [Test]
        public void SettingOff_AllowsWhenThereIsNoWorld()
        {
            Assert.That(TeleportPolicy.BloodMoonRefusal(Blocked, null), Is.Null);
        }

        [Test]
        public void NullSettings_Allow()
        {
            Assert.That(TeleportPolicy.BloodMoonRefusal(null, Clock(14, 23, 0)), Is.Null);
        }

        [Test]
        public void RefusalHasNoEmDash()
        {
            const char EmDash = (char)0x2014;
            Assert.That(TeleportPolicy.BloodMoonRefusal(Blocked, Clock(14, 23, 0)), Does.Not.Contain(EmDash.ToString()));
        }

        // ── LoadSettings ────────────────────────────────────────

        [Test]
        public void LoadSettings_ReadsTheSavedSetting()
        {
            var repo = new Mock<ISettingsRepository>();
            repo.Setup(r => r.Get(TeleportPolicy.SettingsKey))
                .Returns("{\"TeleportDelaySeconds\":5,\"DefaultPointsCost\":0,\"AllowTeleportDuringBloodMoon\":false}");

            Assert.That(TeleportPolicy.LoadSettings(repo.Object).AllowTeleportDuringBloodMoon, Is.False);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not json")]
        public void LoadSettings_FallsBackToDefaults(string saved)
        {
            var repo = new Mock<ISettingsRepository>();
            repo.Setup(r => r.Get(TeleportPolicy.SettingsKey)).Returns(saved);

            Assert.That(TeleportPolicy.LoadSettings(repo.Object).AllowTeleportDuringBloodMoon, Is.True);
        }

        [Test]
        public void LoadSettings_FallsBackToDefaultsWhenTheRepositoryThrows()
        {
            var repo = new Mock<ISettingsRepository>();
            repo.Setup(r => r.Get(It.IsAny<string>())).Throws(new InvalidOperationException("db locked"));

            Assert.That(TeleportPolicy.LoadSettings(repo.Object).AllowTeleportDuringBloodMoon, Is.True);
        }
    }
}
