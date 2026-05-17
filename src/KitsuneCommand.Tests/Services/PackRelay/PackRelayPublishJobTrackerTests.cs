// Job-tracker tests. Focus on the contract the controller depends on:
//   - AllocateJob serializes — only one running job at a time
//   - Snapshots are defensive copies (mutating a returned snapshot
//     doesn't affect the in-flight state)
//   - MarkDone / MarkError release the active-job slot
//   - Get returns null for unknown ids (frontend polls expired jobs)

using System;
using KitsuneCommand.Services.PackRelay;
using NUnit.Framework;

namespace KitsuneCommand.Tests.Services.PackRelay
{
    [TestFixture]
    public class PackRelayPublishJobTrackerTests
    {
        private PackRelayPublishJobTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _tracker = new PackRelayPublishJobTracker();
        }

        // ---- Allocate ----

        [Test]
        public void AllocateJob_FirstCall_Succeeds()
        {
            var id = _tracker.AllocateJob(modpackId: 1);
            Assert.That(id, Is.Not.Null.And.Not.Empty);
            var snap = _tracker.Get(id);
            Assert.That(snap, Is.Not.Null);
            Assert.That(snap.ModpackId, Is.EqualTo(1));
            Assert.That(snap.Status, Is.EqualTo(PublishJobStatus.Running));
        }

        [Test]
        public void AllocateJob_WhileRunning_Throws()
        {
            _tracker.AllocateJob(1);
            Assert.Throws<InvalidOperationException>(() => _tracker.AllocateJob(2),
                "Second concurrent publish must be rejected.");
        }

        [Test]
        public void AllocateJob_AfterPriorDone_Succeeds()
        {
            var first = _tracker.AllocateJob(1);
            _tracker.MarkDone(first, new PublishResult { Slug = "x", Version = "1.0.0" });
            // Slot released; a second publish should be allowed.
            var second = _tracker.AllocateJob(2);
            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void AllocateJob_AfterPriorError_Succeeds()
        {
            // Same release semantics as Done — error frees the slot
            // so the user can retry without restarting the panel.
            var first = _tracker.AllocateJob(1);
            _tracker.MarkError(first, "401 token bad", "auth");
            var second = _tracker.AllocateJob(2);
            Assert.That(second, Is.Not.Null.And.Not.EqualTo(first));
        }

        // ---- Snapshots ----

        [Test]
        public void Get_ReturnsDefensiveCopy()
        {
            var id = _tracker.AllocateJob(1);
            _tracker.ReportProgress(id, new PublishProgress
            {
                Phase = PublishPhase.Hashing,
                FilesDone = 5,
                FilesTotal = 100,
            });
            var a = _tracker.Get(id);
            var b = _tracker.Get(id);
            // Two reads return separate Snapshot objects so a caller
            // can't accidentally mutate live state by holding the
            // first reference.
            Assert.That(ReferenceEquals(a, b), Is.False);
        }

        [Test]
        public void Get_UnknownJob_ReturnsNull()
        {
            Assert.That(_tracker.Get("no-such-job"), Is.Null);
            Assert.That(_tracker.Get(""), Is.Null);
        }

        // ---- ReportProgress ----

        [Test]
        public void ReportProgress_UpdatesLatest_AndBumpsTimestamp()
        {
            var id = _tracker.AllocateJob(1);
            var before = _tracker.Get(id).UpdatedAtUtc;
            System.Threading.Thread.Sleep(20);

            _tracker.ReportProgress(id, new PublishProgress
            {
                Phase = PublishPhase.Uploading,
                FilesDone = 3,
                FilesTotal = 10,
                CurrentFile = "Mods/Test/file.xml",
            });

            var after = _tracker.Get(id);
            Assert.That(after.LatestProgress, Is.Not.Null);
            Assert.That(after.LatestProgress.Phase, Is.EqualTo(PublishPhase.Uploading));
            Assert.That(after.LatestProgress.FilesDone, Is.EqualTo(3));
            Assert.That(after.UpdatedAtUtc, Is.GreaterThan(before));
        }

        [Test]
        public void ReportProgress_UnknownJob_NoThrow()
        {
            // Worker shouldn't crash if it reports against an
            // already-evicted job. Silent no-op is correct here.
            Assert.DoesNotThrow(() =>
                _tracker.ReportProgress("nope", new PublishProgress()));
        }

        // ---- Mark complete ----

        [Test]
        public void MarkDone_FreezesSnapshot()
        {
            var id = _tracker.AllocateJob(1);
            var result = new PublishResult
            {
                Slug = "kitsune-den",
                Version = "1.2.3",
                FileCount = 42,
                TotalSize = 1024 * 1024,
                AlreadyPublished = false,
            };
            _tracker.MarkDone(id, result);
            var snap = _tracker.Get(id);
            Assert.That(snap.Status, Is.EqualTo(PublishJobStatus.Done));
            Assert.That(snap.Result, Is.Not.Null);
            Assert.That(snap.Result.Slug, Is.EqualTo("kitsune-den"));
            Assert.That(snap.Result.FileCount, Is.EqualTo(42));
            Assert.That(snap.ErrorMessage, Is.Null);
        }

        [Test]
        public void MarkError_FreezesSnapshot_WithCloudErrorCode()
        {
            var id = _tracker.AllocateJob(1);
            _tracker.MarkError(id, "Token revoked", "auth");
            var snap = _tracker.Get(id);
            Assert.That(snap.Status, Is.EqualTo(PublishJobStatus.Error));
            Assert.That(snap.ErrorMessage, Is.EqualTo("Token revoked"));
            Assert.That(snap.ErrorCode, Is.EqualTo("auth"));
            Assert.That(snap.Result, Is.Null);
        }

        // ---- ListRecent ----

        [Test]
        public void ListRecent_OrdersNewestFirst()
        {
            var a = _tracker.AllocateJob(1);
            _tracker.MarkDone(a, new PublishResult { Version = "1" });
            System.Threading.Thread.Sleep(10);
            var b = _tracker.AllocateJob(2);
            _tracker.MarkDone(b, new PublishResult { Version = "2" });

            var snaps = _tracker.ListRecent(10);
            Assert.That(snaps.Length, Is.EqualTo(2));
            Assert.That(snaps[0].JobId, Is.EqualTo(b),
                "Most-recently-started job must be first.");
            Assert.That(snaps[1].JobId, Is.EqualTo(a));
        }
    }
}
