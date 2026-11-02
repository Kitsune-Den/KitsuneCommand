using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Features;
using KitsuneCommand.Features.Suspicion;

namespace KitsuneCommand.Tests.Features
{
    [TestFixture]
    public class MovementDetectorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

        private MovementDetector _detector;
        private MovementDetectorSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _detector = new MovementDetector();
            _settings = new MovementDetectorSettings
            {
                MaxHorizontalSpeed = 15,
                SpeedSamplesRequired = 2,
                JumpDistance = 80,
                FlyHeight = 12,
                FlySamplesRequired = 3,
                GraceSeconds = 10,
            };
        }

        private static PlayerSample At(double seconds, double x, double z, double y = 60, double? ground = 59, bool attached = false) =>
            new PlayerSample { EntityId = 1, At = T0.AddSeconds(seconds), X = x, Y = y, Z = z, GroundHeight = ground, Attached = attached };

        private List<SuspicionFlag> Feed(params PlayerSample[] samples) =>
            samples.SelectMany(s => _detector.Observe(s, _settings)).ToList();

        [Test]
        public void FirstSample_IsOnlyABaseline()
        {
            Assert.That(Feed(At(0, 5000, 5000)), Is.Empty);
        }

        [Test]
        public void NormalSprint_IsFine()
        {
            Assert.That(Feed(At(0, 0, 0), At(3, 21, 0), At(6, 42, 0), At(9, 63, 0)), Is.Empty);
        }

        [Test]
        public void SustainedSpeed_IsFlaggedOncePerStreak()
        {
            // 20 m/s for four samples: flagged when the second over-speed sample arrives, not again.
            var flags = Feed(At(0, 0, 0), At(3, 60, 0), At(6, 120, 0), At(9, 180, 0), At(12, 240, 0));

            var flag = flags.Single();
            Assert.That(flag.Rule, Is.EqualTo("speed"));
            Assert.That(flag.Details["speed"], Is.EqualTo(20.0));
            Assert.That(flag.X, Is.EqualTo(120));
        }

        [Test]
        public void OneFastSample_IsNotEnough()
        {
            Assert.That(Feed(At(0, 0, 0), At(3, 60, 0), At(6, 70, 0), At(9, 130, 0)), Is.Empty);
        }

        [Test]
        public void VerticalMovement_DoesNotCountTowardSpeed()
        {
            Assert.That(Feed(At(0, 0, 0, y: 200, ground: null), At(3, 0, 0, y: 120, ground: null), At(6, 0, 0, y: 40, ground: null)), Is.Empty);
        }

        [Test]
        public void SingleSampleJump_IsFlaggedAsTeleport()
        {
            var flag = Feed(At(0, 0, 0), At(3, 300, 400)).Single();

            Assert.That(flag.Rule, Is.EqualTo("teleport_jump"));
            Assert.That(flag.Details["distance"], Is.EqualTo(500.0));
            Assert.That(flag.Details["fromX"], Is.EqualTo(0.0));
        }

        [Test]
        public void Jump_AcrossAStalledGap_IsNotTrusted()
        {
            // 300 m over 30 s (a main-thread stall) is 10 m/s: neither a jump nor speeding.
            Assert.That(Feed(At(0, 0, 0), At(30, 300, 0)), Is.Empty);
        }

        [Test]
        public void Vehicle_IsExcluded_OnEitherEndOfTheSample()
        {
            Assert.That(Feed(
                At(0, 0, 0, attached: true),
                At(3, 300, 0, attached: true),
                At(6, 600, 0, attached: true),
                At(9, 650, 0)), Is.Empty, "getting off a fast vehicle mid-sample isn't a jump");
        }

        [Test]
        public void Grace_DropsTheBaseline_SoATeleportIsNotMeasured()
        {
            Feed(At(0, 0, 0));
            _detector.Grace(1, T0.AddSeconds(4));

            Assert.That(Feed(At(3, 5000, 5000), At(6, 5010, 5000)), Is.Empty);
        }

        [Test]
        public void AfterGrace_DetectionResumes()
        {
            _detector.Grace(1, T0.AddSeconds(4));
            var flags = Feed(At(3, 5000, 5000), At(6, 5000, 5000), At(9, 9000, 5000));
            Assert.That(flags.Single().Rule, Is.EqualTo("teleport_jump"));
        }

        [Test]
        public void Forget_StartsThePlayerOver()
        {
            Feed(At(0, 0, 0));
            _detector.Forget(1);
            Assert.That(Feed(At(3, 9000, 0)), Is.Empty);
        }

        [Test]
        public void HoveringHighAboveGround_IsFlaggedAsFlying()
        {
            // Ground block at 59 → standing height 60; hovering at 90 = 30 m up.
            var flags = Feed(
                At(0, 0, 0, y: 90), At(3, 5, 0, y: 90), At(6, 10, 0, y: 91), At(9, 15, 0, y: 90), At(12, 20, 0, y: 90));

            var flag = flags.Single();
            Assert.That(flag.Rule, Is.EqualTo("flying"));
            Assert.That(flag.Details["height"], Is.EqualTo(30.0));
        }

        [Test]
        public void Falling_IsNotFlying()
        {
            var flags = Feed(
                At(0, 0, 0, y: 200), At(3, 0, 0, y: 180), At(6, 0, 0, y: 160), At(9, 0, 0, y: 140), At(12, 0, 0, y: 120));
            Assert.That(flags, Is.Empty);
        }

        [Test]
        public void StandingOnATallBuilding_IsNotFlying()
        {
            // The highest block in the column is the roof the player stands on.
            var flags = Feed(
                At(0, 0, 0, y: 150, ground: 149), At(3, 1, 0, y: 150, ground: 149),
                At(6, 2, 0, y: 150, ground: 149), At(9, 3, 0, y: 150, ground: 149));
            Assert.That(flags, Is.Empty);
        }

        [Test]
        public void UnknownGround_IsNotFlying()
        {
            var flags = Feed(
                At(0, 0, 0, y: 200, ground: null), At(3, 0, 0, y: 200, ground: null),
                At(6, 0, 0, y: 200, ground: null), At(9, 0, 0, y: 200, ground: null));
            Assert.That(flags, Is.Empty);
        }

        [Test]
        public void Flying_CanBeSwitchedOff()
        {
            _settings.FlagFlying = false;
            var flags = Feed(At(0, 0, 0, y: 90), At(3, 0, 0, y: 90), At(6, 0, 0, y: 90), At(9, 0, 0, y: 90));
            Assert.That(flags, Is.Empty);
        }

        [Test]
        public void ShortHop_ResetsTheAirborneCount()
        {
            var flags = Feed(
                At(0, 0, 0, y: 90), At(3, 0, 0, y: 90),
                At(6, 0, 0, y: 60),                     // landed
                At(9, 0, 0, y: 90), At(12, 0, 0, y: 90));
            Assert.That(flags, Is.Empty);
        }
    }
}
