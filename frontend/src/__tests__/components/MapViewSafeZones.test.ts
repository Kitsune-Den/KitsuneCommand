import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import type { SafeZoneSettings } from '@/types'

// ── A fake Leaflet that records what the view draws ──
/* eslint-disable @typescript-eslint/no-explicit-any */
const leaf = vi.hoisted(() => {
  const mapHandlers: Record<string, (e: any) => void> = {}
  const rects: any[] = []

  function layer(): any {
    const l: any = { handlers: {} as Record<string, (e: any) => void> }
    l.addTo = vi.fn(() => l)
    l.clearLayers = vi.fn()
    l.addLayer = vi.fn(() => l)
    l.bindTooltip = vi.fn((text: string) => { l.tooltip = text; return l })
    l.bindPopup = vi.fn(() => l)
    l.on = vi.fn((ev: string, fn: (e: any) => void) => { l.handlers[ev] = fn; return l })
    l.setStyle = vi.fn()
    l.setBounds = vi.fn()
    l.remove = vi.fn()
    return l
  }

  const map: any = {
    on: vi.fn((ev: string, fn: (e: any) => void) => { mapHandlers[ev] = fn; return map }),
    setMaxBounds: vi.fn(),
    setView: vi.fn(),
    invalidateSize: vi.fn(),
    fitBounds: vi.fn(),
    closePopup: vi.fn(),
    remove: vi.fn(),
  }

  const popup: any = {}
  popup.setLatLng = vi.fn(() => popup)
  popup.setContent = vi.fn(() => popup)
  popup.openOn = vi.fn(() => popup)

  const bounds = () => { const b: any = {}; b.pad = vi.fn(() => b); return b }

  const L = {
    map: vi.fn(() => map),
    CRS: { Simple: {} },
    latLng: (lat: number, lng: number) => ({ lat, lng }),
    latLngBounds: vi.fn(() => bounds()),
    tileLayer: vi.fn(() => layer()),
    layerGroup: vi.fn(() => layer()),
    rectangle: vi.fn((_b: unknown, opts: any) => { const r = layer(); r.opts = opts; rects.push(r); return r }),
    circleMarker: vi.fn(() => layer()),
    canvas: vi.fn(() => ({})),
    popup: vi.fn(() => popup),
    control: { layers: vi.fn(() => layer()) },
    DomEvent: { stopPropagation: vi.fn() },
  }
  return { L, map, mapHandlers, rects, popup }
})

vi.mock('leaflet', () => ({ default: leaf.L }))
vi.mock('leaflet/dist/leaflet.css', () => ({}))

