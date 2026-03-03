import apiClient from './client'
import type { ChatCommandSettings } from '@/types'

export async function getChatCommandSettings(): Promise<ChatCommandSettings> {
  const response = await apiClient.get('/api/settings/chat-commands')
  return response.data.data
}

export async function updateChatCommandSettings(settings: ChatCommandSettings): Promise<void> {
  await apiClient.put('/api/settings/chat-commands', settings)
}
