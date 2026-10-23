import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'

// Mock the API module
const mockLogin = vi.fn()
const mockExchangeSteamCode = vi.fn()
vi.mock('@/api/auth', () => ({
  login: (...args: any[]) => mockLogin(...args),
  exchangeSteamCode: (...args: any[]) => mockExchangeSteamCode(...args),
}))

describe('Auth Store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    mockLogin.mockReset()
  })

  it('should start unauthenticated when no token in localStorage', () => {
    const auth = useAuthStore()
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.accessToken).toBeNull()
    expect(auth.username).toBeNull()
  })

  it('should restore token from localStorage on creation', () => {
    localStorage.setItem('kc_access_token', 'stored-token')
    localStorage.setItem('kc_username', 'stored-user')
    localStorage.setItem('kc_role', 'admin')

    // Need to recreate pinia so the store re-reads localStorage
    setActivePinia(createPinia())
    const auth = useAuthStore()

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.accessToken).toBe('stored-token')
    expect(auth.username).toBe('stored-user')
    expect(auth.role).toBe('admin')
  })

  it('setTokens should set state and persist to localStorage', () => {
    const auth = useAuthStore()

    auth.setTokens({
      access_token: 'new-token',
      token_type: 'bearer',
      expires_in: 86400,
      refresh_token: 'new-refresh',
      username: 'admin',
      display_name: 'Administrator',
      role: 'admin',
    })

    // Check state
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.accessToken).toBe('new-token')
    expect(auth.refreshToken).toBe('new-refresh')
    expect(auth.username).toBe('admin')
    expect(auth.displayName).toBe('Administrator')
    expect(auth.role).toBe('admin')

    // Check localStorage
    expect(localStorage.getItem('kc_access_token')).toBe('new-token')
    expect(localStorage.getItem('kc_refresh_token')).toBe('new-refresh')
    expect(localStorage.getItem('kc_username')).toBe('admin')
    expect(localStorage.getItem('kc_display_name')).toBe('Administrator')
    expect(localStorage.getItem('kc_role')).toBe('admin')
  })

  it('login should call API and set tokens', async () => {
    const tokenResponse = {
      access_token: 'login-token',
      token_type: 'bearer',
      expires_in: 86400,
      refresh_token: 'login-refresh',
      username: 'admin',
      display_name: 'Admin',
      role: 'admin',
    }
    mockLogin.mockResolvedValue(tokenResponse)

    const auth = useAuthStore()
    await auth.login('admin', 'password123')

    expect(mockLogin).toHaveBeenCalledWith('admin', 'password123')
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.accessToken).toBe('login-token')
    expect(auth.username).toBe('admin')
  })

  it('login should propagate API errors', async () => {
    mockLogin.mockRejectedValue(new Error('Invalid credentials'))

    const auth = useAuthStore()
    await expect(auth.login('admin', 'wrong')).rejects.toThrow('Invalid credentials')
    expect(auth.isAuthenticated).toBe(false)
  })

  it('logout should clear all state and localStorage', () => {
    const auth = useAuthStore()

    // Set up logged-in state
    auth.setTokens({
      access_token: 'token',
      token_type: 'bearer',
      expires_in: 86400,
      refresh_token: 'refresh',
      username: 'admin',
      display_name: 'Admin',
      role: 'admin',
    })

    // Logout
    auth.logout()

    // Check state
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.accessToken).toBeNull()
    expect(auth.refreshToken).toBeNull()
    expect(auth.username).toBeNull()
    expect(auth.displayName).toBeNull()
    expect(auth.role).toBeNull()

    // Check localStorage
    expect(localStorage.getItem('kc_access_token')).toBeNull()
    expect(localStorage.getItem('kc_refresh_token')).toBeNull()
    expect(localStorage.getItem('kc_username')).toBeNull()
    expect(localStorage.getItem('kc_display_name')).toBeNull()
    expect(localStorage.getItem('kc_role')).toBeNull()
  })

  it('setTokens should handle missing refresh_token', () => {
    const auth = useAuthStore()

    auth.setTokens({
      access_token: 'token',
      username: 'user',
      display_name: 'User',
      role: 'moderator',
    } as any) // refresh_token not provided

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.refreshToken).toBeNull()
  })

  it('loginWithSteamCode should trade the code and store the session', async () => {
    mockExchangeSteamCode.mockResolvedValue({
      access_token: 'steam-token',
      token_type: 'bearer',
      expires_in: 86400,
      username: 'mod1',
      display_name: 'Mod One',
      role: 'moderator',
    })
    const auth = useAuthStore()

    await auth.loginWithSteamCode('one-time-code')

    expect(mockExchangeSteamCode).toHaveBeenCalledWith('one-time-code')
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.username).toBe('mod1')
    expect(localStorage.getItem('kc_access_token')).toBe('steam-token')
  })

  it('loginWithSteamCode should leave the store signed out when the code is refused', async () => {
    mockExchangeSteamCode.mockRejectedValue({ response: { status: 400 } })
    const auth = useAuthStore()

    await expect(auth.loginWithSteamCode('stale')).rejects.toBeTruthy()
    expect(auth.isAuthenticated).toBe(false)
  })
})
