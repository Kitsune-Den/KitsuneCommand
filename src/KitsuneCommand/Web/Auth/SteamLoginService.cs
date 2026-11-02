using System.Collections.Specialized;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Web.Auth
{
    /// <summary>
    /// Admin-controlled settings for "Sign in through Steam". Stored as JSON in the
    /// settings table under <see cref="SteamLoginService.SettingsKey"/>.
    /// </summary>
    public class SteamLoginSettings
    {
        public bool Enabled { get; set; }

        /// <summary>
        /// The panel's address as the browser sees it (e.g. https://panel.example.com,
        /// or https://example.com/kc behind a sub-path proxy). Steam sends people back
        /// here, so it has to be configured explicitly: deriving it from the request's
        /// Host header would let anyone who can forge that header point Steam's signed
        /// response at a site they control and redeem it as the victim.
        /// </summary>
        public string PublicUrl { get; set; }
    }

    public enum SteamAuthPurpose
    {
        Login,
        Link
    }

    /// <summary>A started Steam round trip: where to send the browser, and the state to pin in a cookie.</summary>
    public class SteamAuthStart
    {
        public string RedirectUrl { get; set; }
        public string State { get; set; }
        public bool SecureCookie { get; set; }
    }

    /// <summary>
    /// "Sign in through Steam" for existing panel accounts, using Steam's OpenID 2.0
    /// provider. Steam only proves "this browser controls SteamID X"; what that SteamID
    /// may do is decided here by looking it up in user_accounts.steam_id. A Steam
    /// sign-in never creates an account — a signed-in user links their Steam account
    /// from Settings first.
    ///
    /// Round trip:
    ///   1. BeginLogin / BeginLink stores a single-use state (purpose, user for a link,
    ///      exact return_to) and returns Steam's URL. The caller pins the state in a
    ///      cookie so the callback can only be completed by the browser that started it.
    ///   2. Steam redirects back to /api/auth/steam/callback. HandleCallback checks the
    ///      state + cookie, the assertion's fields, nonce reuse, and finally asks Steam
    ///      to confirm the signature (check_authentication).
    ///   3. A login ends with a short-lived single-use code in the redirect fragment;
    ///      the SPA trades it for a bearer token via RedeemCode. The token itself never
    ///      appears in a URL.
    /// </summary>
    public class SteamLoginService
    {
        public const string SettingsKey = "SteamLogin";
        public const string SteamOpenIdEndpoint = "https://steamcommunity.com/openid/login";
        public const string StateCookieName = "kc_steam_state";
        public const string CallbackPath = "/api/auth/steam/callback";

        private const string OpenIdNs = "http://specs.openid.net/auth/2.0";
        private const string IdentifierSelect = "http://specs.openid.net/auth/2.0/identifier_select";

        private static readonly Regex ClaimedIdPattern =
            new Regex(@"^https://steamcommunity\.com/openid/id/(\d{17})$", RegexOptions.CultureInvariant);

        public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);

        // Anonymous callers can start logins, so bound how many pending states we hold.
        private const int MaxPendingStates = 1000;

        private readonly IUserAccountRepository _users;
        private readonly ISettingsRepository _settings;
        private readonly Func<string, string> _postVerification;
        private readonly Func<DateTime> _utcNow;

        private readonly ConcurrentDictionary<string, PendingState> _states = new ConcurrentDictionary<string, PendingState>();
        private readonly ConcurrentDictionary<string, PendingCode> _codes = new ConcurrentDictionary<string, PendingCode>();
        private readonly ConcurrentDictionary<string, DateTime> _usedNonces = new ConcurrentDictionary<string, DateTime>();

        public SteamLoginService(IUserAccountRepository users, ISettingsRepository settings)
            : this(users, settings, PostToSteam, () => DateTime.UtcNow)
        {
        }

        /// <summary>
        /// Test-friendly constructor: <paramref name="postVerification"/> receives the
        /// form-encoded check_authentication body and returns Steam's response text.
        /// </summary>
        public SteamLoginService(IUserAccountRepository users, ISettingsRepository settings,
            Func<string, string> postVerification, Func<DateTime> utcNow)
        {
            _users = users;
            _settings = settings;
            _postVerification = postVerification;
            _utcNow = utcNow;
        }

        // ---- Settings ----

        public SteamLoginSettings GetSettings()
        {
            try
            {
                var json = _settings.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                    return JsonConvert.DeserializeObject<SteamLoginSettings>(json) ?? new SteamLoginSettings();
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Could not read Steam login settings: {ex.Message}");
            }
            return new SteamLoginSettings();
        }

        /// <summary>True only when an admin turned it on AND a usable public URL is saved.</summary>
        public bool IsEnabled()
        {
            var s = GetSettings();
            return s.Enabled && TryNormalizePublicUrl(s.PublicUrl, out _);
        }

        /// <summary>
        /// Validates and saves settings. Throws <see cref="ArgumentException"/> when
        /// enabling without a valid public URL.
        /// </summary>
        public SteamLoginSettings SaveSettings(SteamLoginSettings input)
        {
            var publicUrl = input?.PublicUrl?.Trim();
            string normalized = null;
            if (!string.IsNullOrEmpty(publicUrl) && !TryNormalizePublicUrl(publicUrl, out normalized))
                throw new ArgumentException("Public URL must be an absolute http:// or https:// address with no query or #fragment.");

            if (input?.Enabled == true && normalized == null)
                throw new ArgumentException("Set the panel's public URL before enabling Steam sign-in.");

            var saved = new SteamLoginSettings { Enabled = input?.Enabled == true, PublicUrl = normalized };
            _settings.Set(SettingsKey, JsonConvert.SerializeObject(saved));
            return saved;
        }

        /// <summary>
        /// Accepts an absolute http(s) URL with no query or fragment, and returns it
        /// without a trailing slash (so paths can be appended).
        /// </summary>
        public static bool TryNormalizePublicUrl(string input, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri))
                return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;
            if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                return false;

            normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return true;
        }

        // ---- Starting the round trip ----

        /// <summary>Starts a Steam sign-in. Returns null when Steam login is off or we're saturated.</summary>
        public SteamAuthStart BeginLogin() => Begin(SteamAuthPurpose.Login, null);

        /// <summary>Starts linking Steam to <paramref name="userId"/>. Returns null when Steam login is off.</summary>
        public SteamAuthStart BeginLink(int userId) => Begin(SteamAuthPurpose.Link, userId);

        private SteamAuthStart Begin(SteamAuthPurpose purpose, int? userId)
        {
            var settings = GetSettings();
            if (!settings.Enabled || !TryNormalizePublicUrl(settings.PublicUrl, out var baseUrl))
                return null;

            Prune();
            if (_states.Count >= MaxPendingStates)
            {
                Log.Warning("[KitsuneCommand] Steam login: too many pending sign-ins; refusing a new one.");
                return null;
            }

            var state = NewToken();
            var returnTo = baseUrl + CallbackPath + "?state=" + state;
            _states[state] = new PendingState
            {
                Purpose = purpose,
                UserId = userId,
                BaseUrl = baseUrl,
                ReturnTo = returnTo,
                ExpiresUtc = _utcNow() + StateLifetime
            };

            return new SteamAuthStart
            {
                RedirectUrl = BuildSteamUrl(returnTo, baseUrl + "/"),
                State = state,
                SecureCookie = baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            };
        }

        public static string BuildSteamUrl(string returnTo, string realm)
        {
            var query = new[]
            {
                ("openid.ns", OpenIdNs),
                ("openid.mode", "checkid_setup"),
                ("openid.return_to", returnTo),
                ("openid.realm", realm),
                ("openid.identity", IdentifierSelect),
                ("openid.claimed_id", IdentifierSelect),
            };
            return SteamOpenIdEndpoint + "?" + string.Join("&",
                query.Select(p => Uri.EscapeDataString(p.Item1) + "=" + Uri.EscapeDataString(p.Item2)));
        }

        // ---- Callback ----

        /// <summary>
        /// Completes the round trip and returns where to send the browser next. Never
        /// throws for bad input: every failure becomes a redirect carrying steam_error.
        /// </summary>
        public string HandleCallback(NameValueCollection query, string stateCookie)
        {
            var state = query?["state"];
            if (string.IsNullOrEmpty(state) || !_states.TryRemove(state, out var pending))
                return LoginRedirect(GetSettings().PublicUrl, "error=expired");

            var failTarget = pending.Purpose == SteamAuthPurpose.Link
                ? (Func<string, string>)(code => SettingsRedirect(pending.BaseUrl, "error=" + code))
                : code => LoginRedirect(pending.BaseUrl, "error=" + code);

            if (pending.ExpiresUtc < _utcNow())
                return failTarget("expired");
            if (!IsEnabled())
                return failTarget("disabled");
            if (!string.Equals(stateCookie, state, StringComparison.Ordinal))
                return failTarget("session_mismatch");
            if (query["openid.mode"] == "cancel")
                return failTarget("cancelled");

            if (!TryValidateAssertion(query, pending.ReturnTo, out var steamId, out var reason))
            {
                Log.Warning($"[KitsuneCommand] Steam login: rejected assertion ({reason}).");
                return failTarget("invalid");
            }

            var nonce = query["openid.response_nonce"];
            if (!_usedNonces.TryAdd(nonce, _utcNow() + StateLifetime))
            {
                Log.Warning("[KitsuneCommand] Steam login: rejected a reused response nonce.");
                return failTarget("invalid");
            }

            try
            {
                var response = _postVerification(BuildVerificationBody(query));
                if (!IsValidVerificationResponse(response))
                {
                    Log.Warning("[KitsuneCommand] Steam login: Steam did not confirm the assertion.");
                    return failTarget("invalid");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Steam login: could not reach Steam to verify: {ex.Message}");
                return failTarget("steam_unreachable");
            }

            return pending.Purpose == SteamAuthPurpose.Link
                ? CompleteLink(pending, steamId)
                : CompleteLogin(pending, steamId);
        }

        private string CompleteLogin(PendingState pending, string steamId)
        {
            var account = _users.GetBySteamId(steamId);
            if (account == null)
                return LoginRedirect(pending.BaseUrl, "error=not_linked");

            var code = NewToken();
            _codes[code] = new PendingCode { UserId = account.Id, ExpiresUtc = _utcNow() + CodeLifetime };
            return LoginRedirect(pending.BaseUrl, "code=" + code);
        }

        private string CompleteLink(PendingState pending, string steamId)
        {
            var user = pending.UserId.HasValue ? _users.GetById(pending.UserId.Value) : null;
            if (user == null || !user.IsActive)
                return SettingsRedirect(pending.BaseUrl, "error=invalid");

            var holder = _users.GetBySteamId(steamId);
            if (holder != null && holder.Id != user.Id)
                return SettingsRedirect(pending.BaseUrl, "error=already_linked");

            try
            {
                _users.SetSteamId(user.Id, steamId);
            }
            catch (Exception ex)
            {
                // Unique index: the SteamID belongs to a deactivated account
                // (GetBySteamId only sees active ones).
                Log.Warning($"[KitsuneCommand] Steam login: could not link {steamId} to '{user.Username}': {ex.Message}");
                return SettingsRedirect(pending.BaseUrl, "error=already_linked");
            }

            Log.Out($"[KitsuneCommand] Steam account {steamId} linked to panel user '{user.Username}'.");
            return SettingsRedirect(pending.BaseUrl, "linked=1");
        }

        /// <summary>
        /// Trades a single-use code from the login redirect for the account it was
        /// issued to. Returns null if the code is unknown, expired, or the account was
        /// deactivated or unlinked in the meantime.
        /// </summary>
        public UserAccount RedeemCode(string code)
        {
            if (string.IsNullOrEmpty(code) || !_codes.TryRemove(code, out var pending))
                return null;
            if (pending.ExpiresUtc < _utcNow())
                return null;

            var account = _users.GetById(pending.UserId);
            if (account == null || !account.IsActive || string.IsNullOrEmpty(account.SteamId))
                return null;

            _users.UpdateLastLogin(account.Username);
            return account;
        }

        // ---- OpenID details ----

        /// <summary>
        /// Checks the fields of a positive assertion before we spend a round trip on
        /// asking Steam about it. Steam's own check_authentication covers the signature.
        /// </summary>
        public static bool TryValidateAssertion(NameValueCollection q, string expectedReturnTo, out string steamId, out string reason)
        {
            steamId = null;
            reason = null;

            if (q["openid.mode"] != "id_res") { reason = "mode"; return false; }
            if (q["openid.ns"] != OpenIdNs) { reason = "ns"; return false; }
            if (q["openid.op_endpoint"] != SteamOpenIdEndpoint) { reason = "op_endpoint"; return false; }
            if (q["openid.return_to"] != expectedReturnTo) { reason = "return_to"; return false; }
            if (string.IsNullOrEmpty(q["openid.response_nonce"])) { reason = "nonce"; return false; }
            if (string.IsNullOrEmpty(q["openid.sig"]) || string.IsNullOrEmpty(q["openid.assoc_handle"])) { reason = "signature"; return false; }

            var claimedId = q["openid.claimed_id"];
            if (claimedId == null || claimedId != q["openid.identity"]) { reason = "identity"; return false; }

            var match = ClaimedIdPattern.Match(claimedId);
            if (!match.Success) { reason = "claimed_id"; return false; }

            // Everything we rely on must be covered by Steam's signature.
            var signed = new HashSet<string>((q["openid.signed"] ?? "").Split(','), StringComparer.Ordinal);
            foreach (var field in new[] { "op_endpoint", "claimed_id", "identity", "return_to", "response_nonce", "assoc_handle" })
            {
                if (!signed.Contains(field)) { reason = "signed:" + field; return false; }
            }

            steamId = match.Groups[1].Value;
            return true;
        }

        /// <summary>Echoes every openid.* field back with mode=check_authentication.</summary>
        public static string BuildVerificationBody(NameValueCollection q)
        {
            var parts = new List<string>();
            foreach (string key in q.AllKeys)
            {
                if (key == null || !key.StartsWith("openid.", StringComparison.Ordinal))
                    continue;
                var value = key == "openid.mode" ? "check_authentication" : q[key];
                parts.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value ?? ""));
            }
            return string.Join("&", parts);
        }

        /// <summary>Steam answers in key-value form: "ns:...\nis_valid:true\n".</summary>
        public static bool IsValidVerificationResponse(string response)
        {
            if (string.IsNullOrEmpty(response))
                return false;
            return response.Split('\n').Any(line => line.Trim() == "is_valid:true");
        }

        /// <summary>
        /// Parses a raw query string ourselves rather than trusting the listener's
        /// QueryString: Steam's signature values contain '+' and '/', and an assertion
        /// has to be echoed back byte-for-byte to verify.
        /// </summary>
        public static NameValueCollection ParseQuery(string rawQuery)
        {
            var result = new NameValueCollection(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(rawQuery))
                return result;

            foreach (var pair in rawQuery.TrimStart('?').Split('&'))
            {
                if (pair.Length == 0)
                    continue;
                var eq = pair.IndexOf('=');
                var key = eq < 0 ? pair : pair.Substring(0, eq);
                var value = eq < 0 ? "" : pair.Substring(eq + 1);
                result[Decode(key)] = Decode(value);
            }
            return result;
        }

        private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        private static string PostToSteam(string body)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(SteamOpenIdEndpoint);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;

            var bytes = Encoding.UTF8.GetBytes(body);
            request.ContentLength = bytes.Length;
            using (var stream = request.GetRequestStream())
                stream.Write(bytes, 0, bytes.Length);

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
                return reader.ReadToEnd();
        }

        // ---- Helpers ----

        // The SPA uses hash routing, so the result rides in the fragment: it never
        // reaches server logs or a Referer header.
        private static string LoginRedirect(string baseUrl, string steamParam) =>
            (baseUrl ?? "") + "/#/login?steam_" + steamParam;

        private static string SettingsRedirect(string baseUrl, string steamParam) =>
            (baseUrl ?? "") + "/#/settings?steam_" + steamParam;

        private static string NewToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private void Prune()
        {
            var now = _utcNow();
            foreach (var kvp in _states.Where(k => k.Value.ExpiresUtc < now).ToList())
                _states.TryRemove(kvp.Key, out _);
            foreach (var kvp in _codes.Where(k => k.Value.ExpiresUtc < now).ToList())
                _codes.TryRemove(kvp.Key, out _);
            foreach (var kvp in _usedNonces.Where(k => k.Value < now).ToList())
                _usedNonces.TryRemove(kvp.Key, out _);
        }

        private class PendingState
        {
            public SteamAuthPurpose Purpose { get; set; }
            public int? UserId { get; set; }
            public string BaseUrl { get; set; }
            public string ReturnTo { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private class PendingCode
        {
            public int UserId { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }
    }
}
