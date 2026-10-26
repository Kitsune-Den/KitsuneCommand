namespace KitsuneCommand.Web
{
    /// <summary>
    /// Port plumbing between KC's three listeners: the OWIN API (bound from
    /// <c>WebUrl</c>), the frontend/login listener (<c>WebPanelPort</c>) and the
    /// WebSocket server (<c>WebSocketPort</c>). Pure so it can be tested outside the game.
    /// </summary>
    public static class WebPorts
    {
        /// <summary>
        /// The port in an OWIN bind URL such as "http://*:8888" or "http://+:9000/".
        /// System.Uri rejects the "*" and "+" wildcard hosts, so this reads the
        /// authority by hand. No explicit port means the scheme's default.
        /// </summary>
        public static int PortFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Web URL is empty.", nameof(url));

            var trimmed = url.Trim();
            var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd < 0)
                throw new ArgumentException($"Web URL '{url}' has no scheme (expected e.g. http://*:8888).", nameof(url));

            var scheme = trimmed.Substring(0, schemeEnd).ToLowerInvariant();
            var rest = trimmed.Substring(schemeEnd + 3);
            var slash = rest.IndexOf('/');
            var authority = slash >= 0 ? rest.Substring(0, slash) : rest;

            // IPv6 hosts are bracketed ("[::1]:8888"), so the port colon is the one after "]".
            var colon = authority.LastIndexOf(':');
            if (colon > authority.LastIndexOf(']'))
            {
                if (int.TryParse(authority.Substring(colon + 1), out var port) && port > 0 && port <= 65535)
                    return port;
                throw new ArgumentException($"Web URL '{url}' has an invalid port.", nameof(url));
            }

            if (scheme == "http") return 80;
            if (scheme == "https") return 443;
            throw new ArgumentException($"Web URL '{url}' has no port and an unknown scheme.", nameof(url));
        }

        /// <summary>
        /// Where the frontend listener forwards /api/ calls: the OWIN API on this machine.
        /// </summary>
        public static string ProxyBaseUrl(string webUrl)
        {
            var scheme = webUrl.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
            return $"{scheme}://127.0.0.1:{PortFromUrl(webUrl)}";
        }

        /// <summary>
        /// Tells the SPA which ports this server uses, so a page opened straight on
        /// the panel port knows where its WebSocket sibling is. Inserted before
        /// &lt;/head&gt;; HTML without a head is returned unchanged.
        /// </summary>
        public static string InjectPortMeta(string html, int panelPort, int webSocketPort)
        {
            const string headClose = "</head>";
            var at = html.IndexOf(headClose, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return html;

            var meta = $"<meta name=\"kc-panel-port\" content=\"{panelPort}\">"
                     + $"<meta name=\"kc-ws-port\" content=\"{webSocketPort}\">";
            return html.Insert(at, meta);
        }
    }
}
