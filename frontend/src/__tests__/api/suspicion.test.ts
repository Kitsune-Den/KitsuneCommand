import { describe, it, expect, vi, beforeEach } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockPut = vi.fn()

vi.mock('@/api/client', () => ({
  default: {
    get: (...args: any[]) => mockGet(...args),
    post: (...args: any[]) => mockPost(...args),
    put: (...args: any[]) => mockPut(...args),
  },
}))

import {
  getSuspicionEvents,
  reviewSuspicionEvents,
  getSuspicionSettings,
  updateSuspicionSettings,
  parseSuspicionDetails,
} from '@/api/suspicion'

describe('Suspicion API', () => {
  beforeEach(() => {
    mockGet.mockReset()
    mockPost.mockReset()
    mockPut.mockReset()
  })

  it('lists events with the given filters and unwraps the envelope', async () => {
    const payload = { items: [{ id: 1 }], total: 1, unreviewed: 4 }
    mockGet.mockResolvedValue({ data: { code: 200, data: payload } })

    const result = await getSuspicionEvents({ pageIndex: 2, pageSize: 25, detector: 'movement', reviewed: false })

    expect(mockGet).toHaveBeenCalledWith('/api/suspicion', {
      params: { pageIndex: 2, pageSize: 25, detector: 'movement', reviewed: false },
    })
    expect(result).toEqual(payload)
  })

  it('throws when the server reports an error inside a 200 response', async () => {
    mockGet.mockResolvedValue({ data: { code: 500, message: 'db locked' } })
    await expect(getSuspicionEvents()).rejects.toThrow('db locked')
  })

  it('posts review changes and returns the updated count', async () => {
    mockPost.mockResolvedValue({ data: { code: 200, data: { updated: 2 } } })

    const updated = await reviewSuspicionEvents([3, 4], true, 'checked')

    expect(mockPost).toHaveBeenCalledWith('/api/suspicion/review', { ids: [3, 4], reviewed: true, note: 'checked' })
    expect(updated).toBe(2)
  })

  it('reads and saves settings, returning what the server stored', async () => {
    const stored = { enabled: true, retentionDays: 90 }
    mockGet.mockResolvedValue({ data: { code: 200, data: stored } })
    mockPut.mockResolvedValue({ data: { code: 200, data: { ...stored, retentionDays: 3650 } } })

    expect(await getSuspicionSettings()).toEqual(stored)
    const saved = await updateSuspicionSettings({ ...stored, retentionDays: 99999 } as any)

    expect(mockGet).toHaveBeenCalledWith('/api/suspicion/settings')
    expect(mockPut).toHaveBeenCalledWith('/api/suspicion/settings', { enabled: true, retentionDays: 99999 })
    expect(saved.retentionDays).toBe(3650)
  })

  describe('parseSuspicionDetails', () => {
    it('parses a JSON object', () => {
      expect(parseSuspicionDetails({ details: '{"item":"resourceWood","count":9000}' })).toEqual({ item: 'resourceWood', count: 9000 })
    })

    it.each([null, '', 'not json', '[1,2]', '"text"'])('returns {} for %j', (details) => {
      expect(parseSuspicionDetails({ details })).toEqual({})
    })
  })
})
