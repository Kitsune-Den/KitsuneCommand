using System.Web.Http;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// API controller for managing in-game chat command settings.
    /// Admin-only access.
    /// </summary>
    [RoutePrefix("api/settings/chat-commands")]
    public class ChatCommandSettingsController : ApiController
    {
        private readonly ChatCommandFeature _feature;

        public ChatCommandSettingsController(ChatCommandFeature feature)
        {
            _feature = feature;
        }

        /// <summary>
        /// Get current chat command settings.
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            return Ok(ApiResponse.Ok(_feature.Settings));
        }

        /// <summary>
        /// Update chat command settings.
        /// </summary>
        [HttpPut]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] ChatCommandSettings settings)
        {
            if (settings == null)
                return BadRequest("Settings body is required.");

            // Validate prefix
            if (string.IsNullOrEmpty(settings.Prefix))
                settings.Prefix = "/";

            // Validate ranges
            if (settings.DefaultCooldownSeconds < 0) settings.DefaultCooldownSeconds = 0;
            if (settings.HomeCooldownSeconds < 0) settings.HomeCooldownSeconds = 0;
            if (settings.TeleportCooldownSeconds < 0) settings.TeleportCooldownSeconds = 0;
            if (settings.MaxHomesPerPlayer < 1) settings.MaxHomesPerPlayer = 1;
            if (settings.MaxHomesPerPlayer > 50) settings.MaxHomesPerPlayer = 50;

            _feature.UpdateSettings(settings);
            return Ok(ApiResponse.Ok("Chat command settings updated."));
        }
    }
}
