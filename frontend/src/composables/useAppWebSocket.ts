import { useWebSocket } from './useWebSocket'
import { usePlayersStore } from '@/stores/players'
import { useServerStore } from '@/stores/server'
import { useChatStore } from '@/stores/chat'
import { useEconomyStore } from '@/stores/economy'
import { getOnlinePlayers } from '@/api/players'
import type { PlayerPositionData, PointsUpdateEvent } from '@/types'

let initialized = false

export function useAppWebSocket() {
  const ws = useWebSocket()
  const playersStore = usePlayersStore()
  const serverStore = useServerStore()
  const chatStore = useChatStore()
  const economyStore = useEconomyStore()

  function init() {
    if (initialized) return
    initialized = true

    // Fetch initial player list
    getOnlinePlayers()
      .then((players) => playersStore.setPlayers(players))
      .catch(() => {})

    // Wire up WebSocket events to stores
    ws.on<{ playerId: string; playerName: string; entityId: number }>('PlayerLogin', (data) => {
      playersStore.addPlayer({
        playerId: data.playerId,
        entityId: data.entityId,
        playerName: data.playerName,
        platformId: '',
        positionX: 0,
        positionY: 0,
        positionZ: 0,
        level: 0,
        health: 100,
        stamina: 100,
        zombieKills: 0,
        playerKills: 0,
        deaths: 0,
        totalPlayTime: 0,
        ip: '',
        isOnline: true,
        score: 0,
        lastLogin: 0,
        isAdmin: false,
      })
      serverStore.addActivity('login', `${data.playerName} joined the server`)
    })

    ws.on<{ entityId: number; playerName: string }>('PlayerDisconnected', (data) => {
      playersStore.removePlayer(data.entityId)
      serverStore.addActivity('logout', `${data.playerName} left the server`)
    })

    ws.on<{ players: PlayerPositionData[] }>('PlayersPositionUpdate', (data) => {
      playersStore.updatePositions(data.players)
    })

    ws.on<{ day: number; hour: number; minute: number; isBloodMoon: boolean }>('SkyChanged', (data) => {
      serverStore.updateGameTime(data)
    })

    ws.on<{ senderName: string; message: string; chatType?: number; playerId?: string; entityId?: number }>('ChatMessage', (data) => {
      serverStore.addActivity('chat', `${data.senderName}: ${data.message}`)
      chatStore.addRealtimeMessage({
        id: Date.now(), // Temporary id for real-time messages
        createdAt: new Date().toISOString(),
        playerId: data.playerId ?? '',
        entityId: data.entityId ?? 0,
        senderName: data.senderName,
        chatType: data.chatType ?? 0,
        message: data.message,
      })
    })

    ws.on<PointsUpdateEvent>('PointsUpdate', (data) => {
      economyStore.handlePointsUpdate(data)
    })

    ws.on<{ deadEntityName: string; killerName: string }>('EntityKilled', (data) => {
      if (data.killerName && data.deadEntityName) {
        serverStore.addActivity('kill', `${data.killerName} killed ${data.deadEntityName}`)
      }
    })

    ws.connect()
  }

  function destroy() {
    initialized = false
    ws.disconnect()
  }

  return { ...ws, init, destroy }
}
