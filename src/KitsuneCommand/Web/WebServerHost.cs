using System.Net;
using System.Threading;
using Autofac;
using KitsuneCommand.Configuration;
using KitsuneCommand.Web.Auth;
using Microsoft.Owin.Hosting;

namespace KitsuneCommand.Web
{
    /// <summary>
    /// Manages the OWIN self-hosted HTTP server lifecycle.
    /// </summary>
    public class WebServerHost
    {
        private readonly AppSettings _settings;
        private readonly IContainer _container;
        private IDisposable _webApp;
        private HttpListener _loginListener;
        private string _proxyBaseUrl;
        private Thread _loginThread;
        private volatile bool _running;

        public WebServerHost(AppSettings settings, IContainer container)
        {
            _settings = settings;
            _container = container;
        }

        public void Start()
        {
            try
            {
                var startup = new OwinStartup(_settings, _container);
                _webApp = WebApp.Start(_settings.WebUrl, startup.Configuration);
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Failed to start web server on {_settings.WebUrl}: {ex.Message}");
                Log.Exception(ex);
                throw;
            }

            // Start the standalone frontend + login listener on WebPanelPort
            try
            {
                _proxyBaseUrl = WebPorts.ProxyBaseUrl(_settings.WebUrl);
                var loginPort = _settings.WebPanelPort;
                var apiPort = WebPorts.PortFromUrl(_settings.WebUrl);
                if (loginPort == apiPort || loginPort == _settings.WebSocketPort)
                    Log.Warning($"[KitsuneCommand] WebPanelPort {loginPort} is the same as the API port ({apiPort}) or WebSocketPort ({_settings.WebSocketPort}); give each its own port.");

                _loginListener = new HttpListener();
                _loginListener.Prefixes.Add($"http://*:{loginPort}/");
                _loginListener.Start();
                _running = true;

                _loginThread = new Thread(LoginLoop) { IsBackground = true, Name = "KitsuneCommand-LoginListener" };
                _loginThread.Start();

                Log.Out($"[KitsuneCommand] Frontend + login endpoint listening on port {loginPort}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to start login listener: {ex.Message}");
            }
        }

