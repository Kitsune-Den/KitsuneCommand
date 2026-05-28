using System;
using System.Web.Http;
using KitsuneCommand.Diagnostics;
using KitsuneCommand.GameIntegration.Harmony;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Reads the in-memory <see cref="JoinAttemptRing"/> populated by
    /// <see cref="AuthWrapperServerDiagnostics"/> so the panel's
    /// "Join Attempts" page can show operators what's happening at
    /// connection-time — specifically the LiteNetLib-layer detail that
    /// 7DTD's vanilla "Peer disconnected in auth state: ... / 0" log line
    /// hides.
    ///
    /// All endpoints are admin-only. The data isn't terribly sensitive
    /// (IPs + ports + protocol-level state), but knowing which IPs are
    /// hammering the server with failed handshakes IS the kind of thing
    /// you'd want an admin gate on.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/join-attempts")]
    public class JoinAttemptsController : ApiController
    {
        /// <summary>
        /// Get the most recent join-attempt events. Returns newest-first.
        ///
        /// Query params:
        /// <list type="bullet">
        ///   <item><c>limit</c> — max events to return (default 100, capped at 500)</item>
        ///   <item><c>since</c> — ISO-8601 UTC timestamp; only events at or
        ///   after this time. Combine with the previous-page's newest
        ///   timestamp for incremental polling.</item>
        /// </list>
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult List(int limit = 100, string since = null)
        {
            DateTime? sinceUtc = null;
            if (!string.IsNullOrEmpty(since))
            {
                if (DateTime.TryParse(since, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    sinceUtc = parsed;
                }
                else
                {
                    return Ok(ApiResponse.Error(400, "Invalid 'since' parameter; expected ISO-8601 timestamp."));
                }
            }

            var events = JoinAttemptRing.Snapshot(limit, sinceUtc);
            return Ok(ApiResponse.Ok(new
            {
                events,
                totalRecorded = JoinAttemptRing.TotalRecorded,
                verboseLogging = AuthWrapperServerDiagnostics.Enabled,
                capacity = JoinAttemptRing.Capacity,
            }));
        }

        /// <summary>
        /// Clear the ring buffer. Useful for "start fresh before reproducing
        /// the bug" debugging flows. Doesn't reset the monotonic
        /// <c>totalRecorded</c> counter — that's process-lifetime activity.
        /// </summary>
        [HttpPost]
        [Route("clear")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Clear()
        {
            JoinAttemptRing.Clear();
            return Ok(ApiResponse.Ok("Join-attempt ring cleared."));
        }

        /// <summary>
        /// Toggle the verbose-console-logging side of the diagnostics. The
        /// ring buffer always records regardless; this controls only whether
        /// each event ALSO produces a <c>[KC-NetDiag]</c> line in
        /// nssm-stdout.log.
        ///
        /// Default off — recommended for steady-state. Flip on when
        /// reproducing a specific bug and you want the log file populated
        /// alongside the panel.
        /// </summary>
        [HttpPost]
        [Route("verbose")]
        [RoleAuthorize("admin")]
        public IHttpActionResult SetVerbose([FromBody] VerboseRequest body)
        {
            if (body == null)
                return Ok(ApiResponse.Error(400, "Body required: { \"enabled\": true|false }"));

            AuthWrapperServerDiagnostics.Enabled = body.Enabled;
            Log.Out("[KitsuneCommand] AuthWrapperServerDiagnostics verbose logging "
                + (body.Enabled ? "ENABLED" : "disabled"));
            return Ok(ApiResponse.Ok(new { enabled = body.Enabled }));
        }

        public class VerboseRequest
        {
            public bool Enabled { get; set; }
        }
    }
}
