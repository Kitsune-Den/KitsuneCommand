import { describe, it, expect, vi, beforeEach } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()

vi.mock('@/api/client', () => ({
  default: {
    get: (...args: any[]) => mockGet(...args),
    post: (...args: any[]) => mockPost(...args),
  },
}))

import { getServerInfo, getServerStats, saveWorld, shutdownServer } from '@/api/serverControl'

describe('Server Control API', () => {
  beforeEach(() => {
    mockGet.mockReset()
    mockPost.mockReset()
  })

  it('getServerInfo calls GET /api/server/info', async () => {
    const info = { serverName: 'Test Server', maxPlayers: 8 }
    mockGet.mockResolvedValue({ data: { data: info } })
    const result = await getServerInfo()
    expect(mockGet).toHaveBeenCalledWith('/api/server/info')
    expect(result.serverName).toBe('Test Server')
  })

  it('getServerStats calls GET /api/server/stats', async () => {
    const stats = { fps: 60, playerCount: 5, gcMemory: 1024 }
    mockGet.mockResolvedValue({ data: { data: stats } })
    const result = await getServerStats()
    expect(mockGet).toHaveBeenCalledWith('/api/server/stats')
    expect(result.fps).toBe(60)
  })

  it('saveWorld calls POST /api/server/save', async () => {
    mockPost.mockResolvedValue({ data: { data: { message: 'World saved' } } })
    const result = await saveWorld()
    expect(mockPost).toHaveBeenCalledWith('/api/server/save')
    expect(result).toBe('World saved')
  })

  it('shutdownServer calls POST with delay', async () => {
    mockPost.mockResolvedValue({ data: { message: 'Shutting down' } })
    const result = await shutdownServer(30)
    expect(mockPost).toHaveBeenCalledWith('/api/server/shutdown', { delaySeconds: 30 })
    expect(result).toBe('Shutting down')
  })

  it('shutdownServer defaults to 10 seconds', async () => {
    mockPost.mockResolvedValue({ data: { message: 'Shutting down' } })
    await shutdownServer()
    expect(mockPost).toHaveBeenCalledWith('/api/server/shutdown', { delaySeconds: 10 })
  })
})
