import apiClient from './client'
import type { PlayerInfo, PlayerDetailInfo, InventorySlot, PlayerSkillInfo, PlayerMetadata } from '@/types'

export async function getOnlinePlayers(): Promise<PlayerInfo[]> {
  const response = await apiClient.get('/api/players')
  return response.data.data
}

export interface KnownPlayersResponse {
  items: PlayerInfo[]
  total: number
  pageIndex: number
  pageSize: number
}

export async function getKnownPlayers(
  pageIndex = 0,
  pageSize = 100,
  search?: string
): Promise<KnownPlayersResponse> {
  const response = await apiClient.get('/api/players/known', {
    params: { pageIndex, pageSize, search: search || undefined },
  })
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

export async function getAllPlayerMetadata(): Promise<Record<string, PlayerMetadata>> {
  const response = await apiClient.get('/api/players/metadata')
  return response.data.data ?? {}
}

export async function getPlayerMetadata(playerId: string): Promise<PlayerMetadata | null> {
  const response = await apiClient.get(`/api/players/metadata/${encodeURIComponent(playerId)}`)
  return response.data.data
}

export async function updatePlayerMetadata(
  playerId: string,
  data: { nameColor?: string | null; customTag?: string | null; notes?: string | null }
): Promise<PlayerMetadata> {
  const response = await apiClient.put(`/api/players/metadata/${encodeURIComponent(playerId)}`, data)
  return response.data.data
}

export async function setAdminLevel(entityId: number, level: number): Promise<void> {
  await apiClient.post(`/api/players/${entityId}/admin-level`, { level })
}
