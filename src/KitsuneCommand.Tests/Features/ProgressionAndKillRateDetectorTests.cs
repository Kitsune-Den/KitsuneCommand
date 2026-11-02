using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Features;
using KitsuneCommand.Features.Suspicion;

namespace KitsuneCommand.Tests.Features
{
    [TestFixture]
    public class ProgressionDetectorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

        private ProgressionDetector _detector;
        private ProgressionDetectorSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _detector = new ProgressionDetector();
            _settings = new ProgressionDetectorSettings
            {
                WindowMinutes = 10,
                MaxLevelGain = 10,
                MaxUnexplainedSkillPoints = 5,
                WarmupSeconds = 60,
            };
        }

        private List<SuspicionFlag> Observe(double minutes, int level, int points, int spl = 1) =>
            _detector.Observe(new PlayerSample { EntityId = 1, At = T0.AddMinutes(minutes), Level = level, SkillPoints = points }, _settings, spl);

        [Test]
        public void SteadyLevelling_IsFine()
        {
            for (var i = 0; i < 20; i++)
                Assert.That(Observe(i, 20 + i, 3 + i), Is.Empty);
        }

        [Test]
        public void LevelJump_InsideTheWindow_IsFlagged()
        {
            Observe(0, 20, 0);
            var flag = Observe(2, 35, 15).Single();

            Assert.That(flag.Rule, Is.EqualTo("level_jump"));
            Assert.That(flag.Severity, Is.EqualTo(SuspicionSeverity.High));
            Assert.That(flag.Details["fromLevel"], Is.EqualTo(20));
            Assert.That(flag.Details["toLevel"], Is.EqualTo(35));
        }

        [Test]
        public void LargeGain_SpreadOverTwiceTheWindow_IsFine()
        {
            Observe(0, 20, 0);
            Observe(10, 27, 7);
            Assert.That(Observe(20, 35, 15), Is.Empty);
        }

        [Test]
        public void ValueAtWindowStart_IsTheBaseline_EvenWithoutRecentChanges()
        {
            // Level 20 since t=0 with no change; a jump at t=30 must still be measured from 20.
            Observe(0, 20, 0);
            Observe(15, 20, 0);
            Assert.That(Observe(30, 40, 20).Select(f => f.Rule), Does.Contain("level_jump"));
        }

        [Test]
        public void SkillPoints_WithoutLevels_AreFlagged()
        {
            Observe(0, 30, 2);
            var flag = Observe(1, 30, 20).Single();

            Assert.That(flag.Rule, Is.EqualTo("skill_points"));
            Assert.That(flag.Details["unexplained"], Is.EqualTo(18));
        }

        [Test]
        public void SkillPoints_ExplainedByLevels_AreFine()
        {
            Observe(0, 30, 0);
            Assert.That(Observe(5, 38, 8), Is.Empty);
        }

        [Test]
        public void SkillPointsPerLevel_IsHonoured()
        {
            Observe(0, 30, 0);
            Assert.That(Observe(5, 34, 8, spl: 2), Is.Empty);
        }

        [Test]
        public void SpendingThenCheating_IsStillCaught()
        {
            Observe(0, 30, 10);
            Observe(1, 30, 0);   // spent them all
            var flag = Observe(2, 30, 9).Single();
            Assert.That(flag.Rule, Is.EqualTo("skill_points"));
            Assert.That(flag.Details["unexplained"], Is.EqualTo(9));
        }

        [Test]
        public void AfterAFlag_TheSameGainIsNotReportedAgain()
        {
            Observe(0, 20, 0);
            Assert.That(Observe(1, 40, 20), Is.Not.Empty);
            Assert.That(Observe(2, 40, 20), Is.Empty);
        }

        [Test]
        public void Warmup_AfterSpawn_IgnoresTheLateProgressionLoad()
        {
            // The server copy reads level 1 until the player's save arrives, then their real level.
            _detector.Restart(1, T0, TimeSpan.FromSeconds(60));
            Assert.That(Observe(0.1, 1, 0), Is.Empty);
            Assert.That(Observe(0.5, 80, 3), Is.Empty);
            Assert.That(Observe(2, 80, 3), Is.Empty, "first post-warmup sample is the baseline");
            Assert.That(Observe(3, 81, 4), Is.Empty);
        }

        [Test]
        public void MissingProgression_IsSkipped()
        {
            var flags = _detector.Observe(new PlayerSample { EntityId = 1, At = T0, Level = null, SkillPoints = 3 }, _settings, 1);
            Assert.That(flags, Is.Empty);
        }
    }

    [TestFixture]
    public class KillRateDetectorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 23, 22, 0, 0, DateTimeKind.Utc);

        private static SuspicionFlag Burst(KillRateDetector d, int kills, double overSeconds, int limit)
        {
            var settings = new KillRateDetectorSettings { MaxZombieKillsPerMinute = limit };
            SuspicionFlag last = null;
            for (var i = 0; i < kills; i++)
            {
                var f = d.Observe(7, T0.AddSeconds(overSeconds * i / kills), settings);
                if (f != null) last = f;
            }
            return last;
        }

        [Test]
        public void AtTheCeiling_IsFine()
        {
            Assert.That(Burst(new KillRateDetector(), 60, 59, 60), Is.Null);
        }

        [Test]
        public void OverTheCeiling_IsMedium()
        {
            var flag = Burst(new KillRateDetector(), 61, 59, 60);
            Assert.That(flag.Rule, Is.EqualTo("kill_rate"));
            Assert.That(flag.Severity, Is.EqualTo(SuspicionSeverity.Medium));
            Assert.That(flag.Details["kills"], Is.EqualTo(61));
        }

        [Test]
        public void TwiceTheCeiling_EscalatesToHigh_Once()
        {
            var d = new KillRateDetector();
            var settings = new KillRateDetectorSettings { MaxZombieKillsPerMinute = 10 };
            var flags = Enumerable.Range(0, 25).Select(i => d.Observe(7, T0.AddSeconds(i * 0.4), settings)).Where(f => f != null).ToList();

            Assert.That(flags.Select(f => f.Severity), Is.EqualTo(new[] { SuspicionSeverity.Medium, SuspicionSeverity.High }));
            Assert.That(flags[1].Details["kills"], Is.EqualTo(20));
            Assert.That(flags[0].DedupKey, Is.Not.EqualTo(flags[1].DedupKey));
        }

        [Test]
        public void ANewBurst_AfterTheRateDrops_IsFlaggedAgain()
        {
            var d = new KillRateDetector();
            var settings = new KillRateDetectorSettings { MaxZombieKillsPerMinute = 10 };
            var first = Enumerable.Range(0, 11).Select(i => d.Observe(7, T0.AddSeconds(i), settings)).Count(f => f != null);
            d.Observe(7, T0.AddMinutes(5), settings); // quiet: the window empties
            var second = Enumerable.Range(0, 11).Select(i => d.Observe(7, T0.AddMinutes(10).AddSeconds(i), settings)).Count(f => f != null);

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(1));
        }

        [Test]
        public void KillersAreCountedSeparately()
        {
            var d = new KillRateDetector();
            var settings = new KillRateDetectorSettings { MaxZombieKillsPerMinute = 10 };
            var flags = Enumerable.Range(0, 20).Select(i => d.Observe(i % 2, T0.AddSeconds(i), settings)).Where(f => f != null);
            Assert.That(flags, Is.Empty);
        }

        [Test]
        public void KillsOlderThanAMinute_DropOff()
        {
            // 120 kills spread over three minutes is 40/min.
            Assert.That(Burst(new KillRateDetector(), 120, 180, 60), Is.Null);
        }

        [Test]
        public void OneBurst_IsOneFlag()
        {
            var d = new KillRateDetector();
            var settings = new KillRateDetectorSettings { MaxZombieKillsPerMinute = 10 };
            var flags = Enumerable.Range(0, 15).Select(i => d.Observe(7, T0.AddSeconds(i), settings)).Where(f => f != null).ToList();
            Assert.That(flags, Has.Count.EqualTo(1));
        }

        [Test]
        public void ZeroCeiling_DisablesTheCheck()
        {
            Assert.That(Burst(new KillRateDetector(), 500, 10, 0), Is.Null);
        }
    }
}
