import { ref, onUnmounted } from 'vue'
import { useAuthStore } from '@/stores/auth'

export type ModEventType =
  | 'LogCallback'
  | 'GameAwake'
  | 'GameStartDone'
  | 'GameShutdown'
  | 'ChatMessage'
  | 'EntityKilled'
  | 'EntitySpawned'
  | 'PlayerDisconnected'
  | 'PlayerLogin'
  | 'PlayerSpawnedInWorld'
  | 'PlayerSpawning'
  | 'SavePlayerData'
  | 'SkyChanged'
  | 'PlayersPositionUpdate'
  | 'PointsUpdate'
  | 'BloodMoonVoteUpdate'
  | 'CommandResult'
  | 'Welcome'

interface WebSocketMessage<T = unknown> {
  eventType: ModEventType
  data: T
}

export function useWebSocket() {
  const ws = ref<WebSocket | null>(null)
  const isConnected = ref(false)
  const handlers = new Map<string, Set<(data: unknown) => void>>()
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null

  function connect() {
    const auth = useAuthStore()
    if (!auth.accessToken) return

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:'
    const host = window.location.hostname
    // Dev: page at http://host:8890 → WebSocket server is on sibling port 8889, explicit.
    // Prod: page behind reverse proxy (standard 80/443 or custom), same origin — the proxy
    // is responsible for routing /ws to the 8889 backend.
    const isDevDirectPort = window.location.port === '8890'
    const portSuffix = isDevDirectPort ? ':8889' : ''
    const url = `${protocol}//${host}${portSuffix}/ws?token=${auth.accessToken}`

    ws.value = new WebSocket(url)

    ws.value.onopen = () => {
      isConnected.value = true
      if (reconnectTimer) {
        clearTimeout(reconnectTimer)
        reconnectTimer = null
      }
    }

    ws.value.onclose = () => {
      isConnected.value = false
      // Auto-reconnect after 3 seconds
      reconnectTimer = setTimeout(() => connect(), 3000)
    }

    ws.value.onerror = () => {
      ws.value?.close()
    }

    ws.value.onmessage = (event) => {
      try {
        const msg: WebSocketMessage = JSON.parse(event.data)
        const eventHandlers = handlers.get(msg.eventType)
        if (eventHandlers) {
          eventHandlers.forEach((handler) => handler(msg.data))
        }
      } catch {
        // Ignore malformed messages
      }
    }
  }

  function on<T>(eventType: ModEventType, handler: (data: T) => void) {
    if (!handlers.has(eventType)) {
      handlers.set(eventType, new Set())
    }
    handlers.get(eventType)!.add(handler as (data: unknown) => void)
  }

  function off(eventType: ModEventType, handler: (data: unknown) => void) {
    handlers.get(eventType)?.delete(handler)
  }

  function send(data: string) {
    if (ws.value?.readyState === WebSocket.OPEN) {
      ws.value.send(data)
    }
  }

  function disconnect() {
    if (reconnectTimer) {
      clearTimeout(reconnectTimer)
      reconnectTimer = null
    }
    ws.value?.close()
    ws.value = null
    isConnected.value = false
  }

  onUnmounted(() => disconnect())

  return { connect, disconnect, on, off, send, isConnected }
}
