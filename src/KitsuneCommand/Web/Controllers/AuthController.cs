using System.Security.Claims;
using System.Web.Http;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Authentication endpoints: current user info, password change.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/auth")]
    public class AuthController : ApiController
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        /// <summary>
        /// Returns the currently authenticated user's info from their token claims.
        /// </summary>
        [HttpGet]
        [Route("me")]
        public IHttpActionResult GetCurrentUser()
        {
            var identity = User.Identity as ClaimsIdentity;
            if (identity == null)
                return Unauthorized();

            var userInfo = new
            {
                username = identity.FindFirst(ClaimTypes.Name)?.Value,
                role = identity.FindFirst(ClaimTypes.Role)?.Value,
                userId = identity.FindFirst("user_id")?.Value,
                displayName = identity.FindFirst("display_name")?.Value
            };

            return Ok(ApiResponse.Ok(userInfo));
        }

        /// <summary>
        /// Changes the current user's password.
        /// </summary>
        [HttpPost]
        [Route("change-password")]
        public IHttpActionResult ChangePassword([FromBody] ChangePasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
                return BadRequest("Current password and new password are required.");

            if (request.NewPassword.Length < 8)
                return BadRequest("New password must be at least 8 characters.");

            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                return Unauthorized();

            var success = _authService.ChangePassword(username, request.CurrentPassword, request.NewPassword);
            if (!success)
                return BadRequest("Current password is incorrect.");

            return Ok(ApiResponse.Ok(new { message = "Password changed successfully." }));
        }
    }

    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
    }
}
