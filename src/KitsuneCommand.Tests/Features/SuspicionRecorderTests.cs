using System;
using System.Collections.Generic;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.Features.Suspicion;

namespace KitsuneCommand.Tests.Features
{
    [TestFixture]
    public class SuspicionRecorderTests
    {
        private Mock<ISuspicionEventRepository> _repo;
        private List<SuspicionEvent> _inserted;
        private List<(string Url, SuspicionEvent Evt)> _alerts;
        private DateTime _now;
        private SuspicionRecorder _recorder;
        private SuspicionSettings _settings;

        private static readonly SuspectPlayer Ada = new SuspectPlayer
        {
            PlayerId = "EOS_ada", PlatformId = "Steam_76561198000000001", PlayerName = "Ada", EntityId = 171,
        };

        [SetUp]
        public void SetUp()
        {
            _inserted = new List<SuspicionEvent>();
            _alerts = new List<(string, SuspicionEvent)>();
            _repo = new Mock<ISuspicionEventRepository>();
            _repo.Setup(r => r.Insert(It.IsAny<SuspicionEvent>()))
                 .Returns<SuspicionEvent>(e => { _inserted.Add(e); return _inserted.Count; });
            _now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
            _recorder = new SuspicionRecorder(_repo.Object, (url, e) => _alerts.Add((url, e)), () => _now);
            _settings = new SuspicionSettings
            {
                RepeatCooldownMinutes = 10,
                DiscordWebhookUrl = "https://discord.example/webhook",
                DiscordMinSeverity = "medium",
                DiscordCooldownMinutes = 30,
            };
        }

        private static SuspicionFlag Flag(string detector = "movement", string rule = "speed", string severity = "medium", string dedup = null) =>
            new SuspicionFlag
            {
                Detector = detector, Rule = rule, Severity = severity, DedupKey = dedup, Summary = "s",
                Details = new Dictionary<string, object> { ["speed"] = 22.5 }, X = 1, Y = 2, Z = 3,
            };

        [Test]
        public void Record_StoresPlayerFlagDetailsAndPosition()
        {
            var evt = _recorder.Record(Ada, Flag(), _settings);

            Assert.That(evt, Is.Not.Null);
            Assert.That(evt.Id, Is.EqualTo(1));
            var row = _inserted[0];
            Assert.That(row.PlayerId, Is.EqualTo("EOS_ada"));
            Assert.That(row.PlayerName, Is.EqualTo("Ada"));
            Assert.That(row.EntityId, Is.EqualTo(171));
            Assert.That(row.Detector, Is.EqualTo("movement"));
            Assert.That(row.Rule, Is.EqualTo("speed"));
            Assert.That(row.Severity, Is.EqualTo("medium"));
            Assert.That((double)JObject.Parse(row.Details)["speed"], Is.EqualTo(22.5));
            Assert.That(row.PosX, Is.EqualTo(1));
            Assert.That(row.PosZ, Is.EqualTo(3));
        }

        [Test]
        public void Admins_AreExemptByDefault()
        {
            var admin = new SuspectPlayer { PlayerId = "EOS_admin", PlayerName = "Boss", IsAdmin = true };
            Assert.That(_recorder.Record(admin, Flag(), _settings), Is.Null);

            _settings.IgnoreAdmins = false;
            Assert.That(_recorder.Record(admin, Flag(), _settings), Is.Not.Null);
        }

        [TestCase("EOS_ada")]
        [TestCase("Steam_76561198000000001")]
        [TestCase("  eos_ADA  ")]
        public void ExemptPlayerIds_MatchCrossplatformOrPlatformId(string id)
        {
            _settings.ExemptPlayerIds = new List<string> { id };
            Assert.That(SuspicionRecorder.IsExempt(Ada, _settings), Is.True);
            Assert.That(_recorder.Record(Ada, Flag(), _settings), Is.Null);
            Assert.That(_inserted, Is.Empty);
        }

        [Test]
        public void SameCheckAgain_InsideTheCooldown_IsNotRecorded()
        {
            Assert.That(_recorder.Record(Ada, Flag(), _settings), Is.Not.Null);
            _now = _now.AddMinutes(9);
            Assert.That(_recorder.Record(Ada, Flag(), _settings), Is.Null);
            _now = _now.AddMinutes(2);
            Assert.That(_recorder.Record(Ada, Flag(), _settings), Is.Not.Null);
            Assert.That(_inserted, Has.Count.EqualTo(2));
        }

        [Test]
        public void DifferentDedupKeysOrPlayers_AreRecordedSeparately()
        {
            _recorder.Record(Ada, Flag("inventory", "over_stack", "high", "over_stack:resourceWood"), _settings);
            _recorder.Record(Ada, Flag("inventory", "over_stack", "high", "over_stack:resourceIron"), _settings);
            _recorder.Record(new SuspectPlayer { PlayerId = "EOS_bob", PlayerName = "Bob" },
                Flag("inventory", "over_stack", "high", "over_stack:resourceWood"), _settings);
            Assert.That(_inserted, Has.Count.EqualTo(3));
        }

        [Test]
        public void Discord_RespectsMinimumSeverity()
        {
            _recorder.Record(Ada, Flag(rule: "a", severity: "low"), _settings);
            Assert.That(_alerts, Is.Empty);
            Assert.That(_inserted, Has.Count.EqualTo(1), "low flags are still logged");

            _recorder.Record(Ada, Flag(rule: "b", severity: "high"), _settings);
            Assert.That(_alerts, Has.Count.EqualTo(1));
            Assert.That(_alerts[0].Url, Is.EqualTo("https://discord.example/webhook"));
        }

        [Test]
        public void Discord_IsRateLimitedPerPlayerPerDetector()
        {
            _recorder.Record(Ada, Flag("movement", "speed"), _settings);
            _recorder.Record(Ada, Flag("movement", "flying"), _settings);       // same detector: suppressed
            _recorder.Record(Ada, Flag("killrate", "kill_rate"), _settings);    // other detector: sent
            _recorder.Record(new SuspectPlayer { PlayerId = "EOS_bob" }, Flag("movement", "speed"), _settings); // other player: sent
            Assert.That(_alerts, Has.Count.EqualTo(3));
            Assert.That(_inserted, Has.Count.EqualTo(4), "rate limiting never drops the log row");

            _now = _now.AddMinutes(31);
            _recorder.Record(Ada, Flag("movement", "teleport_jump"), _settings);
            Assert.That(_alerts, Has.Count.EqualTo(4));
        }

        [Test]
        public void Discord_IsSkippedWithoutAWebhook()
        {
            _settings.DiscordWebhookUrl = "  ";
            _recorder.Record(Ada, Flag(severity: "high"), _settings);
            Assert.That(_alerts, Is.Empty);
        }

        [Test]
        public void PlayerWithoutId_IsKeyedByEntity()
        {
            var anon = new SuspectPlayer { EntityId = 5 };
            Assert.That(_recorder.Record(anon, Flag(), _settings), Is.Not.Null);
            Assert.That(_recorder.Record(anon, Flag(), _settings), Is.Null);
            Assert.That(_recorder.Record(new SuspectPlayer { EntityId = 6 }, Flag(), _settings), Is.Not.Null);
        }

        [TestCase("high", 2)]
        [TestCase("HIGH", 2)]
        [TestCase("medium", 1)]
        [TestCase("low", 0)]
        [TestCase(null, 0)]
        [TestCase("bogus", 0)]
        public void SeverityRank(string severity, int rank)
        {
            Assert.That(SuspicionSeverity.Rank(severity), Is.EqualTo(rank));
        }
    }
}
