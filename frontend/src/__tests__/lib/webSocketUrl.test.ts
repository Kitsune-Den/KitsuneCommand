import { describe, it, expect, afterEach } from 'vitest'
import { webSocketUrl } from '@/lib/webSocketUrl'

function at(href: string): Location {
  return new URL(href) as unknown as Location
}

function setPortMeta(panel: string, ws: string) {
  document.head.innerHTML =
    `<meta name="kc-panel-port" content="${panel}">` + `<meta name="kc-ws-port" content="${ws}">`
}

describe('webSocketUrl', () => {
  afterEach(() => {
    document.head.innerHTML = ''
  })

  it('uses the stock sibling port when opened on 8890 with no meta (dev server, older KC)', () => {
    expect(webSocketUrl(at('http://10.0.0.5:8890/dashboard'), 'tok')).toBe(
      'ws://10.0.0.5:8889/kctunnel?token=tok',
    )
  })

  it('stays same-origin behind a reverse proxy', () => {
    expect(webSocketUrl(at('https://panel.example.com/'), 'tok')).toBe(
      'wss://panel.example.com/kctunnel?token=tok',
    )
  })

  it('follows the ports the panel listener injected', () => {
    setPortMeta('9890', '9889')
    expect(webSocketUrl(at('http://10.0.0.5:9890/'), 'tok')).toBe(
      'ws://10.0.0.5:9889/kctunnel?token=tok',
    )
  })

  it('treats a page on another port as proxied once the real panel port is known', () => {
    setPortMeta('9890', '9889')
    expect(webSocketUrl(at('http://10.0.0.5:8890/'), 'tok')).toBe(
      'ws://10.0.0.5:8890/kctunnel?token=tok',
    )
  })
})
