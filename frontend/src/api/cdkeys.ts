import apiClient from './client'
import type { CdKey, CdKeyDetail, CdKeyRedeemRecord } from '@/types'
import type { PaginatedResponse } from './store'

// ─── Keys CRUD ───────────────────────────────────────

export async function getCdKeys(params: {
  pageIndex?: number
  pageSize?: number
  search?: string
} = {}): Promise<PaginatedResponse<CdKey>> {
  const response = await apiClient.get('/api/cdkeys', { params })
  return response.data.data
}

export async function getCdKeyDetail(id: number): Promise<CdKeyDetail> {
  const response = await apiClient.get(`/api/cdkeys/${id}`)
  return response.data.data
}

export async function createCdKey(data: {
  key: string
  maxRedeemCount?: number
  expiryAt?: string
  description?: string
  itemIds?: number[]
  commandIds?: number[]
}): Promise<{ id: number }> {
  const response = await apiClient.post('/api/cdkeys', data)
  return response.data.data
}

export async function updateCdKey(id: number, data: {
  key: string
  maxRedeemCount: number
  expiryAt?: string
  description?: string
  itemIds?: number[]
  commandIds?: number[]
}): Promise<{ id: number }> {
  const response = await apiClient.put(`/api/cdkeys/${id}`, data)
  return response.data.data
}

export async function deleteCdKey(id: number): Promise<void> {
  await apiClient.delete(`/api/cdkeys/${id}`)
}

// ─── Redemption ──────────────────────────────────────

export async function redeemCdKey(id: number, playerId: string, playerName?: string): Promise<{
  message: string
  deliveryLog: string[]
  remainingRedemptions: number
}> {
  const response = await apiClient.post(`/api/cdkeys/${id}/redeem`, { playerId, playerName })
  return response.data.data
}

export async function getCdKeyRedemptions(id: number): Promise<CdKeyRedeemRecord[]> {
  const response = await apiClient.get(`/api/cdkeys/${id}/redemptions`)
  return response.data.data
}

export async function getAllRedemptions(params: {
  pageIndex?: number
  pageSize?: number
  cdKeyId?: number
} = {}): Promise<PaginatedResponse<CdKeyRedeemRecord>> {
  const response = await apiClient.get('/api/cdkeys/redemptions', { params })
  return response.data.data
}
