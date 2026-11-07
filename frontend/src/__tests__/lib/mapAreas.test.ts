import { describe, it, expect } from 'vitest'
import { claimRect, zoneRect, cornersToZone, ownerColor } from '@/lib/mapAreas'

describe('mapAreas', () => {
  it('centres a claim on its block', () => {
    // A 41-wide claim at 100,200 covers blocks 80..120 and 180..220
    expect(claimRect(100, 200, 41)).toEqual({ minX: 80, minZ: 180, maxX: 121, maxZ: 221 })
  })

  it('keeps a tiny claim at least one block wide', () => {
    expect(claimRect(5, 5, 0)).toEqual({ minX: 5, minZ: 5, maxX: 6, maxZ: 6 })
  })

  it('draws a zone out to the far edge of its last block', () => {
    expect(zoneRect({ minX: -10, minZ: 0, maxX: 10, maxZ: 5 })).toEqual({ minX: -10, minZ: 0, maxX: 11, maxZ: 6 })
  })

  it('turns two clicks in any order into ordered block corners', () => {
    expect(cornersToZone({ x: 12.7, z: -3.2 }, { x: -4.1, z: 8.9 })).toEqual({ minX: -5, minZ: -4, maxX: 12, maxZ: 8 })
  })

  it('gives each owner a steady colour', () => {
    expect(ownerColor('Steam_1')).toBe(ownerColor('Steam_1'))
    expect(ownerColor('Steam_1')).toMatch(/^hsl\(\d+, 85%, 60%\)$/)
    expect(ownerColor('Steam_1')).not.toBe(ownerColor('Steam_2'))
  })
})
