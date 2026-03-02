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
    const port = 8889 // WebSocket port
    const url = `${protocol}//${host}:${port}/ws?token=${auth.accessToken}`

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
