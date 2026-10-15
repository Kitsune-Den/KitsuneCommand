// The installed-mods update badges after "Check for updates": each one links to
// the site that answered (Nexus or 7daystodiemods.com) and its tooltip names it.
// Only the Installed tab renders; the Discover tab component is stubbed out.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import PrimeVue from 'primevue/config'

const mockGetMods = vi.fn()
const mockCheckModUpdates = vi.fn()

// Translations echo their params so the tooltip's site name is checkable.
vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key} ${JSON.stringify(params)}` : key),
    locale: { value: 'en' },
  }),
}))
vi.mock('@/api/mods', () => ({
  getMods: () => mockGetMods(),
  checkModUpdates: () => mockCheckModUpdates(),
  uploadMod: vi.fn(),
  deleteMod: vi.fn(),
  toggleMod: vi.fn(),
}))
vi.mock('@/api/modpack', () => ({
  getModpackState: () => Promise.resolve({ modpack: null, modList: [] }),
  saveModpackDraft: vi.fn(),
  buildModpack: vi.fn(),
  publishModpack: vi.fn(),
  archiveModpack: vi.fn(),
  unarchiveModpack: vi.fn(),
  deleteModpack: vi.fn(),
  publicModpackDownloadUrl: '/api/modpack/public/download',
}))
vi.mock('@/components/ModDiscoveryTab.vue', () => ({ default: { name: 'ModDiscoveryTab', template: '<div />' } }))

// One .dt-row per row; each Column renders its #body slot for that row.
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
      props: ['value', 'loading', 'paginator', 'rows'],
      setup(props, { slots }) {
        return () => h('div', [
          ...(props.value ?? []).map((row: any) => h(Row, { row, key: row.folderName }, () => slots.default?.())),
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

import ModsView from '@/views/ModsView.vue'

const slot = (name: string) => ({ name, template: '<div><slot /></div>' })

function mountView() {
  return mount(ModsView, {
    global: {
      plugins: [[PrimeVue, { theme: 'none' }]],
      stubs: {
        Tabs: slot('Tabs'),
        TabList: slot('TabList'),
        Tab: { name: 'Tab', props: ['value'], template: '<div><slot /></div>' },
        TabPanels: slot('TabPanels'),
        // Only the Installed tab.
        TabPanel: { name: 'TabPanel', props: ['value'], template: '<div v-if="value === \'0\'"><slot /></div>' },
        Button: { name: 'Button', props: ['label', 'icon'], template: '<button class="btn">{{ label }}</button>' },
        InputText: true, Textarea: true, MultiSelect: true, Tag: true, Dialog: true, Message: true, FileUpload: true,
      },
    },
  })
}

const installed = (folderName: string, version: string) => ({
  folderName, displayName: folderName, version, author: null, description: null,
  website: null, folderSize: 1024, isEnabled: true, isProtected: false,
})

async function checkForUpdates(wrapper: ReturnType<typeof mountView>) {
  const button = wrapper.findAll('button').find((b) => b.text() === 'mods.checkForUpdates')
  await button!.trigger('click')
  await flushPromises()
}

describe('ModsView — update badges', () => {
  beforeEach(() => {
    mockGetMods.mockReset().mockResolvedValue([installed('SiteMod', '1.0'), installed('NexusMod', '2.0'), installed('OddMod', 'beta')])
    mockCheckModUpdates.mockReset().mockResolvedValue([
      {
        folderName: 'SiteMod', status: 'update_available', installedVersion: '1.0', latestVersion: '1.1',
        source: '7daystodiemods', url: 'https://7daystodiemods.com/mods/sitemod',
        nexusModId: null, nexusUrl: null, matchedName: 'SiteMod',
      },
      {
        // An older backend: no source/url, only the Nexus link.
        folderName: 'NexusMod', status: 'update_available', installedVersion: '2.0', latestVersion: '2.1',
        source: null, url: null,
        nexusModId: 42, nexusUrl: 'https://www.nexusmods.com/7daystodie/mods/42', matchedName: 'NexusMod',
      },
      {
        folderName: 'OddMod', status: 'version_differs', installedVersion: 'beta', latestVersion: 'gamma',
        source: '7daystodiemods', url: 'https://7daystodiemods.com/mods/oddmod',
        nexusModId: null, nexusUrl: null, matchedName: 'OddMod',
      },
    ])
  })

  function badgeFor(wrapper: ReturnType<typeof mountView>, folder: string) {
    const row = wrapper.findAll('.dt-row').find((r) => r.find('.mod-name').text() === folder)!
    return row.find('a.update-badge')
  }

  it('links an available update to the site that has it and names that site', async () => {
    const wrapper = mountView()
    await flushPromises()
    await checkForUpdates(wrapper)

    const badge = badgeFor(wrapper, 'SiteMod')
    expect(badge.classes()).toContain('update-badge--available')
    expect(badge.attributes('href')).toBe('https://7daystodiemods.com/mods/sitemod')
    expect(badge.attributes('title')).toContain('"source":"mods.sourceSevenDaysMods"')
    expect(badge.attributes('title')).toContain('"latest":"1.1"')
  })

  it('falls back to the Nexus link and name when the result has no source', async () => {
    const wrapper = mountView()
    await flushPromises()
    await checkForUpdates(wrapper)

    const badge = badgeFor(wrapper, 'NexusMod')
    expect(badge.attributes('href')).toBe('https://www.nexusmods.com/7daystodie/mods/42')
    expect(badge.attributes('title')).toContain('"source":"mods.sourceNexus"')
  })

  it('links a "version differs" badge the same way', async () => {
    const wrapper = mountView()
    await flushPromises()
    await checkForUpdates(wrapper)

    const badge = badgeFor(wrapper, 'OddMod')
    expect(badge.classes()).toContain('update-badge--differs')
    expect(badge.attributes('href')).toBe('https://7daystodiemods.com/mods/oddmod')
    expect(badge.attributes('title')).toContain('"source":"mods.sourceSevenDaysMods"')
  })

  it('falls back to a dead link when a result has no page at all', async () => {
    mockCheckModUpdates.mockResolvedValue([{
      folderName: 'SiteMod', status: 'update_available', installedVersion: '1.0', latestVersion: '1.1',
      source: null, url: null, nexusModId: null, nexusUrl: null, matchedName: null,
    }])
    const wrapper = mountView()
    await flushPromises()
    await checkForUpdates(wrapper)

    expect(badgeFor(wrapper, 'SiteMod').attributes('href')).toBe('#')
  })

  it('shows no badges before a check', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.findAll('a.update-badge')).toHaveLength(0)
  })
})
