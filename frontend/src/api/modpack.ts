import apiClient from './client'
import type { Modpack, ModpackState, PublicModpack } from '@/types'

export async function getModpackState(): Promise<ModpackState> {
  const response = await apiClient.get('/api/modpack')
  return response.data.data
}

export async function saveModpackDraft(payload: {
  name: string
  version: string
  modList: string[]
  description: string | null
}): Promise<Modpack> {
  const response = await apiClient.put('/api/modpack', payload)
  return response.data.data
}

export async function buildModpack(): Promise<Modpack> {
  const response = await apiClient.post('/api/modpack/build')
  return response.data.data
}

export async function publishModpack(): Promise<Modpack> {
  const response = await apiClient.post('/api/modpack/publish')
  return response.data.data
}

export async function archiveModpack(): Promise<Modpack> {
  const response = await apiClient.post('/api/modpack/archive')
  return response.data.data
}

export async function unarchiveModpack(): Promise<Modpack> {
  const response = await apiClient.post('/api/modpack/unarchive')
  return response.data.data
}

export async function deleteModpack(): Promise<void> {
  await apiClient.delete('/api/modpack')
}

/**
 * Public metadata for the currently-published modpack. Returns null if none
 * is published. Doesn't require auth — used by the login page to decide
 * whether to show the download CTA.
 */
export async function getPublishedModpack(): Promise<PublicModpack | null> {
  try {
    const response = await apiClient.get('/api/modpack/public')
    const env = response.data
    if (env?.code !== 200) return null
    return env.data as PublicModpack
  } catch {
    return null
  }
}

/** Public download URL — anchor href, not fetched programmatically. */
export const publicModpackDownloadUrl = '/api/modpack/public/download'
