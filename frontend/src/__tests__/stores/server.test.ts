import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useServerStore } from '@/stores/server'

describe('Server Store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('should initialize with default values', () => {
    const store = useServerStore()
    expect(store.gameDay).toBe(0)
    expect(store.gameHour).toBe(0)
    expect(store.isBloodMoon).toBe(false)
    expect(store.activity).toEqual([])
  })

  it('updateGameTime updates all time fields', () => {
    const store = useServerStore()
    store.updateGameTime({ day: 7, hour: 22, minute: 30, isBloodMoon: true })
    expect(store.gameDay).toBe(7)
    expect(store.gameHour).toBe(22)
    expect(store.gameMinute).toBe(30)
    expect(store.isBloodMoon).toBe(true)
  })

  it('updateBloodMoonVote updates vote state', () => {
    const store = useServerStore()
    store.updateBloodMoonVote({ isActive: true, currentVotes: 3, requiredVotes: 5, totalOnline: 8, bloodMoonDay: 7 })
    expect(store.bloodMoonVote.isActive).toBe(true)
    expect(store.bloodMoonVote.currentVotes).toBe(3)
  })

  it('addActivity prepends items', () => {
    const store = useServerStore()
    store.addActivity('login', 'Alice joined')
    store.addActivity('chat', 'Hello world')
    expect(store.activity.length).toBe(2)
    expect(store.activity[0].message).toBe('Hello world')
    expect(store.activity[1].message).toBe('Alice joined')
  })

  it('addActivity caps at 50 items', () => {
    const store = useServerStore()
    for (let i = 0; i < 60; i++) {
      store.addActivity('system', `event ${i}`)
    }
    expect(store.activity.length).toBe(50)
  })

  it('activity items have incrementing ids', () => {
    const store = useServerStore()
    store.addActivity('login', 'a')
    store.addActivity('logout', 'b')
    expect(store.activity[0].id).toBeGreaterThan(store.activity[1].id)
  })
})
