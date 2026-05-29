using NUnit.Framework;
using KitsuneCommand.Data;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Tests.Repositories
{
    [TestFixture]
    public class VipGiftRepositoryTests
    {
        private string _dbPath;
        private DbConnectionFactory _db;
        private VipGiftRepository _repo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dbPath = TestDbFixture.CreateTempDatabase();
            _db = TestDbFixture.CreateFactory(_dbPath);
            _repo = new VipGiftRepository(_db);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => TestDbFixture.Cleanup(_dbPath);

        [SetUp]
        public void SetUp()
        {
            using var conn = _db.CreateConnection();
            Dapper.SqlMapper.Execute(conn, "DELETE FROM vip_gifts");
        }

        [Test]
        public void HasGiftByNameAndPeriod_MatchesOnAllThreeKeys()
        {
            _repo.Insert(new VipGift { PlayerId = "p1", PlayerName = "One", Name = "Weekly Crate", ClaimPeriod = "weekly" });

            Assert.That(_repo.HasGiftByNameAndPeriod("p1", "Weekly Crate", "weekly"), Is.True);
            Assert.That(_repo.HasGiftByNameAndPeriod("p1", "Weekly Crate", "daily"), Is.False, "different period");
            Assert.That(_repo.HasGiftByNameAndPeriod("p1", "Other", "weekly"), Is.False, "different name");
            Assert.That(_repo.HasGiftByNameAndPeriod("p2", "Weekly Crate", "weekly"), Is.False, "different player");
        }
    }
}
