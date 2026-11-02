using System.Security.Claims;
using System.Web.Http;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// The JSON half of "Sign in through Steam". The redirecting half (login, link,
    /// callback, code exchange) lives on the login listener in WebServerHost, because
    /// the OWIN proxy forwards neither redirects nor cookies.
    /// </summary>
    [RoutePrefix("api/auth/steam")]
    public class SteamAuthController : ApiController
    {
        private readonly SteamLoginService _steam;
        private readonly IUserAccountRepository _userRepo;

        public SteamAuthController(SteamLoginService steam, IUserAccountRepository userRepo)
        {
            _steam = steam;
            _userRepo = userRepo;
        }

        /// <summary>
        /// Whether the login page should show the Steam button. Anonymous, and says
        /// nothing beyond on/off.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        [Route("status")]
        public IHttpActionResult GetStatus()
        {
            return Ok(ApiResponse.Ok(new { enabled = _steam.IsEnabled() }));
        }

        /// <summary>The current user's link state, for Settings → Account.</summary>
        [Authorize]
        [HttpGet]
        [Route("account")]
        public IHttpActionResult GetAccount()
        {
            var user = CurrentUser();
            if (user == null)
                return Unauthorized();

            return Ok(ApiResponse.Ok(new
            {
                enabled = _steam.IsEnabled(),
                linked = !string.IsNullOrEmpty(user.SteamId),
                steamId = user.SteamId
            }));
        }

        /// <summary>Unlinks the current user's Steam account. Allowed even while Steam sign-in is off.</summary>
        [Authorize]
        [HttpPost]
        [Route("unlink")]
        public IHttpActionResult Unlink()
        {
            var user = CurrentUser();
            if (user == null)
                return Unauthorized();

            _userRepo.SetSteamId(user.Id, null);
            Log.Out($"[KitsuneCommand] Steam account unlinked from panel user '{user.Username}'.");
            return Ok(ApiResponse.Ok("Steam account unlinked."));
        }

        [RoleAuthorize("admin")]
        [HttpGet]
        [Route("settings")]
        public IHttpActionResult GetSettings()
        {
            return Ok(ApiResponse.Ok(_steam.GetSettings()));
        }

        [RoleAuthorize("admin")]
        [HttpPut]
        [Route("settings")]
        public IHttpActionResult UpdateSettings([FromBody] SteamLoginSettings request)
        {
            try
            {
                return Ok(ApiResponse.Ok(_steam.SaveSettings(request)));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private Data.Entities.UserAccount CurrentUser()
        {
            var identity = User.Identity as ClaimsIdentity;
            var userId = identity?.FindFirst("user_id")?.Value;
            if (!int.TryParse(userId, out var id))
                return null;

            var user = _userRepo.GetById(id);
            return user != null && user.IsActive ? user : null;
        }
    }
}
