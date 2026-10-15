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

const mockToastAdd = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: mockToastAdd }) }))

// Mock PrimeVue components as stubs. The table renders one .dt-row per row and
// each Column renders its #body slot for that row, so the real cell templates run.
vi.mock('primevue/datatable', async () => {
  const { defineComponent, h, provide } = await import('vue')
  const Row = defineComponent({
    props: ['row'],
    setup(props, { slots }) {
      provide('dtRow', props.row)
      return () => h('div', { class: 'dt-row' }, slots.default?.())
    },
  })
  return {
    default: defineComponent({
      name: 'DataTable',
      props: ['value', 'loading', 'paginator', 'rows', 'totalRecords', 'first', 'lazy', 'dataKey'],
      setup(props, { slots }) {
        return () => h('div', [
          ...(props.value ?? []).map((row: any) => h(Row, { row, key: row.key }, () => slots.default?.())),
          slots.empty?.(),
        ])
      },
    }),
  }
})
vi.mock('primevue/column', async () => {
  const { defineComponent, h, inject } = await import('vue')
  return {
    default: defineComponent({
      name: 'Column',
      props: ['field', 'header'],
      setup(_, { slots }) {
        const row = inject<any>('dtRow', null)
        return () => (row ? h('div', { class: 'dt-cell' }, slots.body?.({ data: row })) : null)
      },
    }),
  }
})
vi.mock('primevue/tag', () => ({ default: { name: 'Tag', template: '<span class="tag">{{ value }}</span>', props: ['value', 'severity'] } }))
vi.mock('primevue/dialog', () => ({ default: { name: 'Dialog', template: '<div v-if="visible"><slot /><slot name="footer" /></div>', props: ['visible', 'header', 'modal'] } }))
vi.mock('primevue/select', () => ({ default: { name: 'Select', template: '<select />', props: ['modelValue', 'options'], emits: ['update:modelValue', 'change'] } }))
vi.mock('primevue/selectbutton', () => ({ default: { name: 'SelectButton', template: '<div class="source-select" />', props: ['modelValue', 'options'], emits: ['update:modelValue'] } }))
vi.mock('primevue/inputnumber', () => ({ default: { name: 'InputNumber', template: '<input />', props: ['modelValue'] } }))

// A 1x1 GIF, so a rendered thumbnail never makes a network request.
const PIXEL = 'data:image/gif;base64,R0lGODlhAQABAAAAACw='

