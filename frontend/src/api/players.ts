import apiClient from './client'
import type { PlayerInfo, PlayerDetailInfo, InventorySlot, PlayerSkillInfo } from '@/types'

export async function getOnlinePlayers(): Promise<PlayerInfo[]> {
  const response = await apiClient.get('/api/players')
  return response.data.data
}

export async function getPlayer(entityId: number): Promise<PlayerDetailInfo> {
  const response = await apiClient.get(`/api/players/${entityId}`)
  return response.data.data
}

export async function getPlayerInventory(entityId: number): Promise<{ bag: InventorySlot[]; belt: InventorySlot[] }> {
  const response = await apiClient.get(`/api/players/${entityId}/inventory`)
  return response.data.data
}

export async function getPlayerSkills(entityId: number): Promise<PlayerSkillInfo[]> {
  const response = await apiClient.get(`/api/players/${entityId}/skills`)
  return response.data.data
}

export async function kickPlayer(entityId: number, reason?: string): Promise<void> {
  await apiClient.post(`/api/players/${entityId}/kick`, { entityId, reason })
}

export async function banPlayer(entityId: number, reason?: string, duration?: string): Promise<void> {
  await apiClient.post(`/api/players/${entityId}/ban`, { entityId, reason, duration })
}

export async function giveItem(entityId: number, itemName: string, count: number, quality?: number): Promise<void> {
  await apiClient.post(`/api/players/${entityId}/give`, { entityId, itemName, count, quality })
}

export async function teleportPlayer(entityId: number, x: number, y: number, z: number): Promise<void> {
  await apiClient.post(`/api/players/${entityId}/teleport`, { entityId, x, y, z })
}
