import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ModDiscoveryTab from '@/components/ModDiscoveryTab.vue'
import PrimeVue from 'primevue/config'

// Mock all API functions
const mockSearchDiscoverMods = vi.fn()
const mockGetDiscoverSettings = vi.fn()
const mockUpdateDiscoverSettings = vi.fn()
const mockValidateNexusKey = vi.fn()
const mockClearDiscoverCache = vi.fn()
const mockSearchSevenDaysMods = vi.fn()
const mockGetSevenDaysModsMetadata = vi.fn()

vi.mock('@/api/mods', () => ({
  searchDiscoverMods: (...args: any[]) => mockSearchDiscoverMods(...args),
  getDiscoverSettings: () => mockGetDiscoverSettings(),
  updateDiscoverSettings: (...args: any[]) => mockUpdateDiscoverSettings(...args),
  validateNexusKey: (...args: any[]) => mockValidateNexusKey(...args),
  clearDiscoverCache: () => mockClearDiscoverCache(),
  searchSevenDaysMods: (...args: any[]) => mockSearchSevenDaysMods(...args),
  getSevenDaysModsMetadata: () => mockGetSevenDaysModsMetadata(),
}))

// Mock PrimeVue components as stubs
vi.mock('primevue/datatable', () => ({ default: { name: 'DataTable', template: '<div><slot /><slot name="empty" /></div>', props: ['value', 'loading', 'paginator', 'rows', 'totalRecords', 'first', 'lazy'] } }))
vi.mock('primevue/column', () => ({ default: { name: 'Column', template: '<div />', props: ['field', 'header'] } }))
vi.mock('primevue/tag', () => ({ default: { name: 'Tag', template: '<span />', props: ['value', 'severity'] } }))
vi.mock('primevue/dialog', () => ({ default: { name: 'Dialog', template: '<div v-if="visible"><slot /><slot name="footer" /></div>', props: ['visible', 'header', 'modal'] } }))
vi.mock('primevue/select', () => ({ default: { name: 'Select', template: '<select />', props: ['modelValue', 'options'], emits: ['update:modelValue', 'change'] } }))
vi.mock('primevue/selectbutton', () => ({ default: { name: 'SelectButton', template: '<div class="source-select" />', props: ['modelValue', 'options'], emits: ['update:modelValue'] } }))
vi.mock('primevue/inputnumber', () => ({ default: { name: 'InputNumber', template: '<input />', props: ['modelValue'] } }))

