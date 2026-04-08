import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useChatStore } from '@/stores/chat'

const mockMessage = (id: number) => ({
  id,
  playerId: 'player1',
  playerName: 'Alice',
  message: `Message ${id}`,
  chatType: 0,
  createdAt: new Date().toISOString(),
})

describe('Chat Store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('should initialize empty with hasOlderMessages true', () => {
    const store = useChatStore()
    expect(store.messages).toEqual([])
    expect(store.hasOlderMessages).toBe(true)
  })

  it('addRealtimeMessage appends to the end', () => {
    const store = useChatStore()
    store.addRealtimeMessage(mockMessage(1) as any)
    store.addRealtimeMessage(mockMessage(2) as any)
    expect(store.messages.length).toBe(2)
    expect(store.messages[0].id).toBe(1)
    expect(store.messages[1].id).toBe(2)
  })

  it('addRealtimeMessage caps at 500 messages (trims to 400)', () => {
    const store = useChatStore()
    for (let i = 0; i < 501; i++) {
      store.addRealtimeMessage(mockMessage(i) as any)
    }
    expect(store.messages.length).toBe(400)
  })

  it('prependHistory adds older messages to the beginning', () => {
    const store = useChatStore()
    store.addRealtimeMessage(mockMessage(10) as any)
    store.prependHistory([mockMessage(1), mockMessage(2)] as any[], 20)
    expect(store.messages.length).toBe(3)
    expect(store.messages[0].id).toBe(1)
    expect(store.messages[2].id).toBe(10)
  })

  it('prependHistory deduplicates by id', () => {
    const store = useChatStore()
    store.addRealtimeMessage(mockMessage(1) as any)
    store.prependHistory([mockMessage(1), mockMessage(2)] as any[], 10)
    expect(store.messages.length).toBe(2)
  })

  it('prependHistory sets hasOlderMessages based on total', () => {
    const store = useChatStore()
    store.prependHistory([mockMessage(1), mockMessage(2)] as any[], 2)
    expect(store.hasOlderMessages).toBe(false)
  })

  it('setMessages replaces all messages', () => {
    const store = useChatStore()
    store.addRealtimeMessage(mockMessage(1) as any)
    store.setMessages([mockMessage(10), mockMessage(11)] as any[], 100)
    expect(store.messages.length).toBe(2)
    expect(store.messages[0].id).toBe(10)
    expect(store.hasOlderMessages).toBe(true)
  })

  it('clearMessages resets state', () => {
    const store = useChatStore()
    store.addRealtimeMessage(mockMessage(1) as any)
    store.clearMessages()
    expect(store.messages).toEqual([])
    expect(store.hasOlderMessages).toBe(true)
  })
})
