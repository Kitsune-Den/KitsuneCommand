import { describe, it, expect, vi, beforeEach } from 'vitest'

const mockGet = vi.fn()

vi.mock('@/api/client', () => ({
  default: {
    get: (...args: any[]) => mockGet(...args),
  },
}))

import { getMapInfo, getMapMarkers, getMapPois, getTileUrl } from '@/api/map'

describe('Map API', () => {
  beforeEach(() => {
    mockGet.mockReset()
  })

  it('getMapInfo calls GET /api/map/info', async () => {
    mockGet.mockResolvedValue({ data: { data: { worldSize: 6144, maxZoom: 5 } } })
    const result = await getMapInfo()
    expect(mockGet).toHaveBeenCalledWith('/api/map/info')
    expect(result.worldSize).toBe(6144)
  })

  it('getMapMarkers calls GET /api/map/markers', async () => {
    mockGet.mockResolvedValue({ data: { data: [] } })
    expect(await getMapMarkers()).toEqual([])
    expect(mockGet).toHaveBeenCalledWith('/api/map/markers')
  })

  it('getMapPois calls GET /api/map/pois', async () => {
    const pois = [{ name: 'Shotgun Messiah', x: -112, z: -112, sizeX: 64, sizeZ: 64 }]
    mockGet.mockResolvedValue({ data: { data: pois } })
    expect(await getMapPois()).toEqual(pois)
    expect(mockGet).toHaveBeenCalledWith('/api/map/pois')
  })

  it('getTileUrl points at the tile endpoint on this origin', () => {
    expect(getTileUrl(2, 1, 3)).toBe(`${window.location.origin}/api/map/tile/2/1/3`)
  })
})
