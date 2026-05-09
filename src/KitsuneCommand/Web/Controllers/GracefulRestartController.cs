using System.Linq;
using System.Web.Http;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// REST surface for the graceful-restart feature.
    ///
    ///   GET    /api/restart/settings    — current schedule + warning ladder (admin)
    ///   PUT    /api/restart/settings    — overwrite schedule + ladder       (admin)
    ///   POST   /api/restart/now         — kick off a countdown right now    (admin)
    ///
    /// Same shape as TraderProtectionController / VoteRewardsController:
    /// pull the feature instance from the FeatureManager so the controller
    /// stays a thin façade over a singleton service.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/restart")]
    public class GracefulRestartController : ApiController
    {
        private readonly FeatureManager _featureManager;

        public GracefulRestartController(FeatureManager featureManager)
        {
            _featureManager = featureManager;
        }

        private GracefulRestartFeature GetFeature()
        {
            return _featureManager.GetAllFeatures()
                .OfType<GracefulRestartFeature>()
                .FirstOrDefault();
        }

        // ─── Settings ─────────────────────────────────────────────────

        [HttpGet]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "Graceful-restart feature not available."));

            return Ok(ApiResponse.Ok(feature.Settings));
        }

        [HttpPut]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] GracefulRestartSettings model)
        {
            if (model == null)
                return BadRequest("Request body is required.");

            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "Graceful-restart feature not available."));

            feature.UpdateSettings(model);

            var status = model.Enabled
                ? $"enabled — {model.ScheduledTime} {model.ScheduledTimezone}"
                : "disabled";
            return Ok(ApiResponse.Ok($"Graceful restart: {status}"));
        }

        // ─── Manual trigger ───────────────────────────────────────────

        public class TriggerRequest
        {
            /// <summary>0–1440. Defaults to longest configured warning step (10).</summary>
            public int? LeadMinutes { get; set; }
        }

        [HttpPost]
        [Route("now")]
        [RoleAuthorize("admin")]
        public IHttpActionResult TriggerNow([FromBody] TriggerRequest body)
        {
            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "Graceful-restart feature not available."));

            int lead = 10;
            if (body?.LeadMinutes is int v)
            {
                if (v < 0 || v > 1440)
                    return BadRequest("LeadMinutes must be 0–1440.");
                lead = v;
            }
            else if (feature.Settings?.WarningLadder != null && feature.Settings.WarningLadder.Count > 0)
            {
                lead = feature.Settings.WarningLadder.Max(w => w.MinutesBefore);
            }

            var actor = User?.Identity?.Name ?? "admin";
            var result = feature.TriggerNow(lead, actor);

            if (!result.Started)
                return Ok(ApiResponse.Error(409, result.Message));

            return Ok(ApiResponse.Ok(result.Message));
        }
    }
}
