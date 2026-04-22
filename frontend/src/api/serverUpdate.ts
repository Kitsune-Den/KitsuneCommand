import apiClient from './client'

export interface ServerUpdateSettings {
  autoUpdate: boolean
  branch: string
  branchPassword: string
  logRetention: number
  steamAppId: number
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
