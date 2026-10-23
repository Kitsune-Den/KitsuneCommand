import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockPut = vi.fn()
const mockDelete = vi.fn()

vi.mock('@/api/client', () => ({
  default: {
    get: (...args: any[]) => mockGet(...args),
    post: (...args: any[]) => mockPost(...args),
    put: (...args: any[]) => mockPut(...args),
    delete: (...args: any[]) => mockDelete(...args),
  },
}))

import {
  steamLoginUrl,
  getSteamLoginEnabled,
  exchangeSteamCode,
  getSteamAccount,
  startSteamLink,
  unlinkSteam,
  getSteamLoginSettings,
  updateSteamLoginSettings,
} from '@/api/auth'
import { unlinkUserSteam } from '@/api/users'

describe('Steam sign-in API', () => {
  const originalFetch = globalThis.fetch

  beforeEach(() => {
    mockGet.mockReset()
    mockPost.mockReset()
    mockPut.mockReset()
    mockDelete.mockReset()
  })

  afterEach(() => {
    globalThis.fetch = originalFetch
  })

  it('points the Steam button at the login listener', () => {
    expect(steamLoginUrl).toBe('/api/auth/steam/login')
  })

  it('getSteamLoginEnabled reads the status endpoint', async () => {
    mockGet.mockResolvedValue({ data: { data: { enabled: true } } })
    expect(await getSteamLoginEnabled()).toBe(true)
    expect(mockGet).toHaveBeenCalledWith('/api/auth/steam/status')
  })

  it('getSteamLoginEnabled treats any failure as off', async () => {
    mockGet.mockRejectedValue(new Error('game not ready'))
    expect(await getSteamLoginEnabled()).toBe(false)

    mockGet.mockResolvedValue({ data: {} })
    expect(await getSteamLoginEnabled()).toBe(false)
  })

  it('exchangeSteamCode posts the code and returns the token response', async () => {
    const token = { access_token: 't', token_type: 'bearer', expires_in: 60, username: 'a', role: 'admin', display_name: 'A' }
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve(token) })
    globalThis.fetch = fetchMock as any

    expect(await exchangeSteamCode('abc')).toEqual(token)
    expect(fetchMock).toHaveBeenCalledWith('/api/auth/steam/exchange', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ code: 'abc' }),
    }))
  })

  it('exchangeSteamCode rejects with the server error in axios shape', async () => {
    const err = { error: 'invalid_grant', error_description: 'Steam sign-in expired. Try again.' }
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: false, status: 400, json: () => Promise.resolve(err) }) as any

    await expect(exchangeSteamCode('stale')).rejects.toEqual({ response: { data: err, status: 400 } })
  })

  it('getSteamAccount unwraps the response envelope', async () => {
    mockGet.mockResolvedValue({ data: { data: { enabled: true, linked: true, steamId: '76561197960287930' } } })
    expect(await getSteamAccount()).toEqual({ enabled: true, linked: true, steamId: '76561197960287930' })
    expect(mockGet).toHaveBeenCalledWith('/api/auth/steam/account')
  })

  it('startSteamLink returns the Steam URL from the listener', async () => {
    mockPost.mockResolvedValue({ data: { url: 'https://steamcommunity.com/openid/login?x=1' } })
    expect(await startSteamLink()).toBe('https://steamcommunity.com/openid/login?x=1')
    expect(mockPost).toHaveBeenCalledWith('/api/auth/steam/link')
  })

  it('unlinkSteam posts to unlink', async () => {
    mockPost.mockResolvedValue({ data: {} })
    await unlinkSteam()
    expect(mockPost).toHaveBeenCalledWith('/api/auth/steam/unlink')
  })

  it('reads and saves the admin settings', async () => {
    const settings = { enabled: true, publicUrl: 'https://panel.example.com' }
    mockGet.mockResolvedValue({ data: { data: settings } })
    mockPut.mockResolvedValue({ data: { data: settings } })

    expect(await getSteamLoginSettings()).toEqual(settings)
    expect(mockGet).toHaveBeenCalledWith('/api/auth/steam/settings')

    expect(await updateSteamLoginSettings(settings)).toEqual(settings)
    expect(mockPut).toHaveBeenCalledWith('/api/auth/steam/settings', settings)
  })

  it("unlinkUserSteam deletes another user's link", async () => {
    mockDelete.mockResolvedValue({ data: {} })
    await unlinkUserSteam(7)
    expect(mockDelete).toHaveBeenCalledWith('/api/users/7/steam')
  })
})
