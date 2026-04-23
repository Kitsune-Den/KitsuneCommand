import apiClient from './client'

export interface ModInfo {
  folderName: string
  displayName: string
  version: string | null
  author: string | null
  description: string | null
  website: string | null
  folderSize: number
  isEnabled: boolean
  isProtected: boolean
}

export async function getMods(): Promise<ModInfo[]> {
  const res = await apiClient.get('/api/mods')
  return res.data.data
}

export interface ModUploadResult {
  sourceFileName: string
  installedMods: ModInfo[]
  warnings: string[]
}

export async function uploadMod(file: File): Promise<ModUploadResult> {
  const formData = new FormData()
  formData.append('file', file)
  const res = await apiClient.post('/api/mods/upload', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
    timeout: 120000, // 2 min for large uploads
  })
  return res.data.data
}

export async function deleteMod(modName: string): Promise<string> {
  const res = await apiClient.delete(`/api/mods/${encodeURIComponent(modName)}`)
  return res.data.message
}

export async function toggleMod(modName: string): Promise<string> {
  const res = await apiClient.post(`/api/mods/${encodeURIComponent(modName)}/toggle`)
  return res.data.message
}

// --- Mod Discovery (Nexus Mods) ---

export interface NexusMod {
  modId: number
  name: string
  summary: string
  version: string
  author: string
  endorsements: number
  downloads: number
  pictureUrl: string | null
  updatedAt: string | null
}

export interface NexusSearchResult {
  mods: NexusMod[]
  totalCount: number
}

export interface NexusDiscoverSettings {
  apiKey: string
  hasApiKey: boolean
  cacheDurationMinutes: number
}

export interface NexusValidationResult {
  name: string
  isValid: boolean
}

export async function searchDiscoverMods(params: {
  q?: string
  sort?: string
  offset?: number
  count?: number
}): Promise<NexusSearchResult> {
  const res = await apiClient.get('/api/mods/discover/search', { params })
  return res.data.data
}

export async function getDiscoverSettings(): Promise<NexusDiscoverSettings> {
  const res = await apiClient.get('/api/mods/discover/settings')
  return res.data.data
}

export async function updateDiscoverSettings(settings: { apiKey?: string; cacheDurationMinutes?: number }): Promise<void> {
  await apiClient.put('/api/mods/discover/settings', settings)
}

export async function validateNexusKey(apiKey: string): Promise<NexusValidationResult> {
  const res = await apiClient.post('/api/mods/discover/validate-key', { apiKey })
  return res.data.data
}

export async function clearDiscoverCache(): Promise<void> {
  await apiClient.post('/api/mods/discover/clear-cache')
}
