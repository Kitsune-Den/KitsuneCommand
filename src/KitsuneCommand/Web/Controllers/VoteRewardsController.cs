using System.Linq;
using System.Web.Http;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// REST surface for the Vote Rewards feature.
    ///
    ///   GET    /api/voterewards/settings        — current config (admin)
    ///   PUT    /api/voterewards/settings        — overwrite config (admin)
    ///   GET    /api/voterewards/grants          — recent audit-log rows (admin)
    ///   GET    /api/voterewards/grants/count    — total grant count (admin)
    ///
    /// Mirrors TraderProtectionController's pattern: pull the feature instance
    /// out of the FeatureManager rather than DI-ing it directly, so the
    /// controller stays cheap to instantiate per request.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/voterewards")]
    public class VoteRewardsController : ApiController
    {
        private readonly FeatureManager _featureManager;
        private readonly IVoteGrantRepository _grantRepo;

        public VoteRewardsController(FeatureManager featureManager, IVoteGrantRepository grantRepo)
        {
            _featureManager = featureManager;
            _grantRepo = grantRepo;
        }

        private VoteRewardsFeature GetFeature()
        {
            return _featureManager.GetAllFeatures()
                .OfType<VoteRewardsFeature>()
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
                return Ok(ApiResponse.Error(404, "Vote rewards feature not available."));

            // The settings blob carries API keys; we'd normally redact them on
            // GET, but parity with the existing TraderProtection / Discord
            // settings endpoints (which also return secrets to admins) wins
            // for now. The role gate is "admin" — if that's compromised we
            // have bigger problems than a vote API key.
            return Ok(ApiResponse.Ok(feature.Settings));
        }

        [HttpPut]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] VoteRewardsSettings model)
        {
            if (model == null)
                return BadRequest("Request body is required.");

            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "Vote rewards feature not available."));

            feature.UpdateSettings(model);

            var enabledCount = model.Providers?.Count(p => p.Enabled) ?? 0;
            var status = model.Enabled
                ? $"enabled — {enabledCount} provider(s) active"
                : "disabled";
            return Ok(ApiResponse.Ok($"Vote rewards: {status}"));
        }

        // ─── Audit Log ────────────────────────────────────────────────

        [HttpGet]
        [Route("grants")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetGrants(int limit = 50)
        {
            // Cap limit to keep the wire response reasonable. The web panel paginates,
            // but a misbehaving caller asking for "limit=1000000" should not OOM us.
            if (limit <= 0 || limit > 500) limit = 50;
            var rows = _grantRepo.GetRecent(limit).ToList();
            return Ok(ApiResponse.Ok(rows));
        }

        [HttpGet]
        [Route("grants/count")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetGrantCount()
        {
            return Ok(ApiResponse.Ok(_grantRepo.GetTotalCount()));
        }
    }
}
