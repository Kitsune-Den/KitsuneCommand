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

vi.mock('@/api/mods', () => ({
  searchDiscoverMods: (...args: any[]) => mockSearchDiscoverMods(...args),
  getDiscoverSettings: () => mockGetDiscoverSettings(),
  updateDiscoverSettings: (...args: any[]) => mockUpdateDiscoverSettings(...args),
  validateNexusKey: (...args: any[]) => mockValidateNexusKey(...args),
  clearDiscoverCache: () => mockClearDiscoverCache(),
}))

// Mock PrimeVue components as stubs
vi.mock('primevue/datatable', () => ({ default: { name: 'DataTable', template: '<div><slot /><slot name="empty" /></div>', props: ['value', 'loading', 'paginator', 'rows', 'totalRecords', 'first', 'lazy'] } }))
vi.mock('primevue/column', () => ({ default: { name: 'Column', template: '<div />', props: ['field', 'header'] } }))
vi.mock('primevue/tag', () => ({ default: { name: 'Tag', template: '<span />', props: ['value', 'severity'] } }))
vi.mock('primevue/dialog', () => ({ default: { name: 'Dialog', template: '<div v-if="visible"><slot /><slot name="footer" /></div>', props: ['visible', 'header', 'modal'] } }))
vi.mock('primevue/select', () => ({ default: { name: 'Select', template: '<select />', props: ['modelValue', 'options'] } }))
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

    // Default: settings load fails (no backend), search returns empty
    mockGetDiscoverSettings.mockRejectedValue(new Error('no backend'))
    mockSearchDiscoverMods.mockResolvedValue({ mods: [], totalCount: 0 })
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
})