function mountComponent() {
  return mount(ModDiscoveryTab, {
    global: {
      plugins: [createPinia(), [PrimeVue, { theme: 'none' }]],
      stubs: {
        Button: { template: '<button @click="$emit(\'click\')">{{ label }}<slot /></button>', props: ['label', 'icon', 'loading', 'disabled', 'severity', 'text', 'rounded', 'size'] },
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
    mockToastAdd.mockReset()
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

    it('renders a site row: details, verified author, likes and a link to the mod page', async () => {
      mockSearchSevenDaysMods.mockResolvedValue({
        mods: [{ ...sevenDaysMod, thumbnailUrl: PIXEL }],
        totalCount: 1, page: 1, totalPages: 1,
      })
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)

      const row = wrapper.find('.dt-row')
      expect(row.find('.mod-name').text()).toBe('KitsuneCommand')
      expect(row.findAll('.tag').map((t) => t.text())).toEqual(['v2.9.0'])
      expect(row.find('.mod-thumbnail').exists()).toBe(true)
      expect(row.find('.verified-icon').exists()).toBe(true)
      expect(row.find('.mod-details').text()).toBe('Server Side Only · V3 Mods, V2 Mods')
      expect(row.find('.pi-heart').exists()).toBe(true)
      expect(row.find('a').attributes('href')).toBe('https://7daystodiemods.com/mods/kitsunecommand')
      expect(row.find('a').text()).toContain('mods.viewOnSevenDaysMods')
    })

    it('tags adult mods 18+ and hides their thumbnail; keeps an existing v prefix', async () => {
      mockSearchSevenDaysMods.mockResolvedValue({
        mods: [{
          ...sevenDaysMod, slug: 'adult', isAdult: true, thumbnailUrl: PIXEL, version: 'v1.2',
          authorVerified: false, summary: null, serverSideName: null, gameVersions: [],
        }],
        totalCount: 1, page: 1, totalPages: 1,
      })
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)

      const row = wrapper.find('.dt-row')
      expect(row.find('.mod-thumbnail').exists()).toBe(false)
      expect(row.findAll('.tag').map((t) => t.text())).toEqual(['v1.2', '18+'])
      expect(row.find('.verified-icon').exists()).toBe(false)
      expect(row.find('.mod-summary').text()).toBe('')
      expect(row.find('.mod-details').exists()).toBe(false)
    })

    it('passes the game version and server side filters as slugs', async () => {
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)

      const selects = wrapper.findAllComponents({ name: 'Select' })
      selects[2].vm.$emit('update:modelValue', 'v3')
      selects[2].vm.$emit('change')
      selects[3].vm.$emit('update:modelValue', 'server-only')
      selects[3].vm.$emit('change')
      await flushPromises()

      expect(mockSearchSevenDaysMods).toHaveBeenLastCalledWith(
        expect.objectContaining({ gameVersion: 'v3', serverSide: 'server-only', page: 1 }),
      )
    })

    it('fetches the filter metadata once, however often the source switches', async () => {
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)
      wrapper.findComponent({ name: 'SelectButton' }).vm.$emit('update:modelValue', 'nexus')
      await flushPromises()
      await switchToSevenDaysMods(wrapper)

      expect(mockGetSevenDaysModsMetadata).toHaveBeenCalledOnce()
      // Back on Nexus the sort resets to its own default.
      expect(mockSearchDiscoverMods).toHaveBeenLastCalledWith(expect.objectContaining({ sort: 'endorsements' }))
    })

    it('leaves the filters at "any" when the metadata fails to load, and still searches', async () => {
      mockGetSevenDaysModsMetadata.mockRejectedValue(new Error('site down'))
      const wrapper = mountComponent()
      await flushPromises()
      await switchToSevenDaysMods(wrapper)

      const selects = wrapper.findAllComponents({ name: 'Select' })
      expect(selects[1].props('options')).toEqual([{ label: 'mods.anyCategory', value: '' }])
      expect(mockSearchSevenDaysMods).toHaveBeenCalledOnce()
    })

    it('shows the site-specific error when a search fails without a server message', async () => {
      const wrapper = mountComponent()
      await flushPromises()
      mockSearchSevenDaysMods.mockRejectedValue(new Error('network'))
      await switchToSevenDaysMods(wrapper)

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', detail: 'mods.failedToLoadSevenDaysMods' }))
    })

    it("prefers the server's error message when there is one", async () => {
      const wrapper = mountComponent()
      await flushPromises()
      mockSearchSevenDaysMods.mockRejectedValue({ response: { data: { message: 'Failed to search 7daystodiemods.com: timeout' } } })
      await switchToSevenDaysMods(wrapper)

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ detail: 'Failed to search 7daystodiemods.com: timeout' }))
    })
  })

  it('renders a Nexus row: plain-text summary, endorsements and a Nexus link', async () => {
    mockSearchDiscoverMods.mockResolvedValue({
      mods: [{ modId: 7, name: 'NexusMod', version: '', author: '', summary: '<b>Bold</b>  <i>claim</i>', endorsements: 1500, downloads: 2_000_000, pictureUrl: null, updatedAt: null }],
      totalCount: 1,
    })
    const wrapper = mountComponent()
    await flushPromises()

    const row = wrapper.find('.dt-row')
    expect(row.find('.mod-summary').text()).toBe('Bold claim')
    expect(row.find('.tag').exists()).toBe(false) // no version
    expect(row.find('.mod-author').text()).toContain('—')
    expect(row.find('.verified-icon').exists()).toBe(false)
    expect(row.find('.pi-thumbs-up').exists()).toBe(true)
    expect(row.text()).toContain('1.5K')
    expect(row.text()).toContain('2.0M')
    expect(row.find('a').attributes('href')).toBe('https://www.nexusmods.com/7daystodie/mods/7')
  })

  it('searches after the typing pauses, from the first page', async () => {
    vi.useFakeTimers()
    try {
      const wrapper = mountComponent()
      await flushPromises()
      mockSearchDiscoverMods.mockClear()

      const input = wrapper.find('input')
      await input.setValue('zom')
      await input.setValue('zombie')
      vi.advanceTimersByTime(399)
      expect(mockSearchDiscoverMods).not.toHaveBeenCalled()
      vi.advanceTimersByTime(1)
      await flushPromises()

      expect(mockSearchDiscoverMods).toHaveBeenCalledOnce()
      expect(mockSearchDiscoverMods).toHaveBeenCalledWith(expect.objectContaining({ q: 'zombie', offset: 0 }))
    } finally {
      vi.useRealTimers()
    }
  })

  it("falls back to Nexus when storage can't be read, and still switches when it can't be written", async () => {
    const blocked = () => {
      throw new Error('storage blocked')
    }
    const getItem = vi.fn(blocked)
    vi.stubGlobal('localStorage', { getItem, setItem: vi.fn(blocked), clear: vi.fn() })
    try {
      const wrapper = mountComponent()
      await flushPromises()
      expect(getItem).toHaveBeenCalled()
      expect(mockSearchDiscoverMods).toHaveBeenCalledOnce()
      expect(mockSearchSevenDaysMods).not.toHaveBeenCalled()

      wrapper.findComponent({ name: 'SelectButton' }).vm.$emit('update:modelValue', '7daystodiemods')
      await flushPromises()
      expect(mockSearchSevenDaysMods).toHaveBeenCalledOnce()
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('shows the Nexus-specific error when a Nexus search fails without a server message', async () => {
    mockSearchDiscoverMods.mockRejectedValue(new Error('network'))
    mountComponent()
    await flushPromises()

    expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', detail: 'mods.failedToLoadDiscover' }))
  })
})
