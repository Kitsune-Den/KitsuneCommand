using System.Linq;
using System.Web.Http;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Safe zones drawn on the map. Anyone signed in to the panel can see them
    /// (they're drawn on the map page); only admins can change them.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/safezones")]
    public class SafeZoneController : ApiController
    {
        private readonly FeatureManager _featureManager;

        public SafeZoneController(FeatureManager featureManager)
        {
            _featureManager = featureManager;
        }

        private SafeZoneFeature GetFeature() =>
            _featureManager.GetAllFeatures().OfType<SafeZoneFeature>().FirstOrDefault();

        [HttpGet]
        [Route("")]
        public IHttpActionResult Get()
        {
            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "Safe zone feature not available."));

            return Ok(ApiResponse.Ok(View(feature)));
        }

        /// <summary>Replaces the whole settings object, zones included. Takes effect on the next sweep.</summary>
        [HttpPut]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Update([FromBody] SafeZoneSettings model)
        {
            if (model == null)
                return BadRequest("Request body is required.");

            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "Safe zone feature not available."));

            feature.UpdateSettings(model);
            return Ok(ApiResponse.Ok(View(feature)));
        }

        private static object View(SafeZoneFeature feature)
        {
            var settings = feature.Settings;
            var counts = feature.RemovedCounts();
            return new
            {
                enabled = settings.Enabled,
                sweepIntervalSeconds = settings.SweepIntervalSeconds,
                zones = settings.Zones.Select(z => new
                {
                    id = z.Id,
                    name = z.Name,
                    enabled = z.Enabled,
                    minX = z.MinX,
                    minZ = z.MinZ,
                    maxX = z.MaxX,
                    maxZ = z.MaxZ,
                    removeAnimals = z.RemoveAnimals,
                    allowBloodMoon = z.AllowBloodMoon,
                    removedCount = counts.TryGetValue(z.Id, out var n) ? n : 0,
                }),
            };
        }
    }
}
