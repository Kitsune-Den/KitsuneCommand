import { describe, it, expect, vi, beforeEach } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockPut = vi.fn()

vi.mock('@/api/client', () => ({
  default: {
    get: (...args: any[]) => mockGet(...args),
    post: (...args: any[]) => mockPost(...args),
    put: (...args: any[]) => mockPut(...args),
  },
}))

import {
  getOnlinePlayers,
  getKnownPlayers,
  getPlayer,
  getPlayerInventory,
  kickPlayer,
  banPlayer,
  giveItem,
  getAllPlayerMetadata,
  updatePlayerMetadata,
} from '@/api/players'

describe('Players API', () => {
  beforeEach(() => {
    mockGet.mockReset()
    mockPost.mockReset()
    mockPut.mockReset()
  })

  it('getOnlinePlayers calls GET /api/players', async () => {
    const players = [{ playerId: 'abc', playerName: 'Alice', isOnline: true }]
    mockGet.mockResolvedValue({ data: { data: players } })
    const result = await getOnlinePlayers()
    expect(mockGet).toHaveBeenCalledWith('/api/players')
    expect(result).toEqual(players)
  })

  it('getKnownPlayers calls GET /api/players/known with pagination', async () => {
    const response = { items: [], total: 0, pageIndex: 0, pageSize: 100 }
    mockGet.mockResolvedValue({ data: { data: response } })
    await getKnownPlayers(0, 50, 'search')
    expect(mockGet).toHaveBeenCalledWith('/api/players/known', {
      params: { pageIndex: 0, pageSize: 50, search: 'search' },
    })
  })

  it('getPlayer calls GET /api/players/{entityId}', async () => {
    mockGet.mockResolvedValue({ data: { data: { entityId: 42, playerName: 'Bob' } } })
    const result = await getPlayer(42)
    expect(mockGet).toHaveBeenCalledWith('/api/players/42')
    expect(result.playerName).toBe('Bob')
  })

  it('getPlayerInventory calls GET /api/players/{entityId}/inventory', async () => {
    mockGet.mockResolvedValue({ data: { data: { bag: [], belt: [] } } })
    await getPlayerInventory(42)
    expect(mockGet).toHaveBeenCalledWith('/api/players/42/inventory')
  })

  it('kickPlayer calls POST with reason', async () => {
    mockPost.mockResolvedValue({ data: {} })
    await kickPlayer(42, 'AFK')
    expect(mockPost).toHaveBeenCalledWith('/api/players/42/kick', { entityId: 42, reason: 'AFK' })
  })

  it('banPlayer calls POST with reason and duration', async () => {
    mockPost.mockResolvedValue({ data: {} })
    await banPlayer(42, 'cheating', '7d')
    expect(mockPost).toHaveBeenCalledWith('/api/players/42/ban', { entityId: 42, reason: 'cheating', duration: '7d' })
  })

  it('giveItem calls POST with item details', async () => {
    mockPost.mockResolvedValue({ data: {} })
    await giveItem(42, 'gunPistol', 1, 6)
    expect(mockPost).toHaveBeenCalledWith('/api/players/42/give', { entityId: 42, itemName: 'gunPistol', count: 1, quality: 6 })
  })

  it('getAllPlayerMetadata calls GET /api/players/metadata', async () => {
    mockGet.mockResolvedValue({ data: { data: { abc: { playerId: 'abc', nameColor: '#ff0000' } } } })
    const result = await getAllPlayerMetadata()
    expect(result.abc.nameColor).toBe('#ff0000')
  })

  it('getAllPlayerMetadata returns empty object on null data', async () => {
    mockGet.mockResolvedValue({ data: { data: null } })
    const result = await getAllPlayerMetadata()
    expect(result).toEqual({})
  })

  it('updatePlayerMetadata calls PUT with encoded playerId', async () => {
    mockPut.mockResolvedValue({ data: { data: { playerId: 'abc', nameColor: '#00ff00' } } })
    await updatePlayerMetadata('abc', { nameColor: '#00ff00' })
    expect(mockPut).toHaveBeenCalledWith('/api/players/metadata/abc', { nameColor: '#00ff00' })
  })
})
