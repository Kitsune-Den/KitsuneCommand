import { describe, it, expect, vi, beforeEach } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()

vi.mock('@/api/client', () => ({
  default: {
    get: (...args: any[]) => mockGet(...args),
    post: (...args: any[]) => mockPost(...args),
  },
}))

import { getChatHistory, sendChatMessage } from '@/api/chat'

describe('Chat API', () => {
  beforeEach(() => {
    mockGet.mockReset()
    mockPost.mockReset()
  })

  it('getChatHistory calls GET /api/chat/history with params', async () => {
    const response = { items: [], total: 0, pageIndex: 0, pageSize: 50 }
    mockGet.mockResolvedValue({ data: { data: response } })

    const result = await getChatHistory({ pageIndex: 0, pageSize: 50, search: 'hello' })

    expect(mockGet).toHaveBeenCalledWith('/api/chat/history', {
      params: { pageIndex: 0, pageSize: 50, search: 'hello' },
    })
    expect(result.total).toBe(0)
  })

  it('getChatHistory works with default params', async () => {
    mockGet.mockResolvedValue({ data: { data: { items: [], total: 0 } } })
    await getChatHistory()
    expect(mockGet).toHaveBeenCalledWith('/api/chat/history', { params: {} })
  })

  it('sendChatMessage calls POST with message', async () => {
    mockPost.mockResolvedValue({ data: {} })
    await sendChatMessage('Hello world')
    expect(mockPost).toHaveBeenCalledWith('/api/chat/send', { message: 'Hello world', targetPlayer: undefined })
  })

  it('sendChatMessage calls POST with target player', async () => {
    mockPost.mockResolvedValue({ data: {} })
    await sendChatMessage('whisper', 'Alice')
    expect(mockPost).toHaveBeenCalledWith('/api/chat/send', { message: 'whisper', targetPlayer: 'Alice' })
  })
})
