using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Features;

namespace KitsuneCommand.Tests.Features
{
    [TestFixture]
    public class SafeZoneFeatureTests
    {
        private static SafeZoneSettings OneZone(SafeZone zone) =>
            new SafeZoneSettings { Zones = new List<SafeZone> { zone } };

        private static SafeZone Square(int min, int max) =>
            new SafeZone { Id = "z", Name = "Base", MinX = min, MinZ = min, MaxX = max, MaxZ = max };

        [Test]
        public void Normalize_HandlesNullAndClampsInterval()
        {
            var s = SafeZoneFeature.Normalize(new SafeZoneSettings { Zones = null, SweepIntervalSeconds = 0 });
            Assert.That(s.Zones, Is.Empty);
            Assert.That(s.SweepIntervalSeconds, Is.EqualTo(1));

            s = SafeZoneFeature.Normalize(new SafeZoneSettings { SweepIntervalSeconds = 999 });
            Assert.That(s.SweepIntervalSeconds, Is.EqualTo(30));

            Assert.That(SafeZoneFeature.Normalize(null), Is.Not.Null);
        }

        [Test]
        public void Normalize_OrdersCornersAndClampsCoordinates()
        {
            var s = SafeZoneFeature.Normalize(OneZone(new SafeZone
            {
                Id = "a", MinX = 50, MaxX = -50, MinZ = 999999, MaxZ = 10,
            }));

            var z = s.Zones.Single();
            Assert.That((z.MinX, z.MaxX), Is.EqualTo((-50, 50)));
            Assert.That((z.MinZ, z.MaxZ), Is.EqualTo((10, SafeZoneFeature.MaxCoordinate)));
        }

        [Test]
        public void Normalize_FixesIdsAndNames()
        {
            var s = SafeZoneFeature.Normalize(new SafeZoneSettings
            {
                Zones = new List<SafeZone>
                {
                    new SafeZone { Id = "same", Name = "  Home  " },
                    new SafeZone { Id = "SAME", Name = "" },
                    new SafeZone { Id = null, Name = new string('x', 200) },
                    null,
                },
            });

            Assert.That(s.Zones, Has.Count.EqualTo(3), "null entries are dropped");
            Assert.That(s.Zones[0].Id, Is.EqualTo("same"));
            Assert.That(s.Zones[0].Name, Is.EqualTo("Home"));
            Assert.That(s.Zones[1].Id, Is.Not.EqualTo("SAME").IgnoreCase, "a duplicate id gets a fresh one");
            Assert.That(s.Zones[1].Name, Is.EqualTo("Safe zone"));
            Assert.That(s.Zones[2].Id, Is.Not.Empty);
            Assert.That(s.Zones[2].Name, Has.Length.EqualTo(SafeZoneFeature.MaxNameLength));
            Assert.That(s.Zones.Select(z => z.Id.ToLowerInvariant()).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void Normalize_CapsZoneCount()
        {
            var zones = Enumerable.Range(0, SafeZoneFeature.MaxZones + 5).Select(i => new SafeZone { Id = "z" + i }).ToList();
            var s = SafeZoneFeature.Normalize(new SafeZoneSettings { Zones = zones });
            Assert.That(s.Zones, Has.Count.EqualTo(SafeZoneFeature.MaxZones));
        }

        [TestCase(0f, 0f, true)]
        [TestCase(10.9f, 10.9f, true)]   // inside the far edge block
        [TestCase(11f, 5f, false)]       // just past it
        [TestCase(-0.1f, 5f, false)]
        public void Contains_IsInclusiveOfEdgeBlocks(float x, float z, bool expected)
        {
            Assert.That(Square(0, 10).Contains(x, z), Is.EqualTo(expected));
        }

        [Test]
        public void Enemies_InsideAnEnabledZone_AreRemoved()
        {
            var settings = OneZone(Square(-10, 10));
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(settings, SafeZoneTarget.Enemy, false, 0, 0)?.Id, Is.EqualTo("z"));
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(settings, SafeZoneTarget.Enemy, false, 50, 0), Is.Null);
        }

        [Test]
        public void NothingIsRemoved_WhenTheZoneOrFeatureIsOff()
        {
            var zone = Square(-10, 10);
            zone.Enabled = false;
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(OneZone(zone), SafeZoneTarget.Enemy, false, 0, 0), Is.Null);

            var off = OneZone(Square(-10, 10));
            off.Enabled = false;
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(off, SafeZoneTarget.Enemy, false, 0, 0), Is.Null);
        }

        [Test]
        public void PlayersTradersAndOtherEntities_AreNeverRemoved()
        {
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(OneZone(Square(-10, 10)), SafeZoneTarget.None, false, 0, 0), Is.Null);
        }

        [Test]
        public void Animals_AreRemovedOnlyWhenTheZoneAsks()
        {
            var zone = Square(-10, 10);
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(OneZone(zone), SafeZoneTarget.Animal, false, 0, 0), Is.Null);

            zone.RemoveAnimals = true;
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(OneZone(zone), SafeZoneTarget.Animal, false, 0, 0), Is.Not.Null);
        }

        [Test]
        public void BloodMoonHorde_IsRemovedUnlessTheZoneLetsItIn()
        {
            var zone = Square(-10, 10);
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(OneZone(zone), SafeZoneTarget.Enemy, true, 0, 0), Is.Not.Null);

            zone.AllowBloodMoon = true;
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(OneZone(zone), SafeZoneTarget.Enemy, true, 0, 0), Is.Null);
            Assert.That(SafeZoneFeature.ZoneToRemoveFrom(OneZone(zone), SafeZoneTarget.Enemy, false, 0, 0), Is.Not.Null,
                "ordinary zombies still go during a blood moon");
        }
    }
}
