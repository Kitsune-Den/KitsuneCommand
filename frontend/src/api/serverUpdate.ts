import apiClient from './client'

export interface ServerUpdateSettings {
  autoUpdate: boolean
  branch: string
  branchPassword: string
  logRetention: number
  steamAppId: number
  steamUsername: string
}

export async function getServerUpdateSettings(): Promise<ServerUpdateSettings> {
  const res = await apiClient.get('/api/server-update/settings')
  return res.data.data
}

export async function saveServerUpdateSettings(settings: ServerUpdateSettings): Promise<string> {
  const res = await apiClient.put('/api/server-update/settings', settings)
  return res.data.data ?? res.data.message ?? 'Saved.'
}

export async function getServerConfigBak(): Promise<string> {
  const res = await apiClient.get('/api/server-update/config-bak')
  return res.data.data
}

export async function saveServerConfigBak(content: string): Promise<string> {
  const res = await apiClient.put('/api/server-update/config-bak', { content })
  return res.data.data ?? res.data.message ?? 'Saved.'
}

export interface SteamAuthResult {
  success: boolean
  message: string
  needsGuardCode?: boolean
}

/**
 * Authenticate the Steam username configured in settings. Password + Guard code
 * are sent over the wire, passed to steamcmd's stdin server-side, and never stored.
 * On success, steamcmd caches credentials so future auto-updates don't prompt.
 */
export async function authenticateSteam(password: string, guardCode?: string): Promise<SteamAuthResult> {
  const res = await apiClient.post('/api/server-update/steam-auth', {
    password,
    guardCode: guardCode || '',
  })
  return res.data.data as SteamAuthResult
}
