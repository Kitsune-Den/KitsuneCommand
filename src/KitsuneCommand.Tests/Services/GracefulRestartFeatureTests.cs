using System.IO;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;

namespace KitsuneCommand.Tests.Services
{
    /// <summary>
    /// Unit tests for <see cref="GracefulRestartFeature"/> — focused on the
    /// "heal-on-read" path that recovers from a persisted timezone the host
    /// runtime can't resolve. The bug being guarded against: .NET Framework
    /// 4.8 / Windows can't resolve IANA IDs like "America/Los_Angeles", so
    /// the scheduled tick used to throw and log "bad schedule config" every
    /// 30s without ever firing the restart.
    /// </summary>
    [TestFixture]
    public class GracefulRestartFeatureTests
    {
        private Mock<ISettingsRepository> _mockSettingsRepo;
        private ModEventBus _bus;
        private ConfigManager _config;

        [SetUp]
        public void SetUp()
        {
            _mockSettingsRepo = new Mock<ISettingsRepository>();
            _bus = new ModEventBus();
            _config = new ConfigManager(new AppSettings());
        }

        /// <summary>
        /// When persisted settings carry a timezone string the host can't
        /// resolve (the literal "UTC-5" observed on a live install), Start()
        /// should heal it to TimeZoneInfo.Local.Id and persist the corrected
        /// value back to the DB so the next boot is clean.
        /// </summary>
        [Test]
        public void Start_WithUnresolvableTimezone_HealsToLocalAndPersists()
        {
            var stored = new GracefulRestartSettings
            {
                Enabled = false,
                ScheduledTime = "04:00",
                ScheduledTimezone = "UTC-5",      // not a valid IANA id, not a Windows id
            };
            _mockSettingsRepo
                .Setup(r => r.Get("GracefulRestart"))
                .Returns(JsonConvert.SerializeObject(stored));

            string persistedJson = null;
            _mockSettingsRepo
                .Setup(r => r.Set("GracefulRestart", It.IsAny<string>()))
                .Callback<string, string>((_, v) => persistedJson = v);

            var feature = new GracefulRestartFeature(_bus, _config, _mockSettingsRepo.Object);
            try
            {
                feature.Start();

                // Healed to whatever the runtime considers Local — verify by
                // round-tripping through FindSystemTimeZoneById, which is the
                // exact gate the schedule tick uses.
                Assert.That(feature.Settings.ScheduledTimezone, Is.Not.EqualTo("UTC-5"));
                Assert.DoesNotThrow(() =>
                    System.TimeZoneInfo.FindSystemTimeZoneById(feature.Settings.ScheduledTimezone));

                // The healed value should have been persisted exactly once so
                // operators don't see the warning again on the next boot.
                _mockSettingsRepo.Verify(r => r.Set("GracefulRestart", It.IsAny<string>()), Times.Once);
                Assert.That(persistedJson, Is.Not.Null);
                var roundTripped = JsonConvert.DeserializeObject<GracefulRestartSettings>(persistedJson);
                Assert.That(roundTripped.ScheduledTimezone, Is.EqualTo(feature.Settings.ScheduledTimezone));
            }
            finally
            {
                feature.Stop();
            }
        }

        /// <summary>
        /// When the persisted timezone already resolves, Start() must NOT
        /// rewrite the DB. We don't want every boot to churn the settings row.
        /// </summary>
        [Test]
        public void Start_WithResolvableTimezone_DoesNotPersist()
        {
            // Pick whatever Local is — guaranteed to round-trip on this host.
            var localId = System.TimeZoneInfo.Local?.Id ?? System.TimeZoneInfo.Utc.Id;

            var stored = new GracefulRestartSettings
            {
                Enabled = false,
                ScheduledTime = "04:00",
                ScheduledTimezone = localId,
            };
            _mockSettingsRepo
                .Setup(r => r.Get("GracefulRestart"))
                .Returns(JsonConvert.SerializeObject(stored));

            var feature = new GracefulRestartFeature(_bus, _config, _mockSettingsRepo.Object);
            try
            {
                feature.Start();

                Assert.That(feature.Settings.ScheduledTimezone, Is.EqualTo(localId));
                _mockSettingsRepo.Verify(
                    r => r.Set(It.IsAny<string>(), It.IsAny<string>()),
                    Times.Never);
            }
            finally
            {
                feature.Stop();
            }
        }
    }
}
