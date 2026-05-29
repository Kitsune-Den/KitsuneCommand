using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Data;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Tests.Repositories
{
    [TestFixture]
    public class PlayerMetadataRepositoryTests
    {
        private string _dbPath;
        private DbConnectionFactory _db;
        private PlayerMetadataRepository _repo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dbPath = TestDbFixture.CreateTempDatabase();
            _db = TestDbFixture.CreateFactory(_dbPath);
            _repo = new PlayerMetadataRepository(_db);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => TestDbFixture.Cleanup(_dbPath);

        [SetUp]
        public void SetUp()
        {
            using var conn = _db.CreateConnection();
            Dapper.SqlMapper.Execute(conn, "DELETE FROM player_metadata");
        }

        [Test]
        public void SetTier_CreatesRow_WhenPlayerHasNoMetadata()
        {
            _repo.SetTier("p1", "VIP");

            var meta = _repo.GetByPlayerId("p1");
            Assert.That(meta, Is.Not.Null);
            Assert.That(meta.VipTier, Is.EqualTo("VIP"));
        }

        [Test]
        public void SetTier_DoesNotClobberNameColorOrNotes()
        {
            _repo.Upsert(new PlayerMetadata { PlayerId = "p1", NameColor = "FF0000", CustomTag = "tag", Notes = "hi" });

            _repo.SetTier("p1", "VIP+");

            var meta = _repo.GetByPlayerId("p1");
            Assert.That(meta.VipTier, Is.EqualTo("VIP+"));
            Assert.That(meta.NameColor, Is.EqualTo("FF0000"), "name colour must survive a tier change");
            Assert.That(meta.CustomTag, Is.EqualTo("tag"));
            Assert.That(meta.Notes, Is.EqualTo("hi"));
        }

        [Test]
        public void Upsert_DoesNotClobberTier()
        {
            _repo.SetTier("p1", "VIP");

            // A subsequent metadata edit (e.g. setting a name colour) must not wipe the tier.
            _repo.Upsert(new PlayerMetadata { PlayerId = "p1", NameColor = "00FF00" });

            var meta = _repo.GetByPlayerId("p1");
            Assert.That(meta.VipTier, Is.EqualTo("VIP"), "tier must survive an Upsert");
            Assert.That(meta.NameColor, Is.EqualTo("00FF00"));
        }

        [Test]
        public void SetTier_ClearsTier_WhenGivenNullOrEmpty()
        {
            _repo.SetTier("p1", "VIP");
            _repo.SetTier("p1", "");

            var meta = _repo.GetByPlayerId("p1");
            Assert.That(meta.VipTier, Is.Null, "empty string normalizes to NULL (pleb)");
        }

        [Test]
        public void GetPlayerIdsByTier_ReturnsOnlyMatchingPlayers()
        {
            _repo.SetTier("a", "VIP");
            _repo.SetTier("b", "VIP");
            _repo.SetTier("c", "VIP+");
            _repo.SetTier("d", "");

            var vip = _repo.GetPlayerIdsByTier("VIP").OrderBy(x => x).ToList();
            Assert.That(vip, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(_repo.GetPlayerIdsByTier("VIP+"), Is.EquivalentTo(new[] { "c" }));
        }

        [Test]
        public void GetPlayerIdsByTier_ReturnsEmpty_ForNullOrEmptyTier()
        {
            _repo.SetTier("a", "VIP");
            Assert.That(_repo.GetPlayerIdsByTier(""), Is.Empty);
            Assert.That(_repo.GetPlayerIdsByTier(null), Is.Empty);
        }
    }
}
