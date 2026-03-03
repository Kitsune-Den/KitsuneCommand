using System.Web.Http;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// User account management — admin only.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/users")]
    public class UserController : ApiController
    {
        private readonly IUserAccountRepository _userRepo;

        private static readonly HashSet<string> ValidRoles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "admin", "moderator", "viewer" };

        public UserController(IUserAccountRepository userRepo)
        {
            _userRepo = userRepo;
        }

        /// <summary>
        /// Get all user accounts.
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetAll()
        {
            var users = _userRepo.GetAll().Select(MapToResponse);
            return Ok(ApiResponse.Ok(users));
        }

        /// <summary>
        /// Get a specific user account.
        /// </summary>
        [HttpGet]
        [Route("{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetById(int id)
        {
            var user = _userRepo.GetById(id);
            if (user == null)
                return NotFound();

            return Ok(ApiResponse.Ok(MapToResponse(user)));
        }

        /// <summary>
        /// Create a new user account.
        /// </summary>
        [HttpPost]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Create([FromBody] CreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Username))
                return BadRequest("Username is required.");
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
                return BadRequest("Password must be at least 8 characters.");
            if (!string.IsNullOrEmpty(request.Role) && !ValidRoles.Contains(request.Role))
                return BadRequest($"Invalid role. Must be one of: {string.Join(", ", ValidRoles)}");

            // Check uniqueness
            var existing = _userRepo.GetByUsername(request.Username);
            if (existing != null)
                return BadRequest("A user with that username already exists.");

            var account = new UserAccount
            {
                Username = request.Username.Trim().ToLowerInvariant(),
                PasswordHash = PasswordHasher.Hash(request.Password),
                DisplayName = request.DisplayName?.Trim() ?? request.Username,
                Role = request.Role ?? "viewer"
            };

            var id = _userRepo.Create(account);
            account.Id = id;

            return Ok(ApiResponse.Ok(MapToResponse(account)));
        }

        /// <summary>
        /// Update a user's display name, role, or active status.
        /// </summary>
        [HttpPut]
        [Route("{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Update(int id, [FromBody] UpdateUserRequest request)
        {
            var user = _userRepo.GetById(id);
            if (user == null)
                return NotFound();

            // Prevent demoting or deactivating the last active admin
            if (user.Role == "admin" && user.IsActive)
            {
                var isBeingDemoted = request.Role != null && !string.Equals(request.Role, "admin", StringComparison.OrdinalIgnoreCase);
                var isBeingDeactivated = request.IsActive.HasValue && !request.IsActive.Value;

                if ((isBeingDemoted || isBeingDeactivated) && _userRepo.CountActiveAdmins() <= 1)
                    return BadRequest("Cannot demote or deactivate the last active admin.");
            }

            if (!string.IsNullOrEmpty(request.Role))
            {
                if (!ValidRoles.Contains(request.Role))
                    return BadRequest($"Invalid role. Must be one of: {string.Join(", ", ValidRoles)}");
                user.Role = request.Role;
            }

            if (request.DisplayName != null)
                user.DisplayName = request.DisplayName.Trim();

            if (request.IsActive.HasValue)
                user.IsActive = request.IsActive.Value;

            _userRepo.Update(user);
            return Ok(ApiResponse.Ok(MapToResponse(user)));
        }

        /// <summary>
        /// Reset a user's password (admin action).
        /// </summary>
        [HttpPost]
        [Route("{id:int}/reset-password")]
        [RoleAuthorize("admin")]
        public IHttpActionResult ResetPassword(int id, [FromBody] ResetPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.NewPassword) || request.NewPassword.Length < 8)
                return BadRequest("Password must be at least 8 characters.");

            var user = _userRepo.GetById(id);
            if (user == null)
                return NotFound();

            _userRepo.UpdatePassword(id, PasswordHasher.Hash(request.NewPassword));
            return Ok(ApiResponse.Ok("Password updated."));
        }

        /// <summary>
        /// Soft-delete a user (set is_active = false).
        /// </summary>
        [HttpDelete]
        [Route("{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Delete(int id)
        {
            var user = _userRepo.GetById(id);
            if (user == null)
                return NotFound();

            // Prevent deactivating the last admin
            if (user.Role == "admin" && user.IsActive && _userRepo.CountActiveAdmins() <= 1)
                return BadRequest("Cannot deactivate the last active admin.");

            user.IsActive = false;
            _userRepo.Update(user);
            return Ok(ApiResponse.Ok("User deactivated."));
        }

        private static UserResponse MapToResponse(UserAccount user)
        {
            return new UserResponse
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Role = user.Role,
                LastLoginAt = user.LastLoginAt,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            };
        }
    }
}
