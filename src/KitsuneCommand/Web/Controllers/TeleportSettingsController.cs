using System.Web.Http;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;
using Newtonsoft.Json;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// API controller for managing Teleport feature settings.
    /// Admin-only access.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/settings/teleport")]
    public class TeleportSettingsController : ApiController
    {
        private readonly ISettingsRepository _settingsRepo;
        private const string SettingsKey = TeleportPolicy.SettingsKey;

        public TeleportSettingsController(ISettingsRepository settingsRepo)
        {
            _settingsRepo = settingsRepo;
        }

        /// <summary>
        /// Get current teleport settings.
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            return Ok(ApiResponse.Ok(LoadSettings()));
        }

        /// <summary>
        /// Update teleport settings.
        /// </summary>
        [HttpPut]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] TeleportSettings settings)
        {
            if (settings == null)
                return BadRequest("Settings body is required.");

            // Validate ranges
            if (settings.TeleportDelaySeconds < 0) settings.TeleportDelaySeconds = 0;
            if (settings.TeleportDelaySeconds > 60) settings.TeleportDelaySeconds = 60;
            if (settings.DefaultPointsCost < 0) settings.DefaultPointsCost = 0;

            try
            {
                var json = JsonConvert.SerializeObject(settings);
                _settingsRepo.Set(SettingsKey, json);
                Log.Out("[KitsuneCommand] Teleport settings updated and saved.");
                return Ok(ApiResponse.Ok("Teleport settings updated."));
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to save teleport settings: {ex.Message}");
                return InternalServerError();
            }
        }

        private TeleportSettings LoadSettings() => TeleportPolicy.LoadSettings(_settingsRepo);
    }
}
