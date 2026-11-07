import apiClient from './client'
import type { SafeZoneSettings } from '@/types'

export async function getSafeZones(): Promise<SafeZoneSettings> {
  const response = await apiClient.get('/api/safezones')
  return response.data.data
}

/** Replaces the whole settings object, zones included. Returns what the server saved. */
export async function saveSafeZones(settings: SafeZoneSettings): Promise<SafeZoneSettings> {
  const response = await apiClient.put('/api/safezones', settings)
  return response.data.data
}
