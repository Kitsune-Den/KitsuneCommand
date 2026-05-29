using NUnit.Framework;
using KitsuneCommand.Data;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Tests.Repositories
{
    [TestFixture]
    public class FirstLoginGrantRepositoryTests
    {
        private string _dbPath;
        private DbConnectionFactory _db;
        private FirstLoginGrantRepository _repo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dbPath = TestDbFixture.CreateTempDatabase();
            _db = TestDbFixture.CreateFactory(_dbPath);
            _repo = new FirstLoginGrantRepository(_db);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => TestDbFixture.Cleanup(_dbPath);

        [SetUp]
        public void SetUp()
        {
            using var conn = _db.CreateConnection();
            Dapper.SqlMapper.Execute(conn, "DELETE FROM first_login_grants");
        }

        [Test]
        public void TryClaim_ReturnsTrue_OnFirstCall_ThenFalse()
        {
            Assert.That(_repo.TryClaim("p1", "Player One"), Is.True, "first claim should succeed");
            Assert.That(_repo.TryClaim("p1", "Player One"), Is.False, "second claim must be a no-op");
        }

        [Test]
        public void HasGrant_ReflectsClaimState()
        {
            Assert.That(_repo.HasGrant("p1"), Is.False);
            _repo.TryClaim("p1", "Player One");
            Assert.That(_repo.HasGrant("p1"), Is.True);
        }

        [Test]
        public void TryClaim_IsPerPlayer()
        {
            Assert.That(_repo.TryClaim("p1", "One"), Is.True);
            Assert.That(_repo.TryClaim("p2", "Two"), Is.True, "a different player still gets their first pack");
        }
    }
}
