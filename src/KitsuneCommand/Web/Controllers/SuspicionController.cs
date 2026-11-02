using System;
using System.Collections.Generic;
using System.Web.Http;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Suspicion log: list what the detectors recorded, mark entries reviewed,
    /// and tune the detectors. Admin only. Nothing here acts on a player.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/suspicion")]
    [RoleAuthorize("admin")]
    public class SuspicionController : ApiController
    {
        private readonly ISuspicionEventRepository _repo;
        private readonly SuspicionFeature _feature;

        public SuspicionController(ISuspicionEventRepository repo, SuspicionFeature feature)
        {
            _repo = repo;
            _feature = feature;
        }

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetEvents(int pageIndex = 0, int pageSize = 50, string detector = null,
            string severity = null, bool? reviewed = null, string search = null)
        {
            try
            {
                pageIndex = Math.Max(0, pageIndex);
                pageSize = Math.Max(1, Math.Min(200, pageSize));
                var filter = new SuspicionEventFilter { Detector = detector, Severity = severity, Reviewed = reviewed, Search = search };
                var items = _repo.GetPage(pageIndex, pageSize, filter);
                var total = _repo.GetCount(filter);
                var unreviewed = _repo.GetCount(new SuspicionEventFilter { Reviewed = false });
                return Ok(ApiResponse.Ok(new { items, total, unreviewed }));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to load the suspicion log: {ex.Message}"));
            }
        }

        [HttpPost]
        [Route("review")]
        public IHttpActionResult Review([FromBody] ReviewSuspicionRequest request)
        {
            if (request?.Ids == null || request.Ids.Count == 0)
                return BadRequest("ids is required.");

            try
            {
                var by = User?.Identity?.Name ?? "admin";
                var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
                var updated = _repo.SetReviewed(request.Ids, request.Reviewed, by, note);
                return Ok(ApiResponse.Ok(new { updated }));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to update the suspicion log: {ex.Message}"));
            }
        }

        [HttpGet]
        [Route("settings")]
        public IHttpActionResult GetSettings()
        {
            return Ok(ApiResponse.Ok(_feature.Settings));
        }

        [HttpPut]
        [Route("settings")]
        public IHttpActionResult UpdateSettings([FromBody] SuspicionSettings model)
        {
            if (model == null)
                return BadRequest("Request body is required.");

            try
            {
                _feature.UpdateSettings(model);
                return Ok(ApiResponse.Ok(_feature.Settings));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to save suspicion settings: {ex.Message}"));
            }
        }
    }

    public class ReviewSuspicionRequest
    {
        public List<int> Ids { get; set; }
        public bool Reviewed { get; set; } = true;
        public string Note { get; set; }
    }
}
