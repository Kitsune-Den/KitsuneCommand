using System.Net;
using System.Web.Http;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// API controller for blood moon vote skip feature.
    /// Provides vote status, admin settings management, and force-skip.
    /// </summary>
    [RoutePrefix("api/bloodmoonvote")]
    public class BloodMoonVoteController : ApiController
    {
        private readonly BloodMoonVoteFeature _feature;

        public BloodMoonVoteController(BloodMoonVoteFeature feature)
        {
            _feature = feature;
        }

        /// <summary>
        /// Get current vote status (available to all authenticated users).
        /// </summary>
        [HttpGet]
        [Route("status")]
        public IHttpActionResult GetStatus()
        {
            return Ok(ApiResponse.Ok(_feature.GetVoteStatus()));
        }

        /// <summary>
        /// Get blood moon vote settings (admin only).
        /// </summary>
        [HttpGet]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            return Ok(ApiResponse.Ok(_feature.Settings));
        }

        /// <summary>
        /// Update blood moon vote settings (admin only).
        /// </summary>
        [HttpPut]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] BloodMoonVoteSettings settings)
        {
            if (settings == null)
                return BadRequest("Settings body is required.");

            // Validate ranges
            if (settings.ThresholdValue < 1) settings.ThresholdValue = 1;
            if (settings.ThresholdType == "percentage" && settings.ThresholdValue > 100)
                settings.ThresholdValue = 100;
            if (settings.CooldownMinutes < 0) settings.CooldownMinutes = 0;
            if (settings.AllowVoteHoursBefore < 0) settings.AllowVoteHoursBefore = 0;
            if (settings.AllowVoteHoursBefore > 22) settings.AllowVoteHoursBefore = 22;

            // Ensure valid threshold type
            if (settings.ThresholdType != "percentage" && settings.ThresholdType != "count")
                settings.ThresholdType = "percentage";

            // Ensure command name is not empty
            if (string.IsNullOrWhiteSpace(settings.CommandName))
                settings.CommandName = "skipbm";

            _feature.UpdateSettings(settings);
            return Ok(ApiResponse.Ok("Blood moon vote settings updated."));
        }

        /// <summary>
        /// Force-skip the current blood moon (admin only).
        /// </summary>
        [HttpPost]
        [Route("force-skip")]
        [RoleAuthorize("admin")]
        public IHttpActionResult ForceSkip()
        {
            if (!_feature.ForceSkip())
                return Content(HttpStatusCode.ServiceUnavailable,
                    ApiResponse.Error(503, "The game didn't answer in time, so the blood moon was not skipped. Try again."));

            return Ok(ApiResponse.Ok("Blood moon force-skipped."));
        }
    }
}
