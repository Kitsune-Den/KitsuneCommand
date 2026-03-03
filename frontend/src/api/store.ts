import apiClient from './client'
import type { GoodsItem, GoodsDetail, ItemDefinition, CommandDefinition, PurchaseRecord } from '@/types'

export interface PaginatedResponse<T> {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
}

// ─── Goods ───────────────────────────────────────────

export async function getStoreGoods(params: {
  pageIndex?: number
  pageSize?: number
  search?: string
} = {}): Promise<PaginatedResponse<GoodsItem>> {
  const response = await apiClient.get('/api/store/goods', { params })
  return response.data.data
}

export async function getStoreGoodsDetail(id: number): Promise<GoodsDetail> {
  const response = await apiClient.get(`/api/store/goods/${id}`)
  return response.data.data
}

export async function createStoreGoods(data: {
  name: string
  price: number
  description?: string
  itemIds?: number[]
  commandIds?: number[]
}): Promise<{ id: number }> {
  const response = await apiClient.post('/api/store/goods', data)
  return response.data.data
}

export async function updateStoreGoods(id: number, data: {
  name: string
  price: number
  description?: string
  itemIds?: number[]
  commandIds?: number[]
}): Promise<{ id: number }> {
  const response = await apiClient.put(`/api/store/goods/${id}`, data)
  return response.data.data
}

export async function deleteStoreGoods(id: number): Promise<void> {
  await apiClient.delete(`/api/store/goods/${id}`)
}

export async function buyStoreGoods(id: number, playerId: string, playerName: string): Promise<{
  message: string
  newBalance: number
  deliveryLog: string[]
}> {
  const response = await apiClient.post(`/api/store/goods/${id}/buy`, { playerId, playerName })
  return response.data.data
}

// ─── Purchase History ─────────────────────────────────

export async function getPurchaseHistory(params: {
  pageIndex?: number
  pageSize?: number
  playerId?: string
} = {}): Promise<PaginatedResponse<PurchaseRecord>> {
  const response = await apiClient.get('/api/store/history', { params })
  return response.data.data
}

// ─── Item Definitions ─────────────────────────────────

export async function getItemDefinitions(): Promise<ItemDefinition[]> {
  const response = await apiClient.get('/api/store/item-definitions')
  return response.data.data
}

export async function createItemDefinition(data: {
  itemName: string
  count?: number
  quality?: number
  durability?: number
  description?: string
}): Promise<{ id: number }> {
  const response = await apiClient.post('/api/store/item-definitions', data)
  return response.data.data
}

export async function updateItemDefinition(id: number, data: {
  itemName: string
  count: number
  quality: number
  durability: number
  description?: string
}): Promise<{ id: number }> {
  const response = await apiClient.put(`/api/store/item-definitions/${id}`, data)
  return response.data.data
}

export async function deleteItemDefinition(id: number): Promise<void> {
  await apiClient.delete(`/api/store/item-definitions/${id}`)
}

// ─── Command Definitions ──────────────────────────────

export async function getCommandDefinitions(): Promise<CommandDefinition[]> {
  const response = await apiClient.get('/api/store/command-definitions')
  return response.data.data
}

export async function createCommandDefinition(data: {
  command: string
  runInMainThread?: boolean
  description?: string
}): Promise<{ id: number }> {
  const response = await apiClient.post('/api/store/command-definitions', data)
  return response.data.data
}

export async function updateCommandDefinition(id: number, data: {
  command: string
  runInMainThread: boolean
  description?: string
}): Promise<{ id: number }> {
  const response = await apiClient.put(`/api/store/command-definitions/${id}`, data)
  return response.data.data
}

export async function deleteCommandDefinition(id: number): Promise<void> {
  await apiClient.delete(`/api/store/command-definitions/${id}`)
}
