using System.Collections.Generic;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.Services;

namespace KitsuneCommand.Tests.Features
{
    [TestFixture]
    public class SuspicionFeatureTests
    {
        [Test]
        public void Normalize_FillsMissingSections()
        {
            var s = SuspicionFeature.Normalize(new SuspicionSettings
            {
                Inventory = null, Movement = null, Progression = null, KillRate = null, ExemptPlayerIds = null,
            });

            Assert.That(s.Inventory, Is.Not.Null);
            Assert.That(s.Movement, Is.Not.Null);
            Assert.That(s.Progression, Is.Not.Null);
            Assert.That(s.KillRate, Is.Not.Null);
            Assert.That(s.ExemptPlayerIds, Is.Empty);
        }

        [Test]
        public void Normalize_ClampsOutOfRangeValues()
        {
            var s = new SuspicionSettings
            {
                RepeatCooldownMinutes = -5,
                RetentionDays = 999999,
                DiscordMinSeverity = "critical",
            };
            s.Inventory.ScanIntervalSeconds = 1;
            s.Movement.SpeedSamplesRequired = 0;
            s.Movement.MaxHorizontalSpeed = double.NaN;
            s.Progression.WindowMinutes = 0;

            s = SuspicionFeature.Normalize(s);

            Assert.That(s.RepeatCooldownMinutes, Is.EqualTo(0));
            Assert.That(s.RetentionDays, Is.EqualTo(3650));
            Assert.That(s.DiscordMinSeverity, Is.EqualTo("medium"));
            Assert.That(s.Inventory.ScanIntervalSeconds, Is.EqualTo(15), "the scan never runs more than every 15 s");
            Assert.That(s.Movement.SpeedSamplesRequired, Is.EqualTo(1));
            Assert.That(s.Movement.MaxHorizontalSpeed, Is.EqualTo(0));
            Assert.That(s.Progression.WindowMinutes, Is.EqualTo(1));
        }

        [Test]
        public void Normalize_CleansListsAndSeverity()
        {
            var s = new SuspicionSettings { DiscordMinSeverity = " HIGH ", ExemptPlayerIds = new List<string> { " a ", "", "A", null } };
            s.Inventory.AllowList = new List<string> { "x", " x ", "  ", "y*" };

            s = SuspicionFeature.Normalize(s);

            Assert.That(s.DiscordMinSeverity, Is.EqualTo("high"));
            Assert.That(s.ExemptPlayerIds, Is.EqualTo(new[] { "a" }));
            Assert.That(s.Inventory.AllowList, Is.EqualTo(new[] { "x", "y*" }));
        }

        [Test]
        public void SavedLists_ReplaceTheSeededDefaults()
        {
            // Without Replace, Json.NET appends onto the default allow-list and an
            // admin could never remove a seeded entry.
            var loaded = JsonConvert.DeserializeObject<SuspicionSettings>(
                "{\"Inventory\":{\"AllowList\":[\"onlyThis\"]},\"ExemptPlayerIds\":[\"p1\"]}");
            Assert.That(loaded.Inventory.AllowList, Is.EqualTo(new[] { "onlyThis" }));
            Assert.That(loaded.ExemptPlayerIds, Is.EqualTo(new[] { "p1" }));

            var emptied = JsonConvert.DeserializeObject<SuspicionSettings>("{\"Inventory\":{\"AllowList\":[]}}");
            Assert.That(emptied.Inventory.AllowList, Is.Empty);
        }

        [Test]
        public void PartialJson_KeepsDefaultsForMissingFields()
        {
            var loaded = JsonConvert.DeserializeObject<SuspicionSettings>("{\"Movement\":{\"MaxHorizontalSpeed\":25}}");
            Assert.That(loaded.Movement.MaxHorizontalSpeed, Is.EqualTo(25));
            Assert.That(loaded.Movement.JumpDistance, Is.EqualTo(80));
            Assert.That(loaded.Inventory.FlagDevItems, Is.True);
            Assert.That(loaded.Inventory.FlagHiddenItems, Is.False);
        }

        [Test]
        public void UpdateSettings_NormalizesAndPersistsUnderSuspicionKey()
        {
            var settingsRepo = new Mock<ISettingsRepository>();
            string saved = null;
            settingsRepo.Setup(r => r.Set("Suspicion", It.IsAny<string>())).Callback<string, string>((_, v) => saved = v);

            var bus = new ModEventBus();
            var feature = new SuspicionFeature(bus, new ConfigManager(new AppSettings()),
                new Mock<ISuspicionEventRepository>().Object, settingsRepo.Object,
                new LivePlayerManager(bus), new GameItemCatalog(), new DiscordWebhookService());

            var update = new SuspicionSettings { RetentionDays = -1 };
            update.KillRate.MaxZombieKillsPerMinute = 45;
            feature.UpdateSettings(update);

            Assert.That(feature.Settings.RetentionDays, Is.EqualTo(0));
            var persisted = JsonConvert.DeserializeObject<SuspicionSettings>(saved);
            Assert.That(persisted.KillRate.MaxZombieKillsPerMinute, Is.EqualTo(45));
        }

        [TestCase("INF Executing command 'teleportplayer 171 10 60 20' by Telnet from 127.0.0.1:5000", "171")]
        [TestCase("Executing command 'tele Ada 0 80 0' from client EntityID=171", "Ada")]
        [TestCase("Executing command 'teleportplayer \"Ada Vale\" 0 80 0'", "Ada Vale")]
        [TestCase("Executing command 'TELE Steam_76561198000000001 1 2 3'", "Steam_76561198000000001")]
        [TestCase("Executing command 'tele Ada' by Telnet", "Ada")]
        [TestCase("Executing command 'say hello'", null)]
        [TestCase("Executing command 'teleportplayer'", null)]
        [TestCase("Player Ada teleported somewhere", null)]
        [TestCase("", null)]
        [TestCase(null, null)]
        public void ParseConsoleTeleportTarget(string line, string expected)
        {
            Assert.That(SuspicionFeature.ParseConsoleTeleportTarget(line), Is.EqualTo(expected));
        }
    }
}
