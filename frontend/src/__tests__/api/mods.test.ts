import { describe, it, expect, vi, beforeEach } from 'vitest'

const mockGet = vi.fn()
const mockPost = vi.fn()
const mockPut = vi.fn()
const mockDelete = vi.fn()

vi.mock('@/api/client', () => ({
  default: {
    get: (...args: any[]) => mockGet(...args),
    post: (...args: any[]) => mockPost(...args),
    put: (...args: any[]) => mockPut(...args),
    delete: (...args: any[]) => mockDelete(...args),
  },
}))

import {
  getMods,
  uploadMod,
  deleteMod,
  toggleMod,
  searchDiscoverMods,
  getDiscoverSettings,
  updateDiscoverSettings,
  validateNexusKey,
  clearDiscoverCache,
  searchSevenDaysMods,
  getSevenDaysModsMetadata,
} from '@/api/mods'

describe('Mods API', () => {
  beforeEach(() => {
    mockGet.mockReset()
    mockPost.mockReset()
    mockPut.mockReset()
    mockDelete.mockReset()
  })

  describe('getMods', () => {
    it('should call GET /api/mods and unwrap data', async () => {
      const mods = [{ folderName: 'TestMod', displayName: 'Test Mod', isEnabled: true }]
      mockGet.mockResolvedValue({ data: { data: mods } })
      const result = await getMods()
      expect(mockGet).toHaveBeenCalledWith('/api/mods')
      expect(result).toEqual(mods)
    })
  })

  describe('uploadMod', () => {
    it('should POST multipart form data with 2min timeout and return the upload result', async () => {
      const file = new File(['content'], 'test.zip', { type: 'application/zip' })
      const uploadResult = {
        sourceFileName: 'test.zip',
        installedMods: [{ folderName: 'TestMod', displayName: 'Test Mod' }],
        warnings: [],
      }
      mockPost.mockResolvedValue({ data: { data: uploadResult } })

      const result = await uploadMod(file)

      expect(mockPost).toHaveBeenCalledWith('/api/mods/upload', expect.any(FormData), {
        headers: { 'Content-Type': 'multipart/form-data' },
        timeout: 120000,
      })
      expect(result).toEqual(uploadResult)
    })
  })

  describe('deleteMod', () => {
    it('should call DELETE with encoded mod name', async () => {
      mockDelete.mockResolvedValue({ data: { message: 'Deleted' } })
      await deleteMod('My Mod')
      expect(mockDelete).toHaveBeenCalledWith('/api/mods/My%20Mod')
    })
  })

  describe('toggleMod', () => {
    it('should POST to toggle endpoint', async () => {
      mockPost.mockResolvedValue({ data: { message: 'Toggled' } })
      await toggleMod('TestMod')
      expect(mockPost).toHaveBeenCalledWith('/api/mods/TestMod/toggle')
    })
  })

  describe('searchDiscoverMods', () => {
    it('should call GET /api/mods/discover/search with params', async () => {
      const result = { mods: [{ modId: 1, name: 'Test' }], totalCount: 100 }
      mockGet.mockResolvedValue({ data: { data: result } })

      const response = await searchDiscoverMods({ q: 'vehicle', sort: 'endorsements', offset: 0, count: 20 })

      expect(mockGet).toHaveBeenCalledWith('/api/mods/discover/search', {
        params: { q: 'vehicle', sort: 'endorsements', offset: 0, count: 20 },
      })
      expect(response.totalCount).toBe(100)
    })

    it('should work with empty search params', async () => {
      mockGet.mockResolvedValue({ data: { data: { mods: [], totalCount: 0 } } })
      await searchDiscoverMods({})
      expect(mockGet).toHaveBeenCalledWith('/api/mods/discover/search', { params: {} })
    })
  })

  describe('getDiscoverSettings', () => {
    it('should call GET /api/mods/discover/settings', async () => {
      const settings = { hasApiKey: true, cacheDurationMinutes: 60 }
      mockGet.mockResolvedValue({ data: { data: settings } })
      const result = await getDiscoverSettings()
      expect(result).toEqual(settings)
    })
  })

  describe('updateDiscoverSettings', () => {
    it('should PUT settings', async () => {
      mockPut.mockResolvedValue({ data: { message: 'Saved' } })
      await updateDiscoverSettings({ apiKey: 'test-key', cacheDurationMinutes: 30 })
      expect(mockPut).toHaveBeenCalledWith('/api/mods/discover/settings', { apiKey: 'test-key', cacheDurationMinutes: 30 })
    })
  })

  describe('validateNexusKey', () => {
    it('should POST api key and return validation result', async () => {
      const result = { name: 'TestUser', isValid: true }
      mockPost.mockResolvedValue({ data: { data: result } })
      const response = await validateNexusKey('my-key')
      expect(mockPost).toHaveBeenCalledWith('/api/mods/discover/validate-key', { apiKey: 'my-key' })
      expect(response.isValid).toBe(true)
    })
  })

  describe('clearDiscoverCache', () => {
    it('should POST to clear-cache endpoint', async () => {
      mockPost.mockResolvedValue({ data: { message: 'Cleared' } })
      await clearDiscoverCache()
      expect(mockPost).toHaveBeenCalledWith('/api/mods/discover/clear-cache')
    })
  })

  describe('searchSevenDaysMods', () => {
    it('should GET the 7daystodiemods search with params and unwrap data', async () => {
      const result = { mods: [], totalCount: 0, page: 2, totalPages: 5 }
      mockGet.mockResolvedValue({ data: { data: result } })
      const params = { q: 'zombie', sort: 'likes', page: 2, limit: 20, category: 'overhaul' }
      const response = await searchSevenDaysMods(params)
      expect(mockGet).toHaveBeenCalledWith('/api/mods/discover/7daystodiemods/search', { params })
      expect(response).toEqual(result)
    })
  })

  describe('getSevenDaysModsMetadata', () => {
    it('should GET the 7daystodiemods metadata and unwrap data', async () => {
      const metadata = { categories: [{ slug: 'overhaul', name: 'Overhaul' }], gameVersions: [], serverSides: [] }
      mockGet.mockResolvedValue({ data: { data: metadata } })
      const response = await getSevenDaysModsMetadata()
      expect(mockGet).toHaveBeenCalledWith('/api/mods/discover/7daystodiemods/metadata')
      expect(response).toEqual(metadata)
    })
  })
})
