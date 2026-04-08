import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { usePlayersStore } from '@/stores/players'

const mockPlayer = (overrides: Record<string, any> = {}) => ({
  playerId: 'player1',
  entityId: 100,
  playerName: 'Alice',
  platformId: 'Steam_123',
  isOnline: true,
  health: 100,
  stamina: 100,
  level: 10,
  zombieKills: 0,
  playerKills: 0,
  deaths: 0,
  totalPlayTime: 0,
  ip: '127.0.0.1',
  score: 0,
  lastLogin: 0,
  isAdmin: false,
  adminLevel: 0,
  positionX: 0,
  positionY: 0,
  positionZ: 0,
  ...overrides,
})

describe('Players Store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('should initialize empty', () => {
    const store = usePlayersStore()
    expect(store.playerList).toEqual([])
    expect(store.onlineCount).toBe(0)
  })

  it('setPlayers populates the player list', () => {
    const store = usePlayersStore()
    store.setPlayers([mockPlayer(), mockPlayer({ playerId: 'player2', entityId: 200, playerName: 'Bob' })])
    expect(store.playerList.length).toBe(2)
  })

  it('onlineCount counts only online players', () => {
    const store = usePlayersStore()
    store.setPlayers([
      mockPlayer({ isOnline: true }),
      mockPlayer({ playerId: 'p2', entityId: 200, isOnline: false }),
    ])
    expect(store.onlineCount).toBe(1)
  })

  it('addPlayer adds to the list', () => {
    const store = usePlayersStore()
    store.addPlayer(mockPlayer())
    expect(store.playerList.length).toBe(1)
    expect(store.getPlayer(100)?.playerName).toBe('Alice')
  })

  it('removePlayer removes by entityId', () => {
    const store = usePlayersStore()
    store.addPlayer(mockPlayer())
    store.removePlayer(100)
    expect(store.playerList.length).toBe(0)
  })

  it('updatePositions updates player coordinates', () => {
    const store = usePlayersStore()
    store.addPlayer(mockPlayer())
    store.updatePositions([{ entityId: 100, playerName: 'Alice', x: 10, y: 20, z: 30 }])
    const player = store.getPlayer(100)
    expect(player?.positionX).toBe(10)
    expect(player?.positionY).toBe(20)
    expect(player?.positionZ).toBe(30)
  })

  it('getPlayer returns undefined for unknown entityId', () => {
    const store = usePlayersStore()
    expect(store.getPlayer(999)).toBeUndefined()
  })

  it('metadata operations work correctly', () => {
    const store = usePlayersStore()
    store.setMetadata({ player1: { playerId: 'player1', nameColor: '#ff0000', customTag: null, notes: null } as any })
    expect(store.getMetadata('player1')?.nameColor).toBe('#ff0000')

    store.updateMetadataEntry('player1', { playerId: 'player1', nameColor: '#00ff00' } as any)
    expect(store.getMetadata('player1')?.nameColor).toBe('#00ff00')
  })
})