        private void LoginLoop()
        {
            while (_running && _loginListener.IsListening)
            {
                try
                {
                    var ctx = _loginListener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch (Exception ex)
                {
                    if (_running) Log.Warning($"[KitsuneCommand] Login listener error: {ex.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath;

                // CORS headers for all responses
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                ctx.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
                ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";

                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                    return;
                }

                // Login endpoint
                if (path.TrimEnd('/') == "/api/auth/login" && ctx.Request.HttpMethod == "POST")
                {
                    HandleLogin(ctx);
                    return;
                }

                // Sign in through Steam. Handled here rather than in OWIN because these
                // routes redirect and set a cookie, and ProxyToOwin forwards neither.
                if (path.StartsWith("/api/auth/steam/") && TryHandleSteam(ctx, path.TrimEnd('/')))
                    return;

                // Proxy API calls to the OWIN API on WebUrl's port
                if (path.StartsWith("/api/") || path == "/token")
                {
                    ProxyToOwin(ctx);
                    return;
                }

                // Static file serving
                HandleStaticFile(ctx, path);
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Request handler error: {ex.Message}");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        private void HandleStaticFile(HttpListenerContext ctx, string path)
        {
            var webRoot = Path.Combine(Core.ModEntry.ModPath, "wwwroot");

            // Default to index.html for SPA routes
            if (path == "/" || !path.Contains("."))
                path = "/index.html";

            var filePath = Path.Combine(webRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(filePath))
            {
                // SPA fallback
                filePath = Path.Combine(webRoot, "index.html");
            }

            if (!File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            ctx.Response.ContentType = GetMimeType(ext);

            var bytes = Path.GetFileName(filePath).Equals("index.html", StringComparison.OrdinalIgnoreCase)
                ? System.Text.Encoding.UTF8.GetBytes(WebPorts.InjectPortMeta(
                    File.ReadAllText(filePath), _settings.WebPanelPort, _settings.WebSocketPort))
                : File.ReadAllBytes(filePath);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private void ProxyToOwin(HttpListenerContext ctx)
        {
            try
            {
                var targetUrl = _proxyBaseUrl + ctx.Request.Url.PathAndQuery;
                var webReq = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(targetUrl);
                webReq.Method = ctx.Request.HttpMethod;
                webReq.ContentType = ctx.Request.ContentType;
                webReq.Timeout = 30000;

                // Forward auth header
                var auth = ctx.Request.Headers["Authorization"];
                if (!string.IsNullOrEmpty(auth))
                    webReq.Headers["Authorization"] = auth;

                // Forward request body for POST/PUT
                if (ctx.Request.HttpMethod == "POST" || ctx.Request.HttpMethod == "PUT")
                {
                    using (var reqStream = webReq.GetRequestStream())
                        ctx.Request.InputStream.CopyTo(reqStream);
                }

                using (var webResp = (System.Net.HttpWebResponse)webReq.GetResponse())
                {
                    ctx.Response.StatusCode = (int)webResp.StatusCode;
                    ctx.Response.ContentType = webResp.ContentType;
                    using (var respStream = webResp.GetResponseStream())
                        respStream.CopyTo(ctx.Response.OutputStream);
                }
            }
            catch (System.Net.WebException wex) when (wex.Response is System.Net.HttpWebResponse errResp)
            {
                ctx.Response.StatusCode = (int)errResp.StatusCode;
                ctx.Response.ContentType = errResp.ContentType;
                using (var respStream = errResp.GetResponseStream())
                    respStream.CopyTo(ctx.Response.OutputStream);
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Proxy error: {ex.Message}");
                WriteJson(ctx, 502, new { error = "proxy_error", error_description = ex.Message });
                return;
            }
            ctx.Response.Close();
        }

        private void HandleLogin(HttpListenerContext ctx)
        {
            ctx.Response.ContentType = "application/json";
            try
            {
                string body;
                using (var reader = new System.IO.StreamReader(ctx.Request.InputStream))
                    body = reader.ReadToEnd();

                var loginReq = JsonConvert.DeserializeObject<LoginPayload>(body);
                // Normalize to match how accounts are stored: UserController.Create lowercases +
                // trims the username, and SQLite compares TEXT case-sensitively. Without this, a
                // username created/typed as "KCAdmin" is stored "kcadmin" and never matches at login.
                string username = loginReq?.username?.Trim().ToLowerInvariant();
                string password = loginReq?.password;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    WriteJson(ctx, 400, new { error = "invalid_request", error_description = "Username and password are required." });
                    return;
                }

                var userRepo = _container.Resolve<Data.Repositories.IUserAccountRepository>();
                var account = userRepo.GetByUsername(username);
                if (account == null)
                {
                    WriteJson(ctx, 400, new { error = "invalid_grant", error_description = "Invalid username or password." });
                    return;
                }

                bool verified = Auth.PasswordHasher.Verify(password, account.PasswordHash);

                if (!verified)
                {
                    // Emergency password reset mechanism: if BCrypt verification fails (e.g. hash
                    // was corrupted or the Mono runtime mangled it), the server admin can place a
                    // plaintext RESET_PASSWORD.txt in the KitsuneCommand data folder — same
                    // world-agnostic location as the DB (see
                    // ConfigManager.ResolveWorldAgnosticDataDir). When the submitted password
                    // matches that file's contents, the password is re-hashed with BCrypt and
                    // the reset file is deleted, restoring normal login.
                    try
                    {
                        var resetFile = Path.Combine(ConfigManager.ResolveWorldAgnosticDataDir(), "RESET_PASSWORD.txt");
                        if (File.Exists(resetFile))
                        {
                            var resetPassword = File.ReadAllText(resetFile).Trim();
                            if (password == resetPassword)
                            {
                                var newHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
                                userRepo.UpdatePassword(account.Id, newHash);
                                File.Delete(resetFile);
                                Log.Out("[KitsuneCommand] Password reset via RESET_PASSWORD.txt — file deleted.");
                                verified = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[KitsuneCommand] Error checking reset file: {ex.Message}");
                    }
                }

                if (!verified)
                {
                    WriteJson(ctx, 400, new { error = "invalid_grant", error_description = "Invalid username or password." });
                    return;
                }

                userRepo.UpdateLastLogin(username);
                WriteTokenResponse(ctx, account);
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Login handler error: {ex.Message}");
                try { WriteJson(ctx, 500, new { error = "server_error", error_description = ex.Message }); }
                catch { }
            }
        }

        private void WriteTokenResponse(HttpListenerContext ctx, Data.Entities.UserAccount account)
        {
            var expiresIn = TimeSpan.FromMinutes(_settings.AccessTokenExpireMinutes);
            var token = TokenValidator.CreateToken(
                account.Username, account.Role,
                account.Id.ToString(), account.DisplayName ?? account.Username,
                expiresIn);

            WriteJson(ctx, 200, new
            {
                access_token = token,
                token_type = "bearer",
                expires_in = (int)expiresIn.TotalSeconds,
                username = account.Username,
                role = account.Role,
                display_name = account.DisplayName ?? account.Username
            });
        }

        /// <summary>
        /// The SteamLoginService routes that can't go through OWIN. Returns false for any
        /// other /api/auth/steam/* path (status, account, unlink, settings) so it falls
        /// through to the OWIN proxy.
        /// </summary>
        private bool TryHandleSteam(HttpListenerContext ctx, string path)
        {
            var method = ctx.Request.HttpMethod;
            if (path == "/api/auth/steam/login" && method == "GET")
            {
                HandleSteamStart(ctx, steam => steam.BeginLogin(), redirect: true);
                return true;
            }
            if (path == "/api/auth/steam/link" && method == "POST")
            {
                HandleSteamLinkStart(ctx);
                return true;
            }
            if (path == "/api/auth/steam/callback" && method == "GET")
            {
                HandleSteamCallback(ctx);
                return true;
            }
            if (path == "/api/auth/steam/exchange" && method == "POST")
            {
                HandleSteamExchange(ctx);
                return true;
            }
            return false;
        }

        private void HandleSteamStart(HttpListenerContext ctx, Func<SteamLoginService, SteamAuthStart> begin, bool redirect)
        {
            ctx.Response.Headers["Cache-Control"] = "no-store";
            var steam = _container.Resolve<SteamLoginService>();
            var start = begin(steam);

            if (start == null)
            {
                if (redirect)
                    Redirect(ctx, (steam.GetSettings().PublicUrl ?? "") + "/#/login?steam_error=disabled");
                else
                    WriteJson(ctx, 400, new { error = "steam_disabled", error_description = "Steam sign-in is not enabled." });
                return;
            }

            // Pins the round trip to this browser; the callback checks it. No Path, so it
            // defaults to /api/auth/steam as the browser sees it, sub-path proxies included.
            ctx.Response.AppendHeader("Set-Cookie",
                $"{SteamLoginService.StateCookieName}={start.State}; Max-Age={(int)SteamLoginService.StateLifetime.TotalSeconds}; HttpOnly; SameSite=Lax"
                + (start.SecureCookie ? "; Secure" : ""));

            if (redirect)
                Redirect(ctx, start.RedirectUrl);
            else
                WriteJson(ctx, 200, new { url = start.RedirectUrl });
        }

        private void HandleSteamLinkStart(HttpListenerContext ctx)
        {
            ctx.Response.ContentType = "application/json";
            var header = ctx.Request.Headers["Authorization"] ?? "";
            var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header.Substring(7).Trim() : null;

            var account = TokenValidator.ValidateToken(token, out var username, out _)
                ? _container.Resolve<Data.Repositories.IUserAccountRepository>().GetByUsername(username)
                : null;
            if (account == null)
            {
                WriteJson(ctx, 401, new { error = "unauthorized", error_description = "Sign in first." });
                return;
            }

            HandleSteamStart(ctx, steam => steam.BeginLink(account.Id), redirect: false);
        }

        private void HandleSteamCallback(HttpListenerContext ctx)
        {
            ctx.Response.Headers["Cache-Control"] = "no-store";
            var steam = _container.Resolve<SteamLoginService>();

            var rawUrl = ctx.Request.RawUrl ?? "";
            var q = rawUrl.IndexOf('?');
            var query = SteamLoginService.ParseQuery(q < 0 ? "" : rawUrl.Substring(q + 1));
            var cookie = ctx.Request.Cookies[SteamLoginService.StateCookieName]?.Value;

            var next = steam.HandleCallback(query, cookie);

            ctx.Response.AppendHeader("Set-Cookie", $"{SteamLoginService.StateCookieName}=; Max-Age=0; HttpOnly; SameSite=Lax");
            Redirect(ctx, next);
        }

        private void HandleSteamExchange(HttpListenerContext ctx)
        {
            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers["Cache-Control"] = "no-store";

            string body;
            using (var reader = new System.IO.StreamReader(ctx.Request.InputStream))
                body = reader.ReadToEnd();

            string code = null;
            try { code = JsonConvert.DeserializeObject<SteamExchangePayload>(body)?.code; }
            catch (JsonException) { }

            var account = _container.Resolve<SteamLoginService>().RedeemCode(code);
            if (account == null)
            {
                WriteJson(ctx, 400, new { error = "invalid_grant", error_description = "Steam sign-in expired. Try again." });
                return;
            }

            WriteTokenResponse(ctx, account);
        }

        private static void Redirect(HttpListenerContext ctx, string url)
        {
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = url;
            ctx.Response.Close();
        }

        private static string GetMimeType(string ext)
        {
            switch (ext)
            {
                case ".html": return "text/html";
                case ".js": return "application/javascript";
                case ".css": return "text/css";
                case ".json": return "application/json";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".svg": return "image/svg+xml";
                case ".ico": return "image/x-icon";
                case ".woff": case ".woff2": return "font/woff2";
                default: return "application/octet-stream";
            }
        }

        private static void WriteJson(HttpListenerContext ctx, int status, object data)
        {
            ctx.Response.StatusCode = status;
            var json = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));
            ctx.Response.OutputStream.Write(json, 0, json.Length);
            ctx.Response.Close();
        }

        private class SteamExchangePayload
        {
            public string code { get; set; }
        }

        private class LoginPayload
        {
            public string username { get; set; }
            public string password { get; set; }
        }

        public void Stop()
        {
            _running = false;
            try
            {
                _loginListener?.Stop();
                _loginListener?.Close();
            }
            catch { }

            try
            {
                _webApp?.Dispose();
                _webApp = null;
                Log.Out("[KitsuneCommand] Web server stopped.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Error stopping web server: {ex.Message}");
            }
        }
    }
}
