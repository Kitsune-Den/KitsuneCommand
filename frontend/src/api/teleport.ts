import apiClient from './client'
import type { CityLocation, HomeLocation, TeleRecord } from '@/types'
import type { PaginatedResponse } from './store'

// ─── Cities ──────────────────────────────────────────

export async function getCities(params: {
  pageIndex?: number
  pageSize?: number
  search?: string
} = {}): Promise<PaginatedResponse<CityLocation>> {
  const response = await apiClient.get('/api/teleport/cities', { params })
  return response.data.data
}

export async function getCity(id: number): Promise<CityLocation> {
  const response = await apiClient.get(`/api/teleport/cities/${id}`)
  return response.data.data
}

export async function createCity(data: {
  cityName: string
  pointsRequired: number
  position: string
  viewDirection?: string
}): Promise<{ id: number }> {
  const response = await apiClient.post('/api/teleport/cities', data)
  return response.data.data
}

export async function updateCity(id: number, data: {
  cityName: string
  pointsRequired: number
  position: string
  viewDirection?: string
}): Promise<{ id: number }> {
  const response = await apiClient.put(`/api/teleport/cities/${id}`, data)
  return response.data.data
}

export async function deleteCity(id: number): Promise<void> {
  await apiClient.delete(`/api/teleport/cities/${id}`)
}

export async function teleportToCity(cityId: number, playerId: string, playerName?: string): Promise<{
  message: string
  output: string
}> {
  const response = await apiClient.post(`/api/teleport/cities/${cityId}/teleport`, { playerId, playerName })
  return response.data.data
}

// ─── Homes ───────────────────────────────────────────

export async function getHomes(params: {
  pageIndex?: number
  pageSize?: number
  search?: string
} = {}): Promise<PaginatedResponse<HomeLocation>> {
  const response = await apiClient.get('/api/teleport/homes', { params })
  return response.data.data
}

export async function getPlayerHomes(playerId: string): Promise<HomeLocation[]> {
  const response = await apiClient.get(`/api/teleport/homes/player/${playerId}`)
  return response.data.data
}

export async function deleteHome(id: number): Promise<void> {
  await apiClient.delete(`/api/teleport/homes/${id}`)
}

export async function teleportToHome(homeId: number, playerId?: string): Promise<{
  message: string
  output: string
}> {
  const response = await apiClient.post(`/api/teleport/homes/${homeId}/teleport`, { playerId })
  return response.data.data
}

// ─── History ─────────────────────────────────────────

export async function getTeleportHistory(params: {
  pageIndex?: number
  pageSize?: number
  playerId?: string
} = {}): Promise<PaginatedResponse<TeleRecord>> {
  const response = await apiClient.get('/api/teleport/history', { params })
  return response.data.data
}
