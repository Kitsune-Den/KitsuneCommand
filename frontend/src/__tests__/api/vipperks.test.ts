import { describe, it, expect, vi, beforeEach } from 'vitest'

const mockGet = vi.fn()
const mockPut = vi.fn()

vi.mock('@/api/client', () => ({
  default: {
    get: (...args: any[]) => mockGet(...args),
    put: (...args: any[]) => mockPut(...args),
  },
}))

import { getVipPerksSettings, updateVipPerksSettings, getVipTiers } from '@/api/vipperks'
import type { VipPerksSettings } from '@/types'

const sampleSettings: VipPerksSettings = {
  enabled: true,
  tiers: ['VIP', 'VIP+'],
  firstLoginPackEnabled: true,
  firstLoginTemplateName: 'starter',
  firstLoginMessage: 'hi {player}',
  tierGifts: [{ tier: 'VIP', templateName: 'weekly-crate', period: 'weekly' }],
}

describe('VipPerks API', () => {
  beforeEach(() => {
    mockGet.mockReset()
    mockPut.mockReset()
  })

  it('getVipPerksSettings calls GET /api/vipperks/settings', async () => {
    mockGet.mockResolvedValue({ data: { data: sampleSettings } })
    const result = await getVipPerksSettings()
    expect(mockGet).toHaveBeenCalledWith('/api/vipperks/settings')
    expect(result).toEqual(sampleSettings)
  })

  it('updateVipPerksSettings calls PUT /api/vipperks/settings with the body', async () => {
    mockPut.mockResolvedValue({ data: {} })
    await updateVipPerksSettings(sampleSettings)
    expect(mockPut).toHaveBeenCalledWith('/api/vipperks/settings', sampleSettings)
  })

  it('getVipTiers returns the tier list', async () => {
    mockGet.mockResolvedValue({ data: { data: ['VIP', 'VIP+'] } })
    const result = await getVipTiers()
    expect(mockGet).toHaveBeenCalledWith('/api/vipperks/tiers')
    expect(result).toEqual(['VIP', 'VIP+'])
  })

  it('getVipTiers returns [] when data is null', async () => {
    mockGet.mockResolvedValue({ data: { data: null } })
    const result = await getVipTiers()
    expect(result).toEqual([])
  })
})
