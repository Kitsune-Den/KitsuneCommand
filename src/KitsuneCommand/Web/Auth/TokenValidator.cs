using System.Security.Claims;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.DataHandler;
using Microsoft.Owin.Security.DataHandler.Encoder;
using Microsoft.Owin.Security.DataHandler.Serializer;
using Microsoft.Owin.Security.DataProtection;

namespace KitsuneCommand.Web.Auth
{
    /// <summary>
    /// Validates OAuth bearer tokens outside the OWIN pipeline.
    /// Used by WebSocket handlers to authenticate connections.
    /// </summary>
    public static class TokenValidator
    {
        private static SecureDataFormat<AuthenticationTicket> _tokenFormat;

        /// <summary>
        /// Initializes the token validator with the same protection key used by OWIN OAuth.
        /// Must be called during startup with the same appName used in OwinStartup.
        /// </summary>
        public static void Initialize(string appName)
        {
            var provider = new HmacDataProtectionProvider(appName);
            // These purposes must match what OWIN OAuth middleware uses internally
            var protector = provider.Create("Microsoft.Owin.Security.OAuth", "Access_Token", "v1");

            _tokenFormat = new SecureDataFormat<AuthenticationTicket>(
                new TicketSerializer(),
                protector,
                TextEncodings.Base64Url);

            Log.Out("[KitsuneCommand] TokenValidator initialized.");
        }

        /// <summary>
        /// Creates a bearer token for the given user.
        /// </summary>
        public static string CreateToken(string username, string role, string userId, string displayName, TimeSpan expiresIn)
        {
            if (_tokenFormat == null)
                throw new InvalidOperationException("TokenValidator not initialized");

            var identity = new ClaimsIdentity("Bearer");
            identity.AddClaim(new Claim(ClaimTypes.Name, username));
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
            identity.AddClaim(new Claim("user_id", userId));
            identity.AddClaim(new Claim("display_name", displayName));

            var properties = new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(expiresIn)
            };

            var ticket = new AuthenticationTicket(identity, properties);
            return _tokenFormat.Protect(ticket);
        }

        /// <summary>
        /// Validates a bearer token and extracts the username and role.
        /// Returns true if the token is valid and not expired.
        /// </summary>
        public static bool ValidateToken(string token, out string username, out string role)
        {
            username = null;
            role = null;

            if (string.IsNullOrEmpty(token) || _tokenFormat == null)
                return false;

            try
            {
                var ticket = _tokenFormat.Unprotect(token);
                if (ticket == null)
                {
                    // Log at INFO so it's visible without DEBUG tracing. This is the
                    // path that hits when a client presents a token issued by a
                    // *different* running instance (e.g. key mismatch after a config
                    // change) — silent failure here cost us an afternoon of debugging
                    // why WebSocket connections were "completing with 0 bytes".
                    Log.Out("[KitsuneCommand] Token validation failed: unprotect returned null (malformed or from a different key).");
                    return false;
                }

                // Check expiration
                if (ticket.Properties?.ExpiresUtc != null && ticket.Properties.ExpiresUtc < DateTimeOffset.UtcNow)
                {
                    Log.Out($"[KitsuneCommand] Token validation failed: expired at {ticket.Properties.ExpiresUtc:o} (user={ticket.Identity?.FindFirst(ClaimTypes.Name)?.Value}).");
                    return false;
                }

                username = ticket.Identity?.FindFirst(ClaimTypes.Name)?.Value;
                role = ticket.Identity?.FindFirst(ClaimTypes.Role)?.Value;

                return !string.IsNullOrEmpty(username);
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Token validation failed: {ex.Message}");
                return false;
            }
        }
    }
}
