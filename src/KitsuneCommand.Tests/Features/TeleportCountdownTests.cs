using System;
using NUnit.Framework;
using KitsuneCommand.Features;

namespace KitsuneCommand.Tests.Features
{
    /// <summary>
    /// TeleportCountdown decides, tick by tick, whether a player's own /home or /tp waits,
    /// goes, or is cancelled, and what the player is told.
    /// </summary>
    [TestFixture]
    public class TeleportCountdownTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 25, 18, 0, 0, DateTimeKind.Utc);

        private static PlayerSnapshot At(float x, float y, float z, float health = 100f, bool dead = false) =>
            new PlayerSnapshot(x, y, z, health, dead);

        private static PendingTeleport Home(string name = "base") => new PendingTeleport
        {
            PlayerId = "Steam_1",
            PlayerName = "Fox",
            EntityId = 171,
            Kind = TeleportTargetKind.Home,
            TargetName = name,
            TargetPosition = "100 60 -200",
            X = 100, Y = 60, Z = -200
        };

        private static PendingTeleport City(string name = "Lakeside", int cost = 50) => new PendingTeleport
        {
            PlayerId = "Steam_1",
            PlayerName = "Fox",
            EntityId = 171,
            Kind = TeleportTargetKind.City,
            TargetName = name,
            TargetPosition = "0 70 0",
            PointsCost = cost
        };

        /// <summary>A home teleport started at T0 from (10, 50, 10) with full health and this delay.</summary>
        private static PendingTeleport Started(int delaySeconds = 5, float health = 100f)
        {
            var p = Home();
            TeleportCountdown.Start(p, At(10, 50, 10, health), T0, delaySeconds);
            return p;
        }

        // ── DelaySeconds ────────────────────────────────────────

        [Test]
        public void DefaultSettings_AreAFiveSecondCountdown()
        {
            Assert.That(TeleportCountdown.DelaySeconds(new TeleportSettings()), Is.EqualTo(5));
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(60, 60)]
        [TestCase(-5, 0)]   // a bad saved value is instant, not a negative wait
        [TestCase(600, 60)] // never more than the panel allows
        public void DelaySeconds_IsClampedToZeroToSixty(int saved, int expected)
        {
            var settings = new TeleportSettings { TeleportDelaySeconds = saved };
            Assert.That(TeleportCountdown.DelaySeconds(settings), Is.EqualTo(expected));
        }

        [Test]
        public void NoSettings_IsInstant()
        {
            Assert.That(TeleportCountdown.DelaySeconds(null), Is.EqualTo(0));
        }

        // ── Start ───────────────────────────────────────────────

        [Test]
        public void Start_RemembersWhereAndHowHealthyThePlayerWas_AndWhenItsDue()
        {
            var p = Home();
            TeleportCountdown.Start(p, At(1.5f, 62f, -3f, 87f), T0, 5);

            Assert.That(p.DueUtc, Is.EqualTo(T0.AddSeconds(5)));
            Assert.That(new[] { p.StartX, p.StartY, p.StartZ }, Is.EqualTo(new[] { 1.5f, 62f, -3f }));
            Assert.That(p.LastHealth, Is.EqualTo(87f));
        }

        // ── Step: waiting and going ─────────────────────────────

        [Test]
        public void StandingStill_WaitsUntilTheCountdownRunsOut()
        {
            var p = Started(5);

            Assert.That(TeleportCountdown.Step(p, At(10, 50, 10), T0.AddSeconds(1)).Outcome, Is.EqualTo(CountdownOutcome.Waiting));
            Assert.That(TeleportCountdown.Step(p, At(10, 50, 10), T0.AddSeconds(4.9)).Outcome, Is.EqualTo(CountdownOutcome.Waiting));
            Assert.That(TeleportCountdown.Step(p, At(10, 50, 10), T0.AddSeconds(5)).Outcome, Is.EqualTo(CountdownOutcome.Due));
        }

        [Test]
        public void LateTick_IsStillDue()
        {
            // A long frame or a save can hold the tick up; the teleport still happens.
            var p = Started(5);
            Assert.That(TeleportCountdown.Step(p, At(10, 50, 10), T0.AddSeconds(9)).Outcome, Is.EqualTo(CountdownOutcome.Due));
        }

        [Test]
        public void Due_HasNoReason()
        {
            var p = Started(1);
            Assert.That(TeleportCountdown.Step(p, At(10, 50, 10), T0.AddSeconds(1)).Reason, Is.Null);
        }

        // ── Step: moving ────────────────────────────────────────

        [TestCase(1.9f, 0f, 0f)]  // shuffling about
        [TestCase(0f, 1.5f, 0f)]  // a jump in place
        [TestCase(1.4f, 0f, 1.4f)] // ~1.98 m on the diagonal
        [TestCase(0f, 0f, -2f)]   // exactly at the edge
        public void SmallMoves_DontCancel(float dx, float dy, float dz)
        {
            var p = Started();
            var step = TeleportCountdown.Step(p, At(10 + dx, 50 + dy, 10 + dz), T0.AddSeconds(1));
            Assert.That(step.Outcome, Is.EqualTo(CountdownOutcome.Waiting));
        }

        [TestCase(2.1f, 0f, 0f)]  // walked off
        [TestCase(0f, -3f, 0f)]   // fell
        [TestCase(1.5f, 0f, 1.5f)] // ~2.12 m on the diagonal
        [TestCase(-40f, 0f, 25f)] // ran
        public void MovingAway_Cancels(float dx, float dy, float dz)
        {
            var p = Started();
            var step = TeleportCountdown.Step(p, At(10 + dx, 50 + dy, 10 + dz), T0.AddSeconds(1));

            Assert.That(step.Outcome, Is.EqualTo(CountdownOutcome.Cancelled));
            Assert.That(step.Reason, Is.EqualTo(TeleportCountdown.ReasonMoved));
        }

        [Test]
        public void Moving_IsMeasuredFromWhereTheyStarted_NotTheLastTick()
        {
            // 1.5 m a tick, never 2 m in one tick, still walks them out of range.
            var p = Started(10);
            Assert.That(TeleportCountdown.Step(p, At(11.5f, 50, 10), T0.AddSeconds(1)).Outcome, Is.EqualTo(CountdownOutcome.Waiting));
            Assert.That(TeleportCountdown.Step(p, At(13f, 50, 10), T0.AddSeconds(2)).Outcome, Is.EqualTo(CountdownOutcome.Cancelled));
        }

        [Test]
        public void MovingAway_OnTheLastTick_StillCancels()
        {
            var p = Started(5);
            var step = TeleportCountdown.Step(p, At(20, 50, 10), T0.AddSeconds(5));
            Assert.That(step.Outcome, Is.EqualTo(CountdownOutcome.Cancelled));
        }

        // ── Step: damage and death ──────────────────────────────

        [Test]
        public void LosingHealth_Cancels()
        {
            var p = Started(health: 100f);
            var step = TeleportCountdown.Step(p, At(10, 50, 10, health: 92f), T0.AddSeconds(1));

            Assert.That(step.Outcome, Is.EqualTo(CountdownOutcome.Cancelled));
            Assert.That(step.Reason, Is.EqualTo(TeleportCountdown.ReasonDamaged));
        }

        [Test]
        public void Healing_DoesntCancel_ButTheNextHitStillDoes()
        {
            var p = Started(10, health: 60f);

            // Bandaged up to 80 while waiting.
            Assert.That(TeleportCountdown.Step(p, At(10, 50, 10, 80f), T0.AddSeconds(1)).Outcome, Is.EqualTo(CountdownOutcome.Waiting));
            Assert.That(p.LastHealth, Is.EqualTo(80f));

            // Hit back down to 70: still above where they started, but it's damage.
            var step = TeleportCountdown.Step(p, At(10, 50, 10, 70f), T0.AddSeconds(2));
            Assert.That(step.Outcome, Is.EqualTo(CountdownOutcome.Cancelled));
            Assert.That(step.Reason, Is.EqualTo(TeleportCountdown.ReasonDamaged));
        }

        [Test]
        public void SameHealth_DoesntCancel()
        {
            var p = Started(health: 100f);
            Assert.That(TeleportCountdown.Step(p, At(10, 50, 10, 100f), T0.AddSeconds(1)).Outcome, Is.EqualTo(CountdownOutcome.Waiting));
        }

        [Test]
        public void TakingDamage_OnTheLastTick_StillCancels()
        {
            var p = Started(5, health: 100f);
            var step = TeleportCountdown.Step(p, At(10, 50, 10, 99f), T0.AddSeconds(5));
            Assert.That(step.Outcome, Is.EqualTo(CountdownOutcome.Cancelled));
        }

        [Test]
        public void Dying_Cancels_AndSaysSo()
        {
            var p = Started(health: 100f);
            var step = TeleportCountdown.Step(p, At(10, 50, 10, 0f, dead: true), T0.AddSeconds(1));

            Assert.That(step.Outcome, Is.EqualTo(CountdownOutcome.Cancelled));
            Assert.That(step.Reason, Is.EqualTo(TeleportCountdown.ReasonDied));
        }

        // ── Step: leaving ───────────────────────────────────────

        [Test]
        public void PlayerNotInTheWorld_CancelsWithNobodyToTell()
        {
            var p = Started();
            var step = TeleportCountdown.Step(p, null, T0.AddSeconds(1));

            Assert.That(step.Outcome, Is.EqualTo(CountdownOutcome.Cancelled));
            Assert.That(step.Reason, Is.Null);
        }

        [Test]
        public void PlayerNotInTheWorld_WhenDue_StillCancels()
        {
            var p = Started(5);
            Assert.That(TeleportCountdown.Step(p, null, T0.AddSeconds(6)).Outcome, Is.EqualTo(CountdownOutcome.Cancelled));
        }

        // ── A second command ────────────────────────────────────

        [Test]
        public void AnotherDestination_CancelsTheFirst_AndSaysSo()
        {
            Assert.That(TeleportCountdown.ReplacedMessage(Home("base"), City("Lakeside")),
                Is.EqualTo("Teleport to home 'base' cancelled: you asked for another teleport."));
            Assert.That(TeleportCountdown.ReplacedMessage(Home("base"), Home("mine")),
                Is.EqualTo("Teleport to home 'base' cancelled: you asked for another teleport."));
        }

        [Test]
        public void SameCommandAgain_JustRestarts_WithoutACancelMessage()
        {
            Assert.That(TeleportCountdown.ReplacedMessage(Home("base"), Home("BASE")), Is.Null);
            Assert.That(TeleportCountdown.ReplacedMessage(City("Lakeside"), City("Lakeside")), Is.Null);
        }

        [Test]
        public void AHomeAndACityWithTheSameName_AreDifferentDestinations()
        {
            Assert.That(TeleportCountdown.ReplacedMessage(Home("Lakeside"), City("Lakeside")), Is.Not.Null);
        }

        [Test]
        public void NothingPending_NothingToSay()
        {
            Assert.That(TeleportCountdown.ReplacedMessage(null, Home()), Is.Null);
        }

        // ── What the player is told ─────────────────────────────

        [Test]
        public void StartMessage_SaysWhereWhenAndWhatCancelsIt()
        {
            Assert.That(TeleportCountdown.StartMessage(Home("base"), 5),
                Is.EqualTo("Teleporting to home 'base' in 5s. Stand still: moving or taking damage cancels it."));
            Assert.That(TeleportCountdown.StartMessage(City("Lakeside"), 10),
                Is.EqualTo("Teleporting to Lakeside in 10s. Stand still: moving or taking damage cancels it."));
        }

        [TestCase(TeleportCountdown.ReasonMoved, "Teleport to home 'base' cancelled: you moved.")]
        [TestCase(TeleportCountdown.ReasonDamaged, "Teleport to home 'base' cancelled: you took damage.")]
        [TestCase(TeleportCountdown.ReasonDied, "Teleport to home 'base' cancelled: you died.")]
        public void CancelMessage_SaysWhy(string reason, string expected)
        {
            Assert.That(TeleportCountdown.CancelMessage(Home("base"), reason), Is.EqualTo(expected));
        }

        [Test]
        public void BloodMoonMessage_WrapsThePolicysRefusal()
        {
            var refusal = "Teleporting is turned off during the blood moon. Try again after dawn (04:00).";
            Assert.That(TeleportCountdown.BloodMoonMessage(City("Lakeside"), refusal),
                Is.EqualTo("Teleport to Lakeside cancelled. Teleporting is turned off during the blood moon. Try again after dawn (04:00)."));
        }

        [Test]
        public void NotEnoughPointsMessage_SaysTheyWerentCharged()
        {
            Assert.That(TeleportCountdown.NotEnoughPointsMessage(City("Lakeside", 50), 20),
                Is.EqualTo("Teleport to Lakeside cancelled: it costs 50 pts and you have 20. You were not charged."));
        }

        [Test]
        public void NoMessage_HasAnEmDash()
        {
            var messages = new[]
            {
                TeleportCountdown.StartMessage(Home(), 5),
                TeleportCountdown.CancelMessage(Home(), TeleportCountdown.ReasonMoved),
                TeleportCountdown.ReplacedMessage(Home("a"), Home("b")),
                TeleportCountdown.BloodMoonMessage(City(), "Refused."),
                TeleportCountdown.NotEnoughPointsMessage(City(), 0),
            };
            var emDash = ((char)0x2014).ToString();
            foreach (var m in messages)
                Assert.That(m, Does.Not.Contain(emDash), m);
        }

        // ── Odds and ends ───────────────────────────────────────

        [Test]
        public void CooldownGroup_MatchesTheCommand()
        {
            Assert.That(Home().CooldownGroup, Is.EqualTo("home"));
            Assert.That(City().CooldownGroup, Is.EqualTo("teleport"));
        }
    }
}
