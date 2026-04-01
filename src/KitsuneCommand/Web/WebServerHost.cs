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

            // Start a standalone login listener on port+1
            try
            {
                var loginPort = 8890;
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

                // Proxy API calls to port 8888
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

            var bytes = File.ReadAllBytes(filePath);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private void ProxyToOwin(HttpListenerContext ctx)
        {
            try
            {
                var targetUrl = $"http://127.0.0.1:8888{ctx.Request.Url.PathAndQuery}";
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
                string username = loginReq?.username;
                string password = loginReq?.password;

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    WriteJson(ctx, 400, new { error = "invalid_request", error_description = "Username and password are required." });
                    return;
                }

                var authService = _container.Resolve<AuthService>();
                var account = authService.ValidateCredentials(username, password);
                if (account == null)
                {
                    WriteJson(ctx, 400, new { error = "invalid_grant", error_description = "Invalid username or password." });
                    return;
                }

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
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Login handler error: {ex.Message}");
                try { WriteJson(ctx, 500, new { error = "server_error", error_description = ex.Message }); }
                catch { }
            }
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