function mountComponent() {
  return mount(ModDiscoveryTab, {
    global: {
      plugins: [createPinia(), [PrimeVue, { theme: 'none' }]],
      stubs: {
        Button: { template: '<button @click="$emit(\'click\')"><slot /></button>', props: ['label', 'icon', 'loading', 'disabled', 'severity', 'text', 'rounded', 'size'] },
        InputText: { template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />', props: ['modelValue', 'placeholder', 'type'] },
        Message: { template: '<div class="message"><slot /></div>', props: ['severity', 'closable'] },
      },
    },
  })
}

describe('ModDiscoveryTab', () => {
  beforeEach(() => {
    mockSearchDiscoverMods.mockReset()
    mockGetDiscoverSettings.mockReset()
    mockUpdateDiscoverSettings.mockReset()
    mockValidateNexusKey.mockReset()
    mockClearDiscoverCache.mockReset()
    mockSearchSevenDaysMods.mockReset()
    mockGetSevenDaysModsMetadata.mockReset()
    localStorage.clear()

    // Default: settings load fails (no backend), search returns empty
    mockGetDiscoverSettings.mockRejectedValue(new Error('no backend'))
    mockSearchDiscoverMods.mockResolvedValue({ mods: [], totalCount: 0 })
    mockSearchSevenDaysMods.mockResolvedValue({ mods: [], totalCount: 0, page: 1, totalPages: 0 })
    mockGetSevenDaysModsMetadata.mockResolvedValue({
      categories: [{ slug: 'overhaul', name: 'Overhaul' }],
      gameVersions: [{ slug: 'v3', name: 'V3 Mods' }],
      serverSides: [{ slug: 'server-only', name: 'Server Side Only' }],
    })
  })

  it('renders without errors', async () => {
    const wrapper = mountComponent()
    await flushPromises()
    expect(wrapper.find('.discover-tab').exists()).toBe(true)
  })

  it('calls loadSettings and loadMods on mount', async () => {
    mountComponent()
    await flushPromises()
    expect(mockGetDiscoverSettings).toHaveBeenCalledOnce()
    expect(mockSearchDiscoverMods).toHaveBeenCalledOnce()
  })

  it('passes search params to API', async () => {
    mockSearchDiscoverMods.mockResolvedValue({ mods: [], totalCount: 0 })
    mountComponent()
    await flushPromises()

    expect(mockSearchDiscoverMods).toHaveBeenCalledWith({
      q: '',
      sort: 'endorsements',
      offset: 0,
      count: 20,
    })
  })

  it('shows result count when mods are returned', async () => {
    mockSearchDiscoverMods.mockResolvedValue({
      mods: [{ modId: 1, name: 'TestMod', version: '1.0', author: 'Test', summary: 'A mod', endorsements: 100, downloads: 500, pictureUrl: null, updatedAt: null }],
      totalCount: 42,
    })

    const wrapper = mountComponent()
    await flushPromises()

    expect(wrapper.text()).toContain('42')
  })

  it('loads settings on mount and falls back on error', async () => {
    mockGetDiscoverSettings.mockRejectedValue(new Error('fail'))
    mountComponent()
    await flushPromises()

    // Should still render (fallback settings applied)
    expect(mockSearchDiscoverMods).toHaveBeenCalled()
  })

  describe('7daystodiemods.com source', () => {
    const sevenDaysMod = {
      slug: 'kitsunecommand',
      title: 'KitsuneCommand',
      summary: 'A web admin panel',
      version: '2.9.0',
      author: 'AdaInTheLab',
      authorVerified: true,
      likes: 3,
      downloads: 41,
      thumbnailUrl: null,
      updatedAt: '2026-10-12T08:30:51.700Z',
      categories: ['Application'],
      gameVersions: ['V3 Mods', 'V2 Mods'],
      serverSide: 'server-only',
      serverSideName: 'Server Side Only',
      isAdult: false,
      url: 'https://7daystodiemods.com/mods/kitsunecommand',
    }

    async function switchToSevenDaysMods(wrapper: ReturnType<typeof mountComponent>) {
      wrapper.findComponent({ name: 'SelectButton' }).vm.$emit('update:modelValue', '7daystodiemods')
      await flushPromises()
    }

    it('searches the site with its own sort, page-based', async () => {
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)

      expect(mockGetSevenDaysModsMetadata).toHaveBeenCalledOnce()
      expect(mockSearchSevenDaysMods).toHaveBeenCalledWith({
        q: '',
        sort: 'downloads',
        page: 1,
        limit: 20,
        category: undefined,
        gameVersion: undefined,
        serverSide: undefined,
      })
    })

    it('loads once per switch, not once per changed setting', async () => {
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)
      expect(mockSearchSevenDaysMods).toHaveBeenCalledTimes(1)
    })

    it('remembers the chosen source', async () => {
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)
      wrapper.unmount()

      mockSearchDiscoverMods.mockClear()
      mountComponent()
      await flushPromises()
      expect(mockSearchSevenDaysMods).toHaveBeenCalledTimes(2)
      expect(mockSearchDiscoverMods).not.toHaveBeenCalled()
    })

    it('passes the filters as slugs and starts from page 1', async () => {
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)

      const selects = wrapper.findAllComponents({ name: 'Select' })
      // sort, category, game version, server side
      expect(selects).toHaveLength(4)
      selects[1].vm.$emit('update:modelValue', 'overhaul')
      selects[1].vm.$emit('change')
      await flushPromises()

      expect(mockSearchSevenDaysMods).toHaveBeenLastCalledWith(expect.objectContaining({ category: 'overhaul', page: 1 }))
    })

    it('shows the result count from the site', async () => {
      mockSearchSevenDaysMods.mockResolvedValue({ mods: [sevenDaysMod], totalCount: 1159, page: 1, totalPages: 58 })
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)

      expect(wrapper.text()).toContain('1,159')
    })

    it('hides the Nexus settings button', async () => {
      const wrapper = mountComponent()
      await flushPromises()
      const buttonsOnNexus = wrapper.findAll('button').length
      await switchToSevenDaysMods(wrapper)
      expect(wrapper.findAll('button').length).toBe(buttonsOnNexus - 1)
    })
  })
})
