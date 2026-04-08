using System;
using System.Linq;
using System.Web.Http;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    [Authorize]
    [RoutePrefix("api/pvp")]
    public class PvPBalanceController : ApiController
    {
        private readonly FeatureManager _featureManager;

        public PvPBalanceController(FeatureManager featureManager)
        {
            _featureManager = featureManager;
        }

        private PvPBalanceFeature GetFeature()
        {
            return _featureManager.GetAllFeatures()
                .OfType<PvPBalanceFeature>()
                .FirstOrDefault();
        }

        [HttpGet]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "PvP balance feature not available."));

            return Ok(ApiResponse.Ok(feature.Settings));
        }

        [HttpPut]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] PvPBalanceSettings model)
        {
            if (model == null)
                return BadRequest("Request body is required.");

            // Clamp values
            model.DamageMultiplier = Math.Max(0f, Math.Min(10f, model.DamageMultiplier));
            model.HeadshotMultiplier = Math.Max(0f, Math.Min(10f, model.HeadshotMultiplier));

            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "PvP balance feature not available."));

            feature.UpdateSettings(model);
            return Ok(ApiResponse.Ok("PvP balance settings updated. Changes take effect immediately."));
        }
    }
}
