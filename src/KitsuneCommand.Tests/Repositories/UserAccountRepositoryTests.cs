using System.IO;
using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Data;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Tests.Repositories
{
    [TestFixture]
    public class UserAccountRepositoryTests
    {
        private string _dbPath;
        private DbConnectionFactory _db;
        private UserAccountRepository _repo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dbPath = TestDbFixture.CreateTempDatabase();
            _db = TestDbFixture.CreateFactory(_dbPath);
            _repo = new UserAccountRepository(_db);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            TestDbFixture.Cleanup(_dbPath);
        }

        [SetUp]
        public void SetUp()
        {
            using var conn = _db.CreateConnection();
            Dapper.SqlMapper.Execute(conn, "DELETE FROM user_accounts");
        }

        [Test]
        public void Create_ReturnsNewId()
        {
            var account = new UserAccount
            {
                Username = "testuser",
                PasswordHash = "hash123",
                DisplayName = "Test User",
                Role = "admin"
            };

            var id = _repo.Create(account);

            Assert.That(id, Is.GreaterThan(0));
        }

        [Test]
        public void GetByUsername_ReturnsAccount_WhenExists()
        {
            var account = new UserAccount
            {
                Username = "admin",
                PasswordHash = "hash123",
                DisplayName = "Admin",
                Role = "admin",
                IsActive = true
            };
            _repo.Create(account);

            var result = _repo.GetByUsername("admin");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Username, Is.EqualTo("admin"));
            Assert.That(result.DisplayName, Is.EqualTo("Admin"));
            Assert.That(result.Role, Is.EqualTo("admin"));
        }

        [Test]
        public void GetByUsername_ReturnsNull_WhenNotFound()
        {
            var result = _repo.GetByUsername("nonexistent");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetByUsername_ReturnsNull_WhenInactive()
        {
            var account = new UserAccount
            {
                Username = "inactive",
                PasswordHash = "hash123",
                DisplayName = "Inactive",
                Role = "admin",
                IsActive = false
            };
            _repo.Create(account);

            // Deactivate
            var created = _repo.GetById(_repo.Create(new UserAccount
            {
                Username = "inactive2",
                PasswordHash = "hash",
                DisplayName = "Test",
                Role = "admin",
                IsActive = true
            }));

            // Use direct SQL to set inactive since Update only works with fetched entities
            using var conn = _db.CreateConnection();
            Dapper.SqlMapper.Execute(conn,
                "UPDATE user_accounts SET is_active = 0 WHERE username = 'inactive'");

            var result = _repo.GetByUsername("inactive");
            Assert.That(result, Is.Null, "Inactive accounts should not be returned");
        }

        [Test]
        public void GetById_ReturnsCorrectAccount()
        {
            var id = _repo.Create(new UserAccount
            {
                Username = "byid",
                PasswordHash = "hash",
                DisplayName = "ById User",
                Role = "moderator"
            });

            var result = _repo.GetById(id);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo(id));
            Assert.That(result.Username, Is.EqualTo("byid"));
        }

        [Test]
        public void GetAll_ReturnsAllAccounts()
        {
            _repo.Create(new UserAccount { Username = "u1", PasswordHash = "h", DisplayName = "U1", Role = "admin" });
            _repo.Create(new UserAccount { Username = "u2", PasswordHash = "h", DisplayName = "U2", Role = "admin" });

            var all = _repo.GetAll().ToList();

            Assert.That(all, Has.Count.EqualTo(2));
        }

        [Test]
        public void Update_ChangesDisplayNameAndRole()
        {
            var id = _repo.Create(new UserAccount
            {
                Username = "update",
                PasswordHash = "hash",
                DisplayName = "Old Name",
                Role = "admin"
            });

            var account = _repo.GetById(id);
            account.DisplayName = "New Name";
            account.Role = "moderator";
            _repo.Update(account);

            var updated = _repo.GetById(id);
            Assert.That(updated.DisplayName, Is.EqualTo("New Name"));
            Assert.That(updated.Role, Is.EqualTo("moderator"));
        }

        [Test]
        public void UpdatePassword_ChangesHash()
        {
            var id = _repo.Create(new UserAccount
            {
                Username = "pwchange",
                PasswordHash = "oldhash",
                DisplayName = "Test",
                Role = "admin"
            });

            _repo.UpdatePassword(id, "newhash");

            var result = _repo.GetById(id);
            Assert.That(result.PasswordHash, Is.EqualTo("newhash"));
        }

        [Test]
        public void UpdateLastLogin_SetsTimestamp()
        {
            _repo.Create(new UserAccount
            {
                Username = "logintest",
                PasswordHash = "hash",
                DisplayName = "Test",
                Role = "admin"
            });

            _repo.UpdateLastLogin("logintest");

            var result = _repo.GetByUsername("logintest");
            Assert.That(result.LastLoginAt, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Count_ReturnsCorrectNumber()
        {
            Assert.That(_repo.Count(), Is.EqualTo(0));

            _repo.Create(new UserAccount { Username = "a", PasswordHash = "h", DisplayName = "A", Role = "admin" });
            _repo.Create(new UserAccount { Username = "b", PasswordHash = "h", DisplayName = "B", Role = "admin" });

            Assert.That(_repo.Count(), Is.EqualTo(2));
        }

        [Test]
        public void CountActiveAdmins_OnlyCountsActiveAdmins()
        {
            _repo.Create(new UserAccount { Username = "admin1", PasswordHash = "h", DisplayName = "A", Role = "admin", IsActive = true });
            _repo.Create(new UserAccount { Username = "mod1", PasswordHash = "h", DisplayName = "M", Role = "moderator", IsActive = true });

            // Deactivate one admin
            using var conn = _db.CreateConnection();
            Dapper.SqlMapper.Execute(conn,
                "INSERT INTO user_accounts (username, password_hash, display_name, role, is_active) VALUES ('admin2', 'h', 'A2', 'admin', 0)");

            var count = _repo.CountActiveAdmins();
            Assert.That(count, Is.EqualTo(1), "Should only count active admins");
        }

        [Test]
        public void SetSteamId_LinksAndGetBySteamIdFindsIt()
        {
            var id = _repo.Create(new UserAccount { Username = "mod1", PasswordHash = "h", Role = "moderator" });

            _repo.SetSteamId(id, "76561197960287930");

            var result = _repo.GetBySteamId("76561197960287930");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo(id));
            Assert.That(result.SteamId, Is.EqualTo("76561197960287930"));
        }

        [Test]
        public void SetSteamId_Null_Unlinks()
        {
            var id = _repo.Create(new UserAccount { Username = "mod1", PasswordHash = "h", Role = "moderator" });
            _repo.SetSteamId(id, "76561197960287930");

            _repo.SetSteamId(id, null);

            Assert.That(_repo.GetBySteamId("76561197960287930"), Is.Null);
            Assert.That(_repo.GetById(id).SteamId, Is.Null);
        }

        [Test]
        public void GetBySteamId_IgnoresInactiveAccounts()
        {
            var id = _repo.Create(new UserAccount { Username = "gone", PasswordHash = "h", Role = "viewer" });
            _repo.SetSteamId(id, "76561197960287930");
            _repo.Update(new UserAccount { Id = id, DisplayName = "gone", Role = "viewer", IsActive = false });

            Assert.That(_repo.GetBySteamId("76561197960287930"), Is.Null);
        }

        [Test]
        public void SetSteamId_SameSteamIdOnTwoAccounts_Throws()
        {
            var a = _repo.Create(new UserAccount { Username = "a", PasswordHash = "h", Role = "admin" });
            var b = _repo.Create(new UserAccount { Username = "b", PasswordHash = "h", Role = "admin" });
            _repo.SetSteamId(a, "76561197960287930");

            Assert.That(() => _repo.SetSteamId(b, "76561197960287930"), Throws.Exception);
        }

        [Test]
        public void UnlinkedAccounts_DoNotCollideOnTheUniqueIndex()
        {
            _repo.Create(new UserAccount { Username = "a", PasswordHash = "h", Role = "admin" });
            _repo.Create(new UserAccount { Username = "b", PasswordHash = "h", Role = "admin" });

            Assert.That(_repo.GetAll().Count(u => u.SteamId == null), Is.EqualTo(2));
        }
    }
}
