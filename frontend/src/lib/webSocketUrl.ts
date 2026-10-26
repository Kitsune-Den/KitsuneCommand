// The panel listener (WebPanelPort, default 8890) injects the server's ports into
// index.html as <meta name="kc-panel-port"> / <meta name="kc-ws-port">. Without them
// (the Vite dev server, or an older KC build) fall back to the stock ports.
const DEFAULT_PANEL_PORT = '8890'
const DEFAULT_WS_PORT = '8889'

function metaContent(doc: Document, name: string): string | null {
  return doc.querySelector<HTMLMetaElement>(`meta[name="${name}"]`)?.content?.trim() || null
}

/**
 * Where the panel's WebSocket lives.
 *
 * Page opened straight on the panel port (http://host:8890): the WebSocket server
 * is a sibling port on the same host, so name it explicitly.
 * Page behind a reverse proxy (standard 80/443 or any other port): same origin;
 * the proxy is responsible for routing /kctunnel to the WebSocket backend.
 *
 * Path is /kctunnel. Cloudflare's managed WAF blocks an *expanding* list of path
 * name classes at the edge (HTTP 400 without ever reaching origin). So far: /ws*
 * (blocks WS Upgrade), /socket* (blocks any request), and /*events* (blocks any
 * request; /kcevents got us). /kctunnel has tested clean everywhere we've probed.
 */
export function webSocketUrl(location: Location, token: string, doc: Document = document): string {
  const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:'
  const panelPort = metaContent(doc, 'kc-panel-port') ?? DEFAULT_PANEL_PORT
  const wsPort = metaContent(doc, 'kc-ws-port') ?? DEFAULT_WS_PORT
  // location.host keeps a proxy's non-standard port; hostname alone would drop it.
  const origin = location.port === panelPort ? `${location.hostname}:${wsPort}` : location.host
  return `${protocol}//${origin}/kctunnel?token=${token}`
}
