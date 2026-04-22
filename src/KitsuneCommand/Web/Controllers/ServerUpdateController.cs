using System.Linq;
using System.Web.Http;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    [Authorize]
    [RoutePrefix("api/server-update")]
    public class ServerUpdateController : ApiController
    {
        private readonly FeatureManager _featureManager;

        public ServerUpdateController(FeatureManager featureManager)
        {
            _featureManager = featureManager;
        }

        private ServerUpdateFeature GetFeature()
        {
            return _featureManager.GetAllFeatures()
                .OfType<ServerUpdateFeature>()
                .FirstOrDefault();
        }

        [HttpGet]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "ServerUpdate feature not available."));

            return Ok(ApiResponse.Ok(feature.Settings));
        }

        [HttpPut]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] ServerUpdateSettings model)
        {
            if (model == null)
                return BadRequest("Request body is required.");

            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "ServerUpdate feature not available."));

            feature.UpdateSettings(model);
            return Ok(ApiResponse.Ok($"ServerUpdate settings saved. AutoUpdate={(model.AutoUpdate ? "ON" : "OFF")}, Branch={model.Branch}."));
        }

        /// <summary>
        /// Read the sticky serverconfig.xml.bak - the copy that gets restored over
        /// serverconfig.xml on every server start. This is where admins make durable edits.
        /// </summary>
        [HttpGet]
        [Route("config-bak")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetConfigBak()
        {
            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "ServerUpdate feature not available."));

            var content = feature.GetServerConfigBak();
            if (content == null)
                return Ok(ApiResponse.Error(404, "serverconfig.xml.bak does not exist yet. Save settings here to create it."));

            // Explicit generic arg: ApiResponse.Ok(string) hits the non-generic message
            // overload and the payload lands in `message` instead of `data`. The frontend
            // reads res.data.data, which would be undefined without this.
            return Ok(ApiResponse.Ok<string>(content));
        }

        /// <summary>
        /// Write new contents to serverconfig.xml.bak. Takes effect on next server restart.
        /// </summary>
        [HttpPut]
        [Route("config-bak")]
        [RoleAuthorize("admin")]
        public IHttpActionResult SetConfigBak([FromBody] ConfigBakRequest model)
        {
            if (model == null || string.IsNullOrEmpty(model.Content))
                return BadRequest("Request body with non-empty 'content' is required.");

            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "ServerUpdate feature not available."));

            var success = feature.SetServerConfigBak(model.Content);
            if (!success)
                return Ok(ApiResponse.Error(500, "Failed to write serverconfig.xml.bak - check server logs."));

            return Ok(ApiResponse.Ok("serverconfig.xml.bak saved. Will apply on next server restart."));
        }

        public class ConfigBakRequest
        {
            public string Content { get; set; }
        }

        /// <summary>
        /// Run `steamcmd +login` with the given password + optional Guard code to cache the
        /// Steam credentials on the server. Password and Guard code are passed via subprocess
        /// stdin (not command-line) and are NOT stored by KC. After a successful call,
        /// steamcmd's cache lets the pre-start script run auto-updates without prompting.
        /// </summary>
        [HttpPost]
        [Route("steam-auth")]
        [RoleAuthorize("admin")]
        public IHttpActionResult SteamAuth([FromBody] SteamAuthRequest model)
        {
            if (model == null || string.IsNullOrEmpty(model.Password))
                return BadRequest("Password is required.");

            var feature = GetFeature();
            if (feature == null)
                return Ok(ApiResponse.Error(404, "ServerUpdate feature not available."));

            var result = feature.AuthenticateSteam(model.Password, model.GuardCode);

            // Don't leak the password back in any form. The model object goes out of scope
            // on return; we never persist it.
            model.Password = null;
            model.GuardCode = null;

            return Ok(ApiResponse.Ok(result));
        }

        public class SteamAuthRequest
        {
            public string Password { get; set; }
            public string GuardCode { get; set; }
        }
    }
}
