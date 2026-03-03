import apiClient from './client'
import type { ChatRecord } from '@/types'

export interface ChatHistoryParams {
  pageIndex?: number
  pageSize?: number
  search?: string
  chatType?: number | null
}

export interface ChatHistoryResponse {
  items: ChatRecord[]
  total: number
  pageIndex: number
  pageSize: number
}

export async function getChatHistory(params: ChatHistoryParams = {}): Promise<ChatHistoryResponse> {
  const response = await apiClient.get('/api/chat/history', { params })
  return response.data.data
}

export async function sendChatMessage(message: string, targetPlayer?: string): Promise<void> {
  await apiClient.post('/api/chat/send', { message, targetPlayer })
}
