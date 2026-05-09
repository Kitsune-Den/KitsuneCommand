import apiClient from './client'
import type { GracefulRestartSettings } from '@/types'

export async function getRestartSettings(): Promise<GracefulRestartSettings> {
  const response = await apiClient.get('/api/restart/settings')
  return response.data.data
}

export async function updateRestartSettings(settings: GracefulRestartSettings): Promise<void> {
  await apiClient.put('/api/restart/settings', settings)
}

export async function triggerRestartNow(leadMinutes: number): Promise<string> {
  const response = await apiClient.post('/api/restart/now', { leadMinutes })
  return response.data.message ?? response.data.data ?? 'Restart started.'
}
