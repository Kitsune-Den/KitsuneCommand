import apiClient from './client'

export interface TokenResponse {
  access_token: string
  token_type: string
  expires_in: number
  refresh_token?: string
  username: string
  role: string
  display_name: string
}

export async function login(username: string, password: string): Promise<TokenResponse> {
  // Login hits the standalone listener on port 8890 (OWIN/Mono compat). When the page
  // is served from that listener directly (dev: http://host:8890/), a relative path
  // naturally targets the same host+port. When the page is served from a reverse proxy
  // (prod: https://panel.example.com → CF Tunnel → http://localhost:8890), same-origin
  // keeps the request on HTTPS and avoids mixed-content blocks.
  const loginUrl = '/api/auth/login/'
  const response = await fetch(loginUrl, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password }),
  })

  if (!response.ok) {
    const err = await response.json()
    throw { response: { data: err, status: response.status } }
  }

  return response.json()
}

export async function refreshToken(token: string): Promise<TokenResponse> {
  const params = new URLSearchParams()
  params.append('grant_type', 'refresh_token')
  params.append('refresh_token', token)

  const response = await apiClient.post<TokenResponse>('/token', params, {
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  })

  return response.data
}

export interface UserInfo {
  username: string
  role: string
  userId: string
  displayName: string
}

export async function getCurrentUser(): Promise<UserInfo> {
  const response = await apiClient.get('/api/auth/me')
  return response.data.data
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  await apiClient.post('/api/auth/change-password', { currentPassword, newPassword })
}

// ---- Sign in through Steam ----
// The redirecting steps (/api/auth/steam/login, /link, /callback, /exchange) are
// served by the login listener itself; the rest are ordinary OWIN endpoints.

export const steamLoginUrl = '/api/auth/steam/login'

export async function getSteamLoginEnabled(): Promise<boolean> {
  try {
    const response = await apiClient.get('/api/auth/steam/status')
    return !!response.data.data?.enabled
  } catch {
    return false
  }
}

export async function exchangeSteamCode(code: string): Promise<TokenResponse> {
  const response = await fetch('/api/auth/steam/exchange', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code }),
  })

  if (!response.ok) {
    const err = await response.json()
    throw { response: { data: err, status: response.status } }
  }

  return response.json()
}

export interface SteamAccountInfo {
  enabled: boolean
  linked: boolean
  steamId?: string
}

export async function getSteamAccount(): Promise<SteamAccountInfo> {
  const response = await apiClient.get('/api/auth/steam/account')
  return response.data.data
}

/** Returns the Steam URL to send the browser to; the server pins the attempt in a cookie. */
export async function startSteamLink(): Promise<string> {
  const response = await apiClient.post('/api/auth/steam/link')
  return response.data.url
}

export async function unlinkSteam(): Promise<void> {
  await apiClient.post('/api/auth/steam/unlink')
}

export interface SteamLoginSettings {
  enabled: boolean
  publicUrl: string | null
}

export async function getSteamLoginSettings(): Promise<SteamLoginSettings> {
  const response = await apiClient.get('/api/auth/steam/settings')
  return response.data.data
}

export async function updateSteamLoginSettings(settings: SteamLoginSettings): Promise<SteamLoginSettings> {
  const response = await apiClient.put('/api/auth/steam/settings', settings)
  return response.data.data
}
