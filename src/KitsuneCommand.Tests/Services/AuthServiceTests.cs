using System.Linq;
using NUnit.Framework;
using Moq;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;

namespace KitsuneCommand.Tests.Services
{
    [TestFixture]
    public class AuthServiceTests
    {
        private Mock<IUserAccountRepository> _mockRepo;
        private AuthService _authService;

        [SetUp]
        public void SetUp()
        {
            _mockRepo = new Mock<IUserAccountRepository>();
            _authService = new AuthService(_mockRepo.Object);
        }

        [Test]
        public void ValidateCredentials_ReturnsAccount_WhenValid()
        {
            var hash = PasswordHasher.Hash("correct-password");
            var account = new UserAccount
            {
                Id = 1,
                Username = "admin",
                PasswordHash = hash,
                DisplayName = "Admin",
                Role = "admin",
                IsActive = true
            };

            _mockRepo.Setup(r => r.GetByUsername("admin")).Returns(account);

            var result = _authService.ValidateCredentials("admin", "correct-password");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Username, Is.EqualTo("admin"));
            _mockRepo.Verify(r => r.UpdateLastLogin("admin"), Times.Once);
        }

        [Test]
        public void ValidateCredentials_ReturnsNull_WhenWrongPassword()
        {
            var hash = PasswordHasher.Hash("correct-password");
            var account = new UserAccount
            {
                Id = 1,
                Username = "admin",
                PasswordHash = hash,
                Role = "admin"
            };

            _mockRepo.Setup(r => r.GetByUsername("admin")).Returns(account);

            var result = _authService.ValidateCredentials("admin", "wrong-password");

            Assert.That(result, Is.Null);
            _mockRepo.Verify(r => r.UpdateLastLogin(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void ValidateCredentials_ReturnsNull_WhenUserNotFound()
        {
            _mockRepo.Setup(r => r.GetByUsername("nonexistent")).Returns((UserAccount)null);

            var result = _authService.ValidateCredentials("nonexistent", "any-password");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ChangePassword_ReturnsTrue_WhenCurrentPasswordIsCorrect()
        {
            var hash = PasswordHasher.Hash("old-password");
            var account = new UserAccount
            {
                Id = 1,
                Username = "admin",
                PasswordHash = hash,
                DisplayName = "Admin",
                Role = "admin",
                IsActive = true
            };

            _mockRepo.Setup(r => r.GetByUsername("admin")).Returns(account);

            var result = _authService.ChangePassword("admin", "old-password", "new-password");

            Assert.That(result, Is.True);
            // AuthService routes through UpdatePassword(id, hash) rather
            // than Update(account) — see the long-form comment on
            // AuthService.ChangePassword for the why. Update() only
            // persists display_name / role / is_active and silently
            // drops password_hash changes; this test caught the bug
            // *after* the fix landed (which is why it was failing
            // against the new — correct — implementation).
            _mockRepo.Verify(
                r => r.UpdatePassword(
                    account.Id,
                    It.Is<string>(h => PasswordHasher.Verify("new-password", h))),
                Times.Once);
        }

        [Test]
        public void ChangePassword_ReturnsFalse_WhenCurrentPasswordIsWrong()
        {
            var hash = PasswordHasher.Hash("correct-password");
            var account = new UserAccount
            {
                Id = 1,
                Username = "admin",
                PasswordHash = hash,
                Role = "admin"
            };

            _mockRepo.Setup(r => r.GetByUsername("admin")).Returns(account);

            var result = _authService.ChangePassword("admin", "wrong-password", "new-password");

            Assert.That(result, Is.False);
            _mockRepo.Verify(r => r.Update(It.IsAny<UserAccount>()), Times.Never);
        }

        [Test]
        [Ignore("Log class requires game runtime (netstandard 2.1) — JIT resolves Log.Out even in early-return path")]
        public void EnsureAdminExists_DoesNothing_WhenAccountsExist()
        {
            _mockRepo.Setup(r => r.Count()).Returns(1);

            _authService.EnsureAdminExists();

            _mockRepo.Verify(r => r.Create(It.IsAny<UserAccount>()), Times.Never);
        }

        [Test]
        [Ignore("Requires game runtime (Log.Out + GameIO.GetSaveGameDir) — integration test only")]
        public void EnsureAdminExists_CreatesAdmin_WhenNoAccountsExist()
        {
            _mockRepo.Setup(r => r.Count()).Returns(0);

            _authService.EnsureAdminExists();

            _mockRepo.Verify(r => r.Create(It.Is<UserAccount>(a =>
                a.Username == "admin" &&
                a.Role == "admin" &&
                a.IsActive == true &&
                !string.IsNullOrEmpty(a.PasswordHash)
            )), Times.Once);
        }
    }

    /// <summary>
    /// Standalone tests for the PasswordHasher utility.
    /// </summary>
    [TestFixture]
    public class PasswordHasherTests
    {
        [Test]
        public void Hash_ReturnsNonEmptyString()
        {
            var hash = PasswordHasher.Hash("test-password");
            Assert.That(hash, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Hash_DifferentInputs_ProduceDifferentHashes()
        {
            var hash1 = PasswordHasher.Hash("password1");
            var hash2 = PasswordHasher.Hash("password2");
            Assert.That(hash1, Is.Not.EqualTo(hash2));
        }

        [Test]
        public void Hash_SameInput_ProducesDifferentHashes_DueToBCryptSalt()
        {
            var hash1 = PasswordHasher.Hash("same-password");
            var hash2 = PasswordHasher.Hash("same-password");
            Assert.That(hash1, Is.Not.EqualTo(hash2), "BCrypt salts should differ");
        }

        [Test]
        public void Verify_ReturnsTrue_WhenPasswordMatchesHash()
        {
            var hash = PasswordHasher.Hash("correct");
            Assert.That(PasswordHasher.Verify("correct", hash), Is.True);
        }

        [Test]
        public void Verify_ReturnsFalse_WhenPasswordDoesNotMatch()
        {
            var hash = PasswordHasher.Hash("correct");
            Assert.That(PasswordHasher.Verify("wrong", hash), Is.False);
        }

        [Test]
        public void Verify_ReturnsFalse_WhenHashIsInvalid()
        {
            Assert.That(PasswordHasher.Verify("anything", "not-a-valid-hash"), Is.False);
        }
    }
}
