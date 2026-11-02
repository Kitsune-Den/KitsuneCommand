using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Data;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Tests.Repositories
{
    [TestFixture]
    public class SuspicionEventRepositoryTests
    {
        private string _dbPath;
        private DbConnectionFactory _db;
        private SuspicionEventRepository _repo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dbPath = TestDbFixture.CreateTempDatabase();
            _db = TestDbFixture.CreateFactory(_dbPath);
            _repo = new SuspicionEventRepository(_db);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() => TestDbFixture.Cleanup(_dbPath);

        [SetUp]
        public void SetUp()
        {
            using var conn = _db.CreateConnection();
            Dapper.SqlMapper.Execute(conn, "DELETE FROM suspicion_events");
        }

        private int Add(string player, string detector = "inventory", string severity = "high", string rule = "over_stack")
        {
            return _repo.Insert(new SuspicionEvent
            {
                PlayerId = "Steam_" + player,
                PlayerName = player,
                EntityId = 171,
                Detector = detector,
                Rule = rule,
                Severity = severity,
                Summary = "test",
                Details = "{\"item\":\"resourceWood\"}",
                PosX = 10.5,
                PosY = 60,
                PosZ = -20.25,
            });
        }

        [Test]
        public void Insert_ReturnsId_AndRoundTripsEveryColumn()
        {
            var id = Add("Ada");

            var row = _repo.GetById(id);
            Assert.That(id, Is.GreaterThan(0));
            Assert.That(row.PlayerId, Is.EqualTo("Steam_Ada"));
            Assert.That(row.PlayerName, Is.EqualTo("Ada"));
            Assert.That(row.EntityId, Is.EqualTo(171));
            Assert.That(row.Detector, Is.EqualTo("inventory"));
            Assert.That(row.Rule, Is.EqualTo("over_stack"));
            Assert.That(row.Severity, Is.EqualTo("high"));
            Assert.That(row.Details, Is.EqualTo("{\"item\":\"resourceWood\"}"));
            Assert.That(row.PosX, Is.EqualTo(10.5));
            Assert.That(row.PosZ, Is.EqualTo(-20.25));
            Assert.That(row.Reviewed, Is.False);
            Assert.That(row.CreatedAt, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void GetPage_IsNewestFirst_AndPages()
        {
            var first = Add("A");
            var second = Add("B");
            var third = Add("C");

            var page0 = _repo.GetPage(0, 2).Select(e => e.Id).ToList();
            var page1 = _repo.GetPage(1, 2).Select(e => e.Id).ToList();

            Assert.That(page0, Is.EqualTo(new[] { third, second }));
            Assert.That(page1, Is.EqualTo(new[] { first }));
        }

        [Test]
        public void Filters_ByDetectorSeverityReviewedAndSearch()
        {
            var a = Add("Ada", "inventory", "high");
            Add("Bob", "movement", "medium", "speed");
            Add("Cy", "killrate", "medium", "kill_rate");
            _repo.SetReviewed(new[] { a }, true, "admin");

            Assert.That(_repo.GetCount(new SuspicionEventFilter { Detector = "movement" }), Is.EqualTo(1));
            Assert.That(_repo.GetCount(new SuspicionEventFilter { Severity = "medium" }), Is.EqualTo(2));
            Assert.That(_repo.GetCount(new SuspicionEventFilter { Reviewed = false }), Is.EqualTo(2));
            Assert.That(_repo.GetCount(new SuspicionEventFilter { Reviewed = true }), Is.EqualTo(1));
            Assert.That(_repo.GetPage(0, 10, new SuspicionEventFilter { Search = "bo" }).Single().PlayerName, Is.EqualTo("Bob"));
            Assert.That(_repo.GetCount(new SuspicionEventFilter { Search = "Steam_Cy" }), Is.EqualTo(1), "search matches player id too");
            Assert.That(_repo.GetCount(new SuspicionEventFilter { Detector = "movement", Severity = "high" }), Is.EqualTo(0));
        }

        [Test]
        public void SetReviewed_RecordsWhoAndNote_AndUnreviewClearsThem()
        {
            var id = Add("Ada");

            var updated = _repo.SetReviewed(new[] { id, id }, true, "mod1", "checked: legit trade");
            var row = _repo.GetById(id);
            Assert.That(updated, Is.EqualTo(1));
            Assert.That(row.Reviewed, Is.True);
            Assert.That(row.ReviewedBy, Is.EqualTo("mod1"));
            Assert.That(row.ReviewNote, Is.EqualTo("checked: legit trade"));
            Assert.That(row.ReviewedAt, Is.Not.Null);

            _repo.SetReviewed(new[] { id }, false, "mod1");
            row = _repo.GetById(id);
            Assert.That(row.Reviewed, Is.False);
            Assert.That(row.ReviewedBy, Is.Null);
            Assert.That(row.ReviewNote, Is.Null);
            Assert.That(row.ReviewedAt, Is.Null);
        }

        [Test]
        public void SetReviewed_WithNoIds_DoesNothing()
        {
            Add("Ada");
            Assert.That(_repo.SetReviewed(new int[0], true, "admin"), Is.EqualTo(0));
            Assert.That(_repo.SetReviewed(null, true, "admin"), Is.EqualTo(0));
        }

        [Test]
        public void DeleteOlderThan_RemovesOnlyOldRows_AndZeroKeepsEverything()
        {
            var old = Add("Old");
            var fresh = Add("Fresh");
            using (var conn = _db.CreateConnection())
                Dapper.SqlMapper.Execute(conn,
                    "UPDATE suspicion_events SET created_at = datetime('now', '-40 days') WHERE id = @Id", new { Id = old });

            Assert.That(_repo.DeleteOlderThan(0), Is.EqualTo(0));
            Assert.That(_repo.DeleteOlderThan(30), Is.EqualTo(1));
            Assert.That(_repo.GetById(old), Is.Null);
            Assert.That(_repo.GetById(fresh), Is.Not.Null);
        }
    }
}
