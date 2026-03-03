using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Controllers;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Auth
{
    /// <summary>
    /// Custom authorization filter that checks the user's role claim against allowed roles.
    /// Returns 403 Forbidden (not 401) when the user is authenticated but lacks the required role.
    /// </summary>
    public class RoleAuthorizeAttribute : AuthorizeAttribute
    {
        private readonly string[] _allowedRoles;

        public RoleAuthorizeAttribute(params string[] roles)
        {
            _allowedRoles = roles;
        }

        protected override bool IsAuthorized(HttpActionContext actionContext)
        {
            if (!base.IsAuthorized(actionContext))
                return false;

            var identity = actionContext.RequestContext.Principal?.Identity as ClaimsIdentity;
            var role = identity?.FindFirst(ClaimTypes.Role)?.Value;

            return role != null && _allowedRoles.Any(r =>
                string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        }

        protected override void HandleUnauthorizedRequest(HttpActionContext actionContext)
        {
            if (actionContext.RequestContext.Principal?.Identity?.IsAuthenticated == true)
            {
                // Authenticated but wrong role → 403 Forbidden
                actionContext.Response = actionContext.Request.CreateResponse(
                    HttpStatusCode.Forbidden,
                    ApiResponse.Error(403, "Insufficient permissions for this action."));
            }
            else
            {
                base.HandleUnauthorizedRequest(actionContext);
            }
        }
    }
}
