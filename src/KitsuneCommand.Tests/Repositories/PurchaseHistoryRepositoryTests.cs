using System;
using System.Globalization;
using NUnit.Framework;
using KitsuneCommand.Data;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;

namespace KitsuneCommand.Tests.Repositories
{
    [TestFixture]
    public class PurchaseHistoryRepositoryTests
    {
        private string _dbPath;
        private DbConnectionFactory _db;
        private PurchaseHistoryRepository _repo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dbPath = TestDbFixture.CreateTempDatabase();
            _db = TestDbFixture.CreateFactory(_dbPath);
            _repo = new PurchaseHistoryRepository(_db);
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
            Dapper.SqlMapper.Execute(conn, "DELETE FROM purchase_history");
        }

        /// <summary>Inserts a purchase with a chosen created_at, which Insert() always sets to now.</summary>
        private void InsertAt(string playerId, DateTime createdAtUtc)
        {
            using var conn = _db.CreateConnection();
            Dapper.SqlMapper.Execute(conn, @"
                INSERT INTO purchase_history (created_at, player_id, player_name, goods_id, goods_name, price)
                VALUES (@CreatedAt, @PlayerId, 'Tester', 1, 'Starter Kit', 100)",
                new { CreatedAt = createdAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), PlayerId = playerId });
        }

        [Test]
        public void GetLastPurchaseUtc_ReturnsNull_WhenPlayerHasNoPurchases()
        {
            InsertAt("someone-else", DateTime.UtcNow);
            Assert.That(_repo.GetLastPurchaseUtc("player1"), Is.Null);
        }

        [Test]
        public void GetLastPurchaseUtc_ReturnsMostRecent_AsUtc()
        {
            InsertAt("player1", new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc));
            InsertAt("player1", new DateTime(2026, 11, 4, 14, 5, 30, DateTimeKind.Utc));
            InsertAt("player1", new DateTime(2026, 11, 2, 23, 0, 0, DateTimeKind.Utc));

            var last = _repo.GetLastPurchaseUtc("player1");

            Assert.That(last, Is.EqualTo(new DateTime(2026, 11, 4, 14, 5, 30, DateTimeKind.Utc)));
            Assert.That(last.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void GetLastPurchaseUtc_ReadsRowsWrittenByInsert()
        {
            var before = DateTime.UtcNow.AddSeconds(-2);
            _repo.Insert(new PurchaseRecord { PlayerId = "player1", PlayerName = "Tester", GoodsId = 1, GoodsName = "Kit", Price = 10 });

            var last = _repo.GetLastPurchaseUtc("player1");

            Assert.That(last, Is.Not.Null);
            Assert.That(last.Value, Is.GreaterThanOrEqualTo(before).And.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(2)));
        }

        [Test]
        public void CountSince_CountsOnlyThatPlayersPurchasesFromTheCutoff()
        {
            var midnight = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
            InsertAt("player1", midnight.AddSeconds(-1));   // yesterday
            InsertAt("player1", midnight);                  // exactly at the cutoff
            InsertAt("player1", midnight.AddHours(15));
            InsertAt("player2", midnight.AddHours(1));

            Assert.That(_repo.CountSince("player1", midnight), Is.EqualTo(2));
            Assert.That(_repo.CountSince("player2", midnight), Is.EqualTo(1));
            Assert.That(_repo.CountSince("player3", midnight), Is.EqualTo(0));
        }

        [Test]
        public void LimitRefusal_ReadsHistory_ForDailyCap()
        {
            var now = new DateTime(2026, 11, 4, 15, 30, 0, DateTimeKind.Utc);
            InsertAt("player1", now.Date.AddMinutes(-5));   // yesterday, doesn't count
            InsertAt("player1", now.AddHours(-3));
            InsertAt("player1", now.AddHours(-2));
            var settings = new StoreSettings { MaxDailyPurchases = 2 };

            Assert.That(StorePurchasePolicy.LimitRefusal(settings, _repo, "player1", now), Does.StartWith("Daily purchase limit reached"));
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, _repo, "player2", now), Is.Null);
        }

        [Test]
        public void LimitRefusal_ReadsHistory_ForCooldown()
        {
            var now = new DateTime(2026, 11, 4, 15, 30, 0, DateTimeKind.Utc);
            InsertAt("player1", now.AddSeconds(-20));
            var settings = new StoreSettings { PurchaseCooldownSeconds = 30 };

            Assert.That(StorePurchasePolicy.LimitRefusal(settings, _repo, "player1", now), Does.EndWith("in 10s."));
            Assert.That(StorePurchasePolicy.LimitRefusal(settings, _repo, "player1", now.AddSeconds(10)), Is.Null);
        }

        [Test]
        public void LoadSettings_ReadsSavedStoreSettings_AndFallsBackOnBadJson()
        {
            var settingsRepo = new SettingsRepository(_db);

            settingsRepo.Set(StorePurchasePolicy.SettingsKey, "{\"PurchaseCooldownSeconds\":45,\"MaxDailyPurchases\":4,\"PriceMultiplier\":1.5}");
            var loaded = StorePurchasePolicy.LoadSettings(settingsRepo);
            Assert.That(loaded.PurchaseCooldownSeconds, Is.EqualTo(45));
            Assert.That(loaded.MaxDailyPurchases, Is.EqualTo(4));
            Assert.That(loaded.PriceMultiplier, Is.EqualTo(1.5));

            settingsRepo.Set(StorePurchasePolicy.SettingsKey, "not json");
            var fallback = StorePurchasePolicy.LoadSettings(settingsRepo);
            Assert.That(fallback.PurchaseCooldownSeconds, Is.EqualTo(0));
            Assert.That(fallback.MaxDailyPurchases, Is.EqualTo(0));
            Assert.That(fallback.PriceMultiplier, Is.EqualTo(1.0));
        }
    }
}
