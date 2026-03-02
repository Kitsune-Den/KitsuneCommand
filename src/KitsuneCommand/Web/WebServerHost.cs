using Autofac;
using KitsuneCommand.Configuration;
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
        }

        public void Stop()
        {
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
