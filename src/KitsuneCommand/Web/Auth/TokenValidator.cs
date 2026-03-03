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
                    return false;

                // Check expiration
                if (ticket.Properties?.ExpiresUtc != null && ticket.Properties.ExpiresUtc < DateTimeOffset.UtcNow)
                    return false;

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
