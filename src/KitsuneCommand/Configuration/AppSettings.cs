namespace KitsuneCommand.Configuration
{
    /// <summary>
    /// Application settings loaded from appsettings.json.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Internal API (OWIN) bind URL (e.g., "http://*:8888"). The panel listener
        /// proxies /api/ calls to this URL's port.
        /// </summary>
        public string WebUrl { get; set; } = "http://*:8888";

        /// <summary>
        /// Frontend + login listener port: the one you open in a browser or point a
        /// reverse proxy at.
        /// </summary>
        public int WebPanelPort { get; set; } = 8890;

        /// <summary>
        /// WebSocket server port.
        /// </summary>
        public int WebSocketPort { get; set; } = 8889;

        /// <summary>
        /// Path to the SQLite database file. Relative paths are resolved from the server root.
        /// </summary>
        public string DatabasePath { get; set; } = "KitsuneCommand.db";

        /// <summary>
        /// OAuth2 access token expiration time in minutes.
        /// </summary>
        public int AccessTokenExpireMinutes { get; set; } = 1440; // 24 hours

        /// <summary>
        /// OAuth2 refresh token expiration time in days.
        /// </summary>
        public int RefreshTokenExpireDays { get; set; } = 7;

        /// <summary>
        /// Name of the server settings XML file.
        /// </summary>
        public string ServerSettingsFileName { get; set; } = "serverconfig.xml";

        /// <summary>
        /// Enable CORS for development (allows requests from Vite dev server).
        /// </summary>
        public bool EnableCors { get; set; } = false;

        /// <summary>
        /// Allowed CORS origins (comma-separated).
        /// </summary>
        public string CorsOrigins { get; set; } = "http://localhost:5173";
    }
}
