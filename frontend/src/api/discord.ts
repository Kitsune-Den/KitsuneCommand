import apiClient from './client'
import type { DiscordSettings, DiscordStatus } from '@/types'

export async function getDiscordSettings(): Promise<DiscordSettings> {
  const response = await apiClient.get('/api/settings/discord')
  return response.data.data
}

export async function updateDiscordSettings(settings: DiscordSettings): Promise<void> {
  await apiClient.put('/api/settings/discord', settings)
}

export async function getDiscordStatus(): Promise<DiscordStatus> {
  const response = await apiClient.get('/api/settings/discord/status')
  return response.data.data
}

export async function testDiscordConnection(): Promise<string> {
  const response = await apiClient.post('/api/settings/discord/test')
  return response.data.message
}
