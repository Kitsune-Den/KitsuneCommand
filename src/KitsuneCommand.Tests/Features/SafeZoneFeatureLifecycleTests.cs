using System.Collections.Generic;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;

namespace KitsuneCommand.Tests.Features
{
    [TestFixture]
    public class SafeZoneFeatureLifecycleTests
    {
        private Mock<ISettingsRepository> _repo;
        private SafeZoneFeature _feature;
        private string _saved;

        [SetUp]
        public void SetUp()
        {
            _saved = null;
            _repo = new Mock<ISettingsRepository>();
            _repo.Setup(r => r.Set("SafeZones", It.IsAny<string>())).Callback<string, string>((_, v) => _saved = v);
            _feature = new SafeZoneFeature(new ModEventBus(), new ConfigManager(new AppSettings()), _repo.Object);
            _feature.LoadSettings();
        }

        [TearDown]
        public void TearDown() => _feature.Stop();

        private static SafeZoneSettings TwoZones() => new SafeZoneSettings
        {
            Zones = new List<SafeZone>
            {
                new SafeZone { Id = "a", Name = "Town", MinX = 10, MaxX = -10, MinZ = 0, MaxZ = 5 },
                new SafeZone { Id = "b", Name = "Farm" },
            },
        };

        [Test]
        public void UpdateSettings_NormalizesAndPersists()
        {
            _feature.UpdateSettings(TwoZones());

            Assert.That(_feature.Settings.Zones[0].MinX, Is.EqualTo(-10));
            Assert.That(_saved, Is.Not.Null);
            var stored = JsonConvert.DeserializeObject<SafeZoneSettings>(_saved);
            Assert.That(stored.Zones, Has.Count.EqualTo(2));
            Assert.That(stored.Zones[0].MaxX, Is.EqualTo(10));
        }

        [Test]
        public void UpdateSettings_WhileRunning_KeepsRunning()
        {
            _feature.Start();
            _feature.UpdateSettings(new SafeZoneSettings { SweepIntervalSeconds = 5 });
            Assert.That(_feature.IsRunning, Is.True);
            Assert.That(_feature.Settings.SweepIntervalSeconds, Is.EqualTo(5));
        }

        [Test]
        public void RemovedCounts_StartsEmpty()
        {
            _feature.UpdateSettings(TwoZones());
            _feature.UpdateSettings(new SafeZoneSettings());
            Assert.That(_feature.RemovedCounts(), Is.Empty);
        }

        [Test]
        public void Start_LoadsPersistedZones()
        {
            _repo.Setup(r => r.Get("SafeZones")).Returns(JsonConvert.SerializeObject(TwoZones()));

            _feature.Start();

            Assert.That(_feature.IsRunning, Is.True);
            Assert.That(_feature.Settings.Zones, Has.Count.EqualTo(2));
            Assert.That(_feature.Settings.Zones[0].MinX, Is.EqualTo(-10), "persisted zones are normalized on load");
        }

        [Test]
        public void Start_FallsBackToDefaults_WhenNothingOrGarbageIsStored()
        {
            _repo.Setup(r => r.Get("SafeZones")).Returns((string)null);
            _feature.Start();
            Assert.That(_feature.Settings.Zones, Is.Empty);
            Assert.That(_feature.Settings.Enabled, Is.True);
            _feature.Stop();

            _repo.Setup(r => r.Get("SafeZones")).Returns("{not json");
            _feature.Start();
            Assert.That(_feature.IsRunning, Is.True);
            Assert.That(_feature.Settings.Zones, Is.Empty);
        }

        [Test]
        public void Stop_StopsTheFeature()
        {
            _feature.Start();
            _feature.Stop();
            Assert.That(_feature.IsRunning, Is.False);
        }
    }
}
