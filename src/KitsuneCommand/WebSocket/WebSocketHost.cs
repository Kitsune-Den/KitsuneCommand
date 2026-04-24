using Autofac;
using KitsuneCommand.Configuration;
using WebSocketSharp.Server;

namespace KitsuneCommand.WebSocket
{
    /// <summary>
    /// Manages the WebSocketSharp server lifecycle.
    /// </summary>
    public class WebSocketHost
    {
        private readonly AppSettings _settings;
        private readonly IContainer _container;
        private WebSocketServer _server;

        public WebSocketHost(AppSettings settings, IContainer container)
        {
            _settings = settings;
            _container = container;
        }

        public void Start()
        {
            try
            {
                _server = new WebSocketServer(_settings.WebSocketPort);
                _server.KeepClean = false; // Don't auto-close idle connections
                _server.WaitTime = TimeSpan.FromSeconds(30);
                // Endpoint path is "/socket" (not "/ws") because Cloudflare's managed
                // WAF rules reject WebSocket Upgrade requests on any path starting with
                // "ws" (including /ws, /wss, /ws-whatever) with HTTP 400 at the edge.
                // Using /socket keeps the upgrade flowing through the tunnel untouched.
                _server.AddWebSocketService<TelnetBehavior>("/socket");
                _server.Start();

                // Initialize the broadcaster
                var eventBus = _container.Resolve<Core.ModEventBus>();
                EventBroadcaster.Initialize(_server, eventBus);

                Log.Out($"[KitsuneCommand] WebSocket server listening on port {_settings.WebSocketPort}");
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Failed to start WebSocket server: {ex.Message}");
                Log.Exception(ex);
            }
        }

        public void Stop()
        {
            try
            {
                _server?.Stop();
                _server = null;
                Log.Out("[KitsuneCommand] WebSocket server stopped.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Error stopping WebSocket server: {ex.Message}");
            }
        }
    }
}
