import apiClient from './client'
import type { VipPerksSettings } from '@/types'

export async function getVipPerksSettings(): Promise<VipPerksSettings> {
  const response = await apiClient.get('/api/vipperks/settings')
  return response.data.data
}

export async function updateVipPerksSettings(settings: VipPerksSettings): Promise<void> {
  await apiClient.put('/api/vipperks/settings', settings)
}

export async function getVipTiers(): Promise<string[]> {
  const response = await apiClient.get('/api/vipperks/tiers')
  return response.data.data ?? []
}
