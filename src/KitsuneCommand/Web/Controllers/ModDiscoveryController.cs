using System;
using System.Web.Http;
using KitsuneCommand.Features;
using KitsuneCommand.Services;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    [Authorize]
    [RoutePrefix("api/mods/discover")]
    public class ModDiscoveryController : ApiController
    {
        private readonly NexusModDiscoveryService _nexusService;
        private readonly SevenDaysModsService _sevenDaysMods;

        public ModDiscoveryController(NexusModDiscoveryService nexusService, SevenDaysModsService sevenDaysMods)
        {
            _nexusService = nexusService;
            _sevenDaysMods = sevenDaysMods;
        }

        /// <summary>
        /// Search mods on Nexus with pagination and sorting.
        /// No API key required (uses GraphQL v2 public API).
        /// </summary>
        [HttpGet]
        [Route("search")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Search(string q = "", string sort = "endorsements", int offset = 0, int count = 20)
        {
            try
            {
                count = Math.Min(count, 50);
                var result = _nexusService.SearchMods(q, sort, offset, count);
                return Ok(ApiResponse.Ok(result));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to search mods: {ex.Message}"));
            }
        }

        /// <summary>
        /// Search 7daystodiemods.com (public API, no key). Page-based: page 1 is
        /// the first page. sort: newest | updated | likes | downloads | views.
        /// Filters take the slugs from /7daystodiemods/metadata.
        /// </summary>
        [HttpGet]
        [Route("7daystodiemods/search")]
        [RoleAuthorize("admin")]
        public IHttpActionResult SearchSevenDaysMods(string q = "", string sort = "newest", int page = 1, int limit = 20,
            string category = null, string gameVersion = null, string serverSide = null)
        {
            try
            {
                return Ok(ApiResponse.Ok(_sevenDaysMods.Search(q, sort, page, limit, category, gameVersion, serverSide)));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to search 7daystodiemods.com: {ex.Message}"));
            }
        }

        /// <summary>7daystodiemods.com's categories, game versions and server-side values, for the filters.</summary>
        [HttpGet]
        [Route("7daystodiemods/metadata")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSevenDaysModsMetadata()
        {
            return Ok(ApiResponse.Ok(_sevenDaysMods.GetMetadata()));
        }

        [HttpGet]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            var settings = _nexusService.GetSettings();
            var masked = new
            {
                apiKey = string.IsNullOrEmpty(settings.ApiKey) ? "" : "***" + settings.ApiKey.Substring(Math.Max(0, settings.ApiKey.Length - 4)),
                hasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey),
                settings.CacheDurationMinutes
            };
            return Ok(ApiResponse.Ok(masked));
        }

        [HttpPut]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] NexusSettingsUpdateModel model)
        {
            if (model == null)
                return BadRequest("Request body is required.");

            var settings = _nexusService.GetSettings();

            if (model.ApiKey != null)
                settings.ApiKey = model.ApiKey;

            if (model.CacheDurationMinutes.HasValue)
                settings.CacheDurationMinutes = Math.Max(5, Math.Min(1440, model.CacheDurationMinutes.Value));

            _nexusService.SaveSettings(settings);
            return Ok(ApiResponse.Ok("Nexus settings saved."));
        }

        [HttpPost]
        [Route("validate-key")]
        [RoleAuthorize("admin")]
        public IHttpActionResult ValidateKey([FromBody] NexusValidateKeyModel model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.ApiKey))
                return BadRequest("API key is required.");

            var result = _nexusService.ValidateKey(model.ApiKey);
            return Ok(ApiResponse.Ok(result));
        }

        [HttpPost]
        [Route("clear-cache")]
        [RoleAuthorize("admin")]
        public IHttpActionResult ClearCache()
        {
            _nexusService.ClearCache();
            _sevenDaysMods.ClearCache();
            return Ok(ApiResponse.Ok("Cache cleared."));
        }
    }

    public class NexusSettingsUpdateModel
    {
        public string ApiKey { get; set; }
        public int? CacheDurationMinutes { get; set; }
    }

    public class NexusValidateKeyModel
    {
        public string ApiKey { get; set; }
    }
}
