import { describe, it, expect, vi, beforeEach } from 'vitest'

const mockGet = vi.fn()
const mockPut = vi.fn()

vi.mock('@/api/client', () => ({
  default: {
    get: (...args: any[]) => mockGet(...args),
    put: (...args: any[]) => mockPut(...args),
  },
}))

import { getSafeZones, saveSafeZones } from '@/api/safeZones'
import type { SafeZoneSettings } from '@/types'

const settings: SafeZoneSettings = {
  enabled: true,
  sweepIntervalSeconds: 2,
  zones: [{
    id: 'abc', name: 'Trader town', enabled: true,
    minX: -10, minZ: -10, maxX: 10, maxZ: 10,
    removeAnimals: false, allowBloodMoon: false, removedCount: 3,
  }],
}

describe('Safe zones API', () => {
  beforeEach(() => {
    mockGet.mockReset()
    mockPut.mockReset()
  })

  it('getSafeZones calls GET /api/safezones', async () => {
    mockGet.mockResolvedValue({ data: { data: settings } })
    expect(await getSafeZones()).toEqual(settings)
    expect(mockGet).toHaveBeenCalledWith('/api/safezones')
  })

  it('saveSafeZones PUTs the whole settings object and returns what the server kept', async () => {
    mockPut.mockResolvedValue({ data: { data: settings } })
    expect(await saveSafeZones(settings)).toEqual(settings)
    expect(mockPut).toHaveBeenCalledWith('/api/safezones', settings)
  })
})
