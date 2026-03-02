using System.Security.Claims;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OAuth;
using System.Collections.Concurrent;

namespace KitsuneCommand.Web.Auth
{
    /// <summary>
    /// OAuth2 password grant provider. Validates credentials and issues JWT-like tokens.
    /// </summary>
    public class OAuthProvider : OAuthAuthorizationServerProvider
    {
        private readonly AuthService _authService;
        private static readonly ConcurrentDictionary<string, RefreshTokenInfo> _refreshTokens
            = new ConcurrentDictionary<string, RefreshTokenInfo>();

        public OAuthProvider(AuthService authService)
        {
            _authService = authService;
        }

        public override Task ValidateClientAuthentication(OAuthValidateClientAuthenticationContext context)
        {
            context.Validated();
            return Task.CompletedTask;
        }

        public override Task GrantResourceOwnerCredentials(OAuthGrantResourceOwnerCredentialsContext context)
        {
            var account = _authService.ValidateCredentials(context.UserName, context.Password);
            if (account == null)
            {
                context.SetError("invalid_grant", "Invalid username or password.");
                return Task.CompletedTask;
            }

            var identity = new ClaimsIdentity(context.Options.AuthenticationType);
            identity.AddClaim(new Claim(ClaimTypes.Name, account.Username));
            identity.AddClaim(new Claim(ClaimTypes.Role, account.Role));
            identity.AddClaim(new Claim("user_id", account.Id.ToString()));
            identity.AddClaim(new Claim("display_name", account.DisplayName ?? account.Username));

            var properties = new AuthenticationProperties(new Dictionary<string, string>
            {
                { "username", account.Username },
                { "role", account.Role },
                { "display_name", account.DisplayName ?? account.Username }
            });

            var ticket = new AuthenticationTicket(identity, properties);
            context.Validated(ticket);

            return Task.CompletedTask;
        }

        public override Task GrantRefreshToken(OAuthGrantRefreshTokenContext context)
        {
            context.Validated(context.Ticket);
            return Task.CompletedTask;
        }

        public override Task TokenEndpointResponse(OAuthTokenEndpointResponseContext context)
        {
            // Add custom fields to token response
            foreach (var property in context.Properties.Dictionary)
            {
                context.AdditionalResponseParameters.Add(property.Key, property.Value);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stores refresh token info for validation.
        /// </summary>
        public static void StoreRefreshToken(string tokenId, string username, DateTime expiresUtc)
        {
            _refreshTokens[tokenId] = new RefreshTokenInfo
            {
                Username = username,
                ExpiresUtc = expiresUtc
            };

            // Cleanup expired tokens periodically
            var expired = _refreshTokens.Where(kvp => kvp.Value.ExpiresUtc < DateTime.UtcNow).ToList();
            foreach (var kvp in expired)
            {
                _refreshTokens.TryRemove(kvp.Key, out _);
            }
        }

        private class RefreshTokenInfo
        {
            public string Username { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }
    }
}
