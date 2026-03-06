import apiClient from './client'
import type { Ticket, TicketDetail, TicketSettings, TicketStats } from '@/types'

export async function getTickets(params: {
  pageIndex?: number
  pageSize?: number
  status?: string
  search?: string
} = {}): Promise<{ items: Ticket[]; total: number }> {
  const response = await apiClient.get('/api/tickets', { params })
  return response.data.data
}

export async function getTicketDetail(id: number): Promise<TicketDetail> {
  const response = await apiClient.get(`/api/tickets/${id}`)
  return response.data.data
}

export async function replyToTicket(id: number, message: string): Promise<{ messageId: number; delivered: boolean }> {
  const response = await apiClient.post(`/api/tickets/${id}/reply`, { message })
  return response.data.data
}

export async function updateTicketStatus(
  id: number,
  status: string,
  assignedTo?: string
): Promise<void> {
  await apiClient.put(`/api/tickets/${id}/status`, { status, assignedTo })
}

export async function getTicketStats(): Promise<TicketStats> {
  const response = await apiClient.get('/api/tickets/stats')
  return response.data.data
}

export async function getTicketSettings(): Promise<TicketSettings> {
  const response = await apiClient.get('/api/tickets/settings')
  return response.data.data
}

export async function updateTicketSettings(settings: TicketSettings): Promise<void> {
  await apiClient.put('/api/tickets/settings', settings)
}
