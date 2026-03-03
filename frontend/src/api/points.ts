import apiClient from './client'

export interface PointsListParams {
  pageIndex?: number
  pageSize?: number
  search?: string
}

export interface PointsListResponse {
  items: PointsInfo[]
  total: number
  pageIndex: number
  pageSize: number
}

export interface PointsInfo {
  id: string
  createdAt: string
  playerName: string
  points: number
  lastSignInAt: string | null
}

export async function getPointsList(params: PointsListParams = {}): Promise<PointsListResponse> {
  const response = await apiClient.get('/api/points', { params })
  return response.data.data
}

export async function getPlayerPoints(playerId: string): Promise<PointsInfo> {
  const response = await apiClient.get(`/api/points/${playerId}`)
  return response.data.data
}

export async function adjustPoints(
  playerId: string,
  amount: number,
  reason?: string,
): Promise<{ playerId: string; playerName: string; points: number; change: number; reason: string }> {
  const response = await apiClient.post(`/api/points/${playerId}/adjust`, { amount, reason })
  return response.data.data
}

export async function triggerSignIn(
  playerId: string,
): Promise<{ playerId: string; message: string; awarded: boolean; points: number; change?: number }> {
  const response = await apiClient.post(`/api/points/${playerId}/sign-in`)
  return response.data.data
}
