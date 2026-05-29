using System.Linq;
using System.Web.Http;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// REST surface for the VipPerks feature (board #233, #234).
    ///
    ///   GET    /api/vipperks/settings   — current config (admin)
    ///   PUT    /api/vipperks/settings   — overwrite config (admin)
    ///   GET    /api/vipperks/tiers      — just the tier-name list (admin)
    ///
    /// Mirrors VoteRewardsController: pull the feature instance from the
    /// FeatureManager rather than DI-ing it directly.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/vipperks")]
    public class VipPerksController : ApiController
    {
        private readonly FeatureManager _featureManager;

        public VipPerksController(FeatureManager featureManager)
        {
            _featureManager = featureManager;
        }

        private VipPerksFeature GetFeature()
        {
            return _featureManager.GetAllFeatures()
                .OfType<VipPerksFeature>()
                .FirstOrDefault();
        }

        [HttpGet]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "VipPerks feature not available."));
            return Ok(ApiResponse.Ok(feature.Settings));
        }

        [HttpPut]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] VipPerksSettings model)
        {
            if (model == null)
                return BadRequest("Request body is required.");

            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "VipPerks feature not available."));

            feature.UpdateSettings(model);
            return Ok(ApiResponse.Ok($"VIP perks: {(model.Enabled ? "enabled" : "disabled")}"));
        }

        /// <summary>
        /// Lightweight list of configured tier names, for the player-edit tier
        /// dropdown. Returns an empty list if the feature isn't available.
        /// </summary>
        [HttpGet]
        [Route("tiers")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetTiers()
        {
            var feature = GetFeature();
            return Ok(ApiResponse.Ok(feature?.Settings?.Tiers ?? new System.Collections.Generic.List<string>()));
        }
    }
}
