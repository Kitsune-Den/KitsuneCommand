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

// --- Mod Update Check ---

export type ModUpdateStatus =
  | 'up_to_date'
  | 'update_available'
  | 'version_differs'
  | 'no_match'
  | 'skipped'

/** One result per installed mod after a check-for-updates sweep. */
export interface ModUpdateCheckResult {
  folderName: string
  status: ModUpdateStatus
  installedVersion: string | null
  latestVersion: string | null
  /** Where latestVersion came from. */
  source: 'nexus' | '7daystodiemods' | null
  /** The matched listing's page on that site. */
  url: string | null
  nexusModId: number | null
  nexusUrl: string | null
  matchedName: string | null
}

export async function checkModUpdates(): Promise<ModUpdateCheckResult[]> {
  const res = await apiClient.post('/api/mods/check-updates')
  return res.data.data ?? []
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

// --- Mod Discovery (7daystodiemods.com) ---

export interface SevenDaysMod {
  slug: string
  title: string
  summary: string | null
  version: string | null
  author: string | null
  authorVerified: boolean
  likes: number
  downloads: number
  thumbnailUrl: string | null
  updatedAt: string | null
  categories: string[]
  gameVersions: string[]
  /** 'server-only' | 'client-only' | 'server-and-client' */
  serverSide: string | null
  serverSideName: string | null
  isAdult: boolean
  url: string
}

export interface SevenDaysModsSearchResult {
  mods: SevenDaysMod[]
  totalCount: number
  page: number
  totalPages: number
}

export interface SevenDaysModsOption {
  slug: string
  name: string
}

export interface SevenDaysModsMetadata {
  categories: SevenDaysModsOption[]
  gameVersions: SevenDaysModsOption[]
  serverSides: SevenDaysModsOption[]
}

/** Page-based (page 1 is first). sort: newest | updated | likes | downloads | views. */
export async function searchSevenDaysMods(params: {
  q?: string
  sort?: string
  page?: number
  limit?: number
  category?: string
  gameVersion?: string
  serverSide?: string
}): Promise<SevenDaysModsSearchResult> {
  const res = await apiClient.get('/api/mods/discover/7daystodiemods/search', { params })
  return res.data.data
}

export async function getSevenDaysModsMetadata(): Promise<SevenDaysModsMetadata> {
  const res = await apiClient.get('/api/mods/discover/7daystodiemods/metadata')
  return res.data.data
}
