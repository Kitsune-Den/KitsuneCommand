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

        public ModDiscoveryController(NexusModDiscoveryService nexusService)
        {
            _nexusService = nexusService;
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
