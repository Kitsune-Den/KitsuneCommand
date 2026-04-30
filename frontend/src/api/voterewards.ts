import apiClient from './client'
import type { VoteRewardsSettings, VoteGrant } from '@/types'

export async function getVoteRewardsSettings(): Promise<VoteRewardsSettings> {
  const response = await apiClient.get('/api/voterewards/settings')
  return response.data.data
}

export async function updateVoteRewardsSettings(settings: VoteRewardsSettings): Promise<void> {
  await apiClient.put('/api/voterewards/settings', settings)
}

export async function getVoteGrants(limit = 50): Promise<VoteGrant[]> {
  const response = await apiClient.get('/api/voterewards/grants', { params: { limit } })
  return response.data.data
}

export async function getVoteGrantCount(): Promise<number> {
  const response = await apiClient.get('/api/voterewards/grants/count')
  return response.data.data
}