// ── Everything else the view talks to ──
const role = vi.hoisted(() => ({ value: 'admin' }))
vi.mock('@/composables/usePermissions', async () => {
  const { computed } = await import('vue')
  return {
    usePermissions: () => ({
      isAdmin: computed(() => role.value === 'admin'),
      isModerator: computed(() => role.value === 'moderator'),
    }),
  }
})

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key} ${JSON.stringify(params)}` : key),
  }),
}))

const mockToastAdd = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: mockToastAdd }) }))

const mockConfirm = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: mockConfirm }) }))

const mockGetMapInfo = vi.fn()
const mockGetMapMarkers = vi.fn()
const mockGetMapPois = vi.fn()
const mockGetLandClaims = vi.fn()
vi.mock('@/api/map', () => ({
  getMapInfo: (...a: unknown[]) => mockGetMapInfo(...a),
  getMapMarkers: (...a: unknown[]) => mockGetMapMarkers(...a),
  getMapPois: (...a: unknown[]) => mockGetMapPois(...a),
  getLandClaims: (...a: unknown[]) => mockGetLandClaims(...a),
}))

const mockGetSafeZones = vi.fn()
const mockSaveSafeZones = vi.fn()
vi.mock('@/api/safeZones', () => ({
  getSafeZones: (...a: unknown[]) => mockGetSafeZones(...a),
  saveSafeZones: (...a: unknown[]) => mockSaveSafeZones(...a),
}))

let resizeCallback: ((entries: any[]) => void) | null = null
class FakeResizeObserver {
  constructor(cb: (entries: any[]) => void) { resizeCallback = cb }
  observe() {}
  disconnect() {}
}

import MapView from '@/views/MapView.vue'

const stubs = {
  Button: {
    props: ['label', 'ariaLabel', 'disabled'],
    emits: ['click'],
    template: '<button :aria-label="ariaLabel" :disabled="disabled" @click="$emit(\'click\')">{{ label }}</button>',
  },
  ToggleSwitch: {
    props: ['modelValue', 'ariaLabel'],
    emits: ['update:modelValue'],
    template: '<input type="checkbox" class="toggle" :aria-label="ariaLabel" :checked="modelValue" @change="$emit(\'update:modelValue\', $event.target.checked)" />',
  },
  Dialog: {
    props: ['visible', 'header'],
    template: '<div v-if="visible" class="dialog"><h3>{{ header }}</h3><slot /><slot name="footer" /></div>',
  },
  InputText: { props: ['modelValue'], template: '<input class="text" />' },
  InputNumber: { props: ['modelValue'], template: '<input class="number" />' },
}

// The view's game <-> CRS.Simple conversion for a 6144-block world
const at = (x: number, z: number) => ({ latlng: { lat: (z - 3072) / 6144 * 256, lng: (x + 3072) / 6144 * 256 } })

function settings(overrides: Partial<SafeZoneSettings> = {}): SafeZoneSettings {
  return {
    enabled: true,
    sweepIntervalSeconds: 2,
    zones: [
      { id: 'town', name: 'Town', enabled: true, minX: 0, minZ: 0, maxX: 9, maxZ: 19, removeAnimals: false, allowBloodMoon: false, removedCount: 7 },
    ],
    ...overrides,
  }
}

function buttonByText(w: VueWrapper, text: string) {
  const b = w.findAll('button').find(x => x.text() === text)
  if (!b) throw new Error(`no button "${text}"`)
  return b
}

const mounted: VueWrapper[] = []
async function mountView() {
  const w = mount(MapView, { global: { stubs } })
  mounted.push(w)
  await flushPromises()
  return w
}

beforeEach(() => {
  vi.clearAllMocks()
  for (const k of Object.keys(leaf.mapHandlers)) delete leaf.mapHandlers[k]
  leaf.rects.length = 0
  role.value = 'admin'
  resizeCallback = null
  vi.stubGlobal('ResizeObserver', FakeResizeObserver)

  mockGetMapInfo.mockResolvedValue({ isAvailable: true, worldSize: 6144, maxZoom: 4, tileSize: 256 })
  mockGetMapMarkers.mockResolvedValue([{ name: 'Ada', x: 1, y: 60, z: 2 }])
  mockGetMapPois.mockResolvedValue([{ name: 'Shop', x: 5, z: 5, sizeX: 10, sizeZ: 10 }])
  mockGetLandClaims.mockResolvedValue({ claimSize: 41, claims: [{ ownerId: '765', ownerName: 'Ada', x: 100, y: 60, z: 200 }] })
  mockGetSafeZones.mockResolvedValue(settings())
  mockSaveSafeZones.mockImplementation(async (s: SafeZoneSettings) => s)
})

afterEach(() => {
  while (mounted.length) mounted.pop()!.unmount()
  vi.unstubAllGlobals()
})

const zoneRects = () => leaf.rects.filter(r => r.opts?.color === '#4ade80' || r.opts?.color === '#9ca3af')
  .filter(r => r.opts?.interactive !== false)
const claimRects = () => leaf.rects.filter(r => r.opts?.dashArray === '4 3')

describe('MapView map layers', () => {
  it('draws safe zones for everyone but hides land claims from viewers', async () => {
    role.value = 'viewer'
    const w = await mountView()

    expect(mockGetSafeZones).toHaveBeenCalledTimes(1)
    expect(mockGetLandClaims).not.toHaveBeenCalled()
    expect(zoneRects()).toHaveLength(1)
    expect(zoneRects()[0].tooltip).toBe('Town')
    expect(zoneRects()[0].handlers.click).toBeUndefined()
    expect(w.text()).not.toContain('map.safeZones')
  })

  it('draws land claims for moderators', async () => {
    role.value = 'moderator'
    await mountView()

    expect(mockGetLandClaims).toHaveBeenCalledTimes(1)
    expect(claimRects()).toHaveLength(1)
    expect(claimRects()[0].tooltip).toContain('map.claimOf {"name":"Ada"}')
    expect(claimRects()[0].tooltip).toContain('100, 60, 200')
  })

  it('still loads when claims and zones fail', async () => {
    mockGetLandClaims.mockRejectedValue(new Error('nope'))
    mockGetSafeZones.mockRejectedValue(new Error('nope'))
    const w = await mountView()

    expect(claimRects()).toHaveLength(0)
    expect(zoneRects()).toHaveLength(0)
    expect(w.find('.map-overlay').exists()).toBe(false)
  })

  it('greys out zones while the master switch is off', async () => {
    mockGetSafeZones.mockResolvedValue(settings({ enabled: false }))
    await mountView()

    const rect = zoneRects()[0]
    expect(rect.opts.color).toBe('#9ca3af')
    expect(rect.tooltip).toBe('Town (map.zoneOff)')
  })

  it('shows coordinates on a map click when not drawing', async () => {
    await mountView()
    leaf.mapHandlers.click(at(10, 20))
    expect(leaf.popup.setContent).toHaveBeenCalledWith(expect.stringContaining('X: 10, Z: 20'))
  })

  it('reports a map that is not available, or fails to load', async () => {
    mockGetMapInfo.mockResolvedValue({ isAvailable: false, worldSize: 0, maxZoom: 0, tileSize: 256 })
    let w = await mountView()
    expect(w.text()).toContain('map.mapNotAvailable')

    mockGetMapInfo.mockRejectedValue(new Error('down'))
    w = await mountView()
    expect(w.text()).toContain('map.failedToLoad')
  })

  it('fits the world once the container has a size, and cleans up on unmount', async () => {
    const w = await mountView()
    resizeCallback!([{ contentRect: { width: 800, height: 600 } }])
    resizeCallback!([{ contentRect: { width: 900, height: 600 } }])
    expect(leaf.map.invalidateSize).toHaveBeenCalledTimes(2)
    expect(leaf.map.fitBounds).toHaveBeenCalledTimes(1)

    w.unmount()
    mounted.length = 0
    expect(leaf.map.remove).toHaveBeenCalled()
  })
})

describe('MapView safe zone editing', () => {
  async function openPanel() {
    const w = await mountView()
    await buttonByText(w, 'map.safeZones').trigger('click')
    await flushPromises()
    return w
  }

  it('lists zones in the panel and re-fetches them when opened', async () => {
    const w = await openPanel()

    expect(mockGetSafeZones).toHaveBeenCalledTimes(2)
    expect(w.find('.zone-panel').exists()).toBe(true)
    expect(w.text()).toContain('Town')
    expect(w.text()).toContain('map.zoneBlocks {"size":"10 × 20"}')
    expect(w.text()).toContain('map.zoneRemoved {"count":7}')

    await w.find('.zone-name').trigger('click')
    expect(leaf.map.fitBounds).toHaveBeenCalled()

    await buttonByText(w, 'map.safeZones').trigger('click')
    expect(w.find('.zone-panel').exists()).toBe(false)
  })

  it('shows an empty state and a paused warning', async () => {
    mockGetSafeZones.mockResolvedValue(settings({ enabled: false, zones: [] }))
    const w = await openPanel()
    expect(w.text()).toContain('map.noZones')
    expect(w.text()).toContain('map.zonesPaused')
  })

  it('draws a new zone from two clicks and saves it', async () => {
    const w = await openPanel()
    await buttonByText(w, 'map.drawZone').trigger('click')
    expect(w.find('.draw-hint').text()).toContain('map.drawFirstCorner')
    expect(leaf.map.closePopup).toHaveBeenCalled()

    leaf.mapHandlers.click(at(50, 80))
    await flushPromises()
    expect(w.find('.draw-hint').text()).toContain('map.drawSecondCorner')
    const preview = leaf.rects.find(r => r.opts?.interactive === false && r.opts?.dashArray === '6 4')
    expect(preview).toBeTruthy()

    leaf.mapHandlers.mousemove(at(30, 60))
    expect(preview.setBounds).toHaveBeenCalled()

    leaf.mapHandlers.click(at(20, 40))
    await flushPromises()
    expect(w.find('.draw-hint').exists()).toBe(false)
    expect(preview.remove).toHaveBeenCalled()
    expect(w.find('.dialog h3').text()).toBe('map.newZone')

    await buttonByText(w, 'common.save').trigger('click')
    await flushPromises()

    const saved = mockSaveSafeZones.mock.calls[0][0] as SafeZoneSettings
    expect(saved.zones).toHaveLength(2)
    expect(saved.zones[1]).toMatchObject({ id: '', minX: 20, minZ: 40, maxX: 50, maxZ: 80, enabled: true })
    expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', detail: 'map.zoneSaved' }))
    expect(w.find('.dialog').exists()).toBe(false)
  })

  it('cancels drawing with Escape, the cancel button, or closing the panel', async () => {
    const w = await openPanel()

    await buttonByText(w, 'map.drawZone').trigger('click')
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await flushPromises()
    expect(w.find('.draw-hint').exists()).toBe(false)

    await buttonByText(w, 'map.drawZone').trigger('click')
    await buttonByText(w, 'map.cancelDrawing').trigger('click')
    expect(w.find('.draw-hint').exists()).toBe(false)

    await buttonByText(w, 'map.drawZone').trigger('click')
    await buttonByText(w, 'map.safeZones').trigger('click')
    expect(w.find('.draw-hint').exists()).toBe(false)
  })

  it('edits a zone when its rectangle is clicked, except while drawing', async () => {
    const w = await openPanel()
    const rect = zoneRects()[0]

    await buttonByText(w, 'map.drawZone').trigger('click')
    rect.handlers.click({})
    await flushPromises()
    expect(w.find('.dialog').exists()).toBe(false)
    await buttonByText(w, 'map.cancelDrawing').trigger('click')

    rect.handlers.click({})
    await flushPromises()
    expect(leaf.L.DomEvent.stopPropagation).toHaveBeenCalled()
    expect(w.find('.dialog h3').text()).toBe('map.editZone')

    await buttonByText(w, 'common.save').trigger('click')
    await flushPromises()
    const saved = mockSaveSafeZones.mock.calls[0][0] as SafeZoneSettings
    expect(saved.zones).toHaveLength(1)
    expect(saved.zones[0].id).toBe('town')
  })

  it('refuses to save a zone with a cleared coordinate', async () => {
    const w = await openPanel()
    await w.find('button[aria-label="common.edit"]').trigger('click')
    const vm = w.vm as any
    // The InputNumber stub can't clear itself; set what a cleared box gives
    vm.$.setupState.editZone.minX = null
    await buttonByText(w, 'common.save').trigger('click')
    await flushPromises()

    expect(mockSaveSafeZones).not.toHaveBeenCalled()
    expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'warn', detail: 'map.zoneCoordsRequired' }))
  })

  it('keeps the dialog open and says so when saving fails', async () => {
    mockSaveSafeZones.mockRejectedValue(new Error('500'))
    const w = await openPanel()
    await w.find('button[aria-label="common.edit"]').trigger('click')
    await buttonByText(w, 'common.save').trigger('click')
    await flushPromises()

    expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', detail: 'map.zoneSaveFailed' }))
    expect(w.find('.dialog').exists()).toBe(true)
  })

  it('deletes a zone after confirming, from the list or the dialog', async () => {
    const w = await openPanel()
    await w.find('button[aria-label="common.delete"]').trigger('click')

    expect(mockConfirm).toHaveBeenCalledWith(expect.objectContaining({ message: 'map.zoneDeleteConfirm {"name":"Town"}' }))
    await mockConfirm.mock.calls[0][0].accept()
    await flushPromises()
    expect((mockSaveSafeZones.mock.calls[0][0] as SafeZoneSettings).zones).toHaveLength(0)
    expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ detail: 'map.zoneDeleted' }))

    mockGetSafeZones.mockResolvedValue(settings())
    await buttonByText(w, 'map.safeZones').trigger('click')
    await buttonByText(w, 'map.safeZones').trigger('click')
    await flushPromises()
    await w.find('button[aria-label="common.edit"]').trigger('click')
    await w.find('.dialog').findAll('button').find(b => b.text() === 'common.delete')!.trigger('click')
    expect(mockConfirm).toHaveBeenCalledTimes(2)
  })

  it('switches one zone or all zones on and off', async () => {
    const w = await openPanel()

    const zoneToggle = w.find('input[aria-label="map.zoneEnabled"]')
    ;(zoneToggle.element as HTMLInputElement).checked = false
    await zoneToggle.trigger('change')
    await flushPromises()
    expect((mockSaveSafeZones.mock.calls[0][0] as SafeZoneSettings).zones[0].enabled).toBe(false)

    const master = w.find('input[aria-label="map.zonesMasterSwitch"]')
    ;(master.element as HTMLInputElement).checked = false
    await master.trigger('change')
    await flushPromises()
    expect((mockSaveSafeZones.mock.calls[1][0] as SafeZoneSettings).enabled).toBe(false)
  })
})

