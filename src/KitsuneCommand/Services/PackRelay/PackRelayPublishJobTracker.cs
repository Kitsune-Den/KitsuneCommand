// In-process job tracker for the publish flow. The web layer kicks
// off PublishAsync as a fire-and-forget task and lets the frontend
// poll for status — no SSE / WebSocket needed for a flow that runs
// 30s-5min and has only one job-at-a-time semantics.
//
// Why polling, not SSE:
//   - KC's existing real-time UI (web console) uses WebSockets, not
//     SSE, so adding SSE infrastructure for one feature would be
//     awkward.
//   - 1s polling is well within the human-perception threshold for
//     "progress is moving"; the orchestrator emits a progress event
//     every file uploaded, so the 1s GET picks up the latest snapshot.
//   - Single-job semantics (publish-already-in-flight returns the
//     existing job) means we don't need fan-out, which is the only
//     thing that would actually benefit from SSE.
//
// Singleton lifecycle: registered SingleInstance in ServiceRegistry.
// Jobs live in a ConcurrentDictionary keyed by their string GUID;
// stale entries auto-evict 30 minutes after final state so a long-
// running panel session doesn't leak.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KitsuneCommand.Services.PackRelay
{
    public enum PublishJobStatus
    {
        Running,
        Done,
        Error,
        Cancelled,
    }

    /// <summary>
    /// Snapshot of a publish job's state — what the frontend polls
    /// for. Mutable in place under <see cref="PackRelayPublishJobTracker"/>'s
    /// internal lock; copied to a fresh instance on every read so
    /// the caller can't accidentally race with the worker task.
    /// </summary>
    public class PublishJobSnapshot
    {
        public string JobId { get; set; }
        public int ModpackId { get; set; }
        public PublishJobStatus Status { get; set; }
        /// <summary>Latest progress event from the orchestrator. Null only briefly during startup.</summary>
        public PublishProgress LatestProgress { get; set; }
        /// <summary>Final result when Status == Done; null otherwise.</summary>
        public PublishResult Result { get; set; }
        /// <summary>Human-readable error when Status == Error; null otherwise.</summary>
        public string ErrorMessage { get; set; }
        /// <summary>Cloud's machine-readable error code when available (e.g. "auth", "duplicate_version").</summary>
        public string ErrorCode { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class PackRelayPublishJobTracker
    {
        private readonly ConcurrentDictionary<string, JobRecord> _jobs =
            new ConcurrentDictionary<string, JobRecord>();
        private readonly object _activeLock = new object();
        // Job IDs that are still running. At most one at a time
        // because we serialize through PublishAsync (which holds the
        // worker thread). Tracked separately so the controller can
        // short-circuit a second concurrent publish.
        private string _activeJobId;

        /// <summary>
        /// Allocate a new job slot AND check that no other publish
        /// is already in flight. Returns the job id when the slot
        /// is yours; throws <see cref="InvalidOperationException"/>
        /// when another publish is in progress (the controller
        /// surfaces this as 409).
        /// </summary>
        public string AllocateJob(int modpackId)
        {
            lock (_activeLock)
            {
                if (_activeJobId != null && _jobs.TryGetValue(_activeJobId, out var existing)
                    && existing.Snapshot.Status == PublishJobStatus.Running)
                {
                    throw new InvalidOperationException(
                        "Another PackRelay publish is already running (job " +
                        existing.Snapshot.JobId + "). Wait for it to complete.");
                }
                var id = Guid.NewGuid().ToString("N");
                var rec = new JobRecord
                {
                    Snapshot = new PublishJobSnapshot
                    {
                        JobId = id,
                        ModpackId = modpackId,
                        Status = PublishJobStatus.Running,
                        StartedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow,
                    },
                };
                _jobs[id] = rec;
                _activeJobId = id;
                return id;
            }
        }

        /// <summary>
        /// Worker calls this from the orchestrator's IProgress callback.
        /// Replaces the snapshot's LatestProgress + bumps UpdatedAt.
        /// </summary>
        public void ReportProgress(string jobId, PublishProgress progress)
        {
            if (_jobs.TryGetValue(jobId, out var rec))
            {
                lock (rec)
                {
                    rec.Snapshot.LatestProgress = progress;
                    rec.Snapshot.UpdatedAtUtc = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Worker calls this on PublishAsync success.
        /// </summary>
        public void MarkDone(string jobId, PublishResult result)
        {
            if (_jobs.TryGetValue(jobId, out var rec))
            {
                lock (rec)
                {
                    rec.Snapshot.Status = PublishJobStatus.Done;
                    rec.Snapshot.Result = result;
                    rec.Snapshot.UpdatedAtUtc = DateTime.UtcNow;
                }
            }
            ReleaseActive(jobId);
            ScheduleEvict(jobId);
        }

        /// <summary>
        /// Worker calls this on any unhandled exception. <paramref name="errorCode"/>
        /// is the cloud's machine-readable code when the exception
        /// was a PackRelayApiException; null otherwise.
        /// </summary>
        public void MarkError(string jobId, string errorMessage, string errorCode)
        {
            if (_jobs.TryGetValue(jobId, out var rec))
            {
                lock (rec)
                {
                    rec.Snapshot.Status = PublishJobStatus.Error;
                    rec.Snapshot.ErrorMessage = errorMessage;
                    rec.Snapshot.ErrorCode = errorCode;
                    rec.Snapshot.UpdatedAtUtc = DateTime.UtcNow;
                }
            }
            ReleaseActive(jobId);
            ScheduleEvict(jobId);
        }

        /// <summary>
        /// Frontend polling endpoint hits this. Returns a defensive
        /// copy so the caller can't observe a mid-update tear
        /// (LatestProgress + Status updated by the worker thread).
        /// </summary>
        public PublishJobSnapshot Get(string jobId)
        {
            if (!_jobs.TryGetValue(jobId, out var rec)) return null;
            lock (rec)
            {
                return new PublishJobSnapshot
                {
                    JobId = rec.Snapshot.JobId,
                    ModpackId = rec.Snapshot.ModpackId,
                    Status = rec.Snapshot.Status,
                    LatestProgress = rec.Snapshot.LatestProgress,
                    Result = rec.Snapshot.Result,
                    ErrorMessage = rec.Snapshot.ErrorMessage,
                    ErrorCode = rec.Snapshot.ErrorCode,
                    StartedAtUtc = rec.Snapshot.StartedAtUtc,
                    UpdatedAtUtc = rec.Snapshot.UpdatedAtUtc,
                };
            }
        }

        /// <summary>
        /// All jobs, newest first. Useful for the settings page's
        /// "recent publishes" list (if we add one later).
        /// </summary>
        public PublishJobSnapshot[] ListRecent(int max = 10)
        {
            var snaps = _jobs.Values
                .Select(r =>
                {
                    lock (r)
                    {
                        return r.Snapshot;
                    }
                })
                .OrderByDescending(s => s.StartedAtUtc)
                .Take(max)
                .ToArray();
            // Each snapshot reference is the live one; defensive copy
            // happens at point-of-read in callers.
            return snaps;
        }

        private void ReleaseActive(string jobId)
        {
            lock (_activeLock)
            {
                if (_activeJobId == jobId) _activeJobId = null;
            }
        }

        // Stale-job eviction: 30 min after final state, drop the
        // record. Long enough for a panel user to navigate back and
        // see "you published v1.0.0 28 minutes ago"; short enough
        // that a server running for a month doesn't accumulate
        // hundreds of dead jobs.
        private void ScheduleEvict(string jobId)
        {
            Task.Delay(TimeSpan.FromMinutes(30))
                .ContinueWith(_ =>
                {
                    JobRecord _evicted;
                    _jobs.TryRemove(jobId, out _evicted);
                });
        }

        private class JobRecord
        {
            public PublishJobSnapshot Snapshot;
        }
    }
}
