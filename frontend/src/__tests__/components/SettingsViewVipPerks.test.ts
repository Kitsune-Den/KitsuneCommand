// Focused coverage for the VIP Perks tab of SettingsView. We render ONLY that
// panel (the TabPanel stub renders its slot solely for value === "11"), so the
// other tabs' templates never execute — their fetch-on-mount calls are mocked
// to resolve with harmless shapes purely so onMounted doesn't throw.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'

const mockGetVipPerks = vi.fn()
const mockUpdateVipPerks = vi.fn()

vi.mock('@/api/vipperks', () => ({
  getVipPerksSettings: () => mockGetVipPerks(),
  updateVipPerksSettings: (...a: any[]) => mockUpdateVipPerks(...a),
  getVipTiers: () => Promise.resolve([]),
}))

// Every other tab's data source — resolve with shapes safe to assign.
vi.mock('@/api/auth', () => ({ changePassword: vi.fn() }))
vi.mock('@/api/users', () => ({
  getUsers: () => Promise.resolve([]), createUser: vi.fn(), updateUser: vi.fn(),
  resetUserPassword: vi.fn(), deleteUser: vi.fn(),
}))
vi.mock('@/api/settings', () => ({
  getChatCommandSettings: () => Promise.resolve({}), updateChatCommandSettings: vi.fn(),
  getPointsSettings: () => Promise.resolve({}), updatePointsSettings: vi.fn(),
  getTeleportSettings: () => Promise.resolve({}), updateTeleportSettings: vi.fn(),
  getStoreSettings: () => Promise.resolve({}), updateStoreSettings: vi.fn(),
}))
vi.mock('@/api/bloodmoonvote', () => ({ getVoteSettings: () => Promise.resolve({}), updateVoteSettings: vi.fn() }))
vi.mock('@/api/voterewards', () => ({
  getVoteRewardsSettings: () => Promise.resolve({ enabled: false, providers: [] }),
  updateVoteRewardsSettings: vi.fn(),
  getVoteGrants: () => Promise.resolve([]),
}))
vi.mock('@/api/restart', () => ({ getRestartSettings: () => Promise.resolve({}), updateRestartSettings: vi.fn(), triggerRestartNow: vi.fn() }))
vi.mock('@/api/tickets', () => ({ getTicketSettings: () => Promise.resolve({}), updateTicketSettings: vi.fn() }))
vi.mock('@/api/discord', () => ({
  getDiscordSettings: () => Promise.resolve({}), updateDiscordSettings: vi.fn(),
  getDiscordStatus: () => Promise.resolve({ isConnected: false }), testDiscordConnection: vi.fn(),
}))
vi.mock('@/api/serverControl', () => ({ restartServer: vi.fn() }))
vi.mock('@/composables/usePermissions', () => ({ usePermissions: () => ({ isAdmin: { value: true } }) }))
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: vi.fn() }) }))

import SettingsView from '@/views/SettingsView.vue'

const sample = {
  enabled: true,
  tiers: ['VIP'],
  firstLoginPackEnabled: false,
  firstLoginTemplateName: '',
  firstLoginMessage: '',
  tierGifts: [{ tier: 'VIP', templateName: 'crate', period: 'weekly' }],
}

const slotStub = (name: string) => ({ name, template: '<div><slot /></div>' })

function mountView() {
  return mount(SettingsView, {
    global: {
      plugins: [createPinia(), [PrimeVue, { theme: 'none' }]],
      stubs: {
        Tabs: slotStub('Tabs'),
        TabList: slotStub('TabList'),
        TabPanels: slotStub('TabPanels'),
        Tab: { name: 'Tab', template: '<div><slot /></div>', props: ['value'] },
        // Render a panel's content only for the VIP Perks tab.
        TabPanel: { name: 'TabPanel', props: ['value'], template: '<div v-if="value === \'11\'"><slot /></div>' },
        Card: { name: 'Card', template: '<div><slot name="title" /><slot name="subtitle" /><slot name="content" /></div>' },
        // No explicit @click emit: the parent's @click is a native fall-through
        // listener on the stub's root <button>, so emitting here too would fire
        // the handler twice. Let trigger('click') hit the native listener once.
        Button: { name: 'Button', props: ['label', 'icon'], template: '<button :data-icon="icon">{{ label }}</button>' },
        InputText: { name: 'InputText', props: ['modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
        ToggleSwitch: { name: 'ToggleSwitch', props: ['modelValue'], template: '<input type="checkbox" />' },
        Select: { name: 'Select', props: ['modelValue', 'options'], template: '<select />' },
        // Unused-by-this-panel components — empty stubs are fine.
        Message: true, InputNumber: true, DataTable: true, Column: true, Tag: true, Dialog: true,
      },
    },
  })
}

describe('SettingsView — VIP Perks tab', () => {
  beforeEach(() => {
    mockGetVipPerks.mockReset().mockResolvedValue({ ...sample, tierGifts: [...sample.tierGifts], tiers: [...sample.tiers] })
    mockUpdateVipPerks.mockReset().mockResolvedValue(undefined)
  })

  it('fetches VIP perks settings on mount and renders the panel', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(mockGetVipPerks).toHaveBeenCalled()
    expect(wrapper.text()).toContain('settings.vipPerksTitle')
  })

  it('add/remove tier + tier gift and save all run', async () => {
    const wrapper = mountView()
    await flushPromises()

    const byLabel = (label: string) => wrapper.findAll('button').find((b) => b.text() === label)!

    await byLabel('settings.vipPerksAddTier').trigger('click')
    await byLabel('settings.vipPerksAddTierGift').trigger('click')

    const trashes = wrapper.findAll('button[data-icon="pi pi-trash"]')
    await trashes[0].trigger('click')                       // removeVipTier
    await trashes[trashes.length - 1].trigger('click')      // removeTierGift

    await byLabel('settings.saveSettings').trigger('click')
    await flushPromises()

    expect(mockUpdateVipPerks).toHaveBeenCalledTimes(1)
    // Blank tiers are stripped before save.
    const saved = mockUpdateVipPerks.mock.calls[0][0]
    expect(saved.tiers.every((t: string) => t.length > 0)).toBe(true)
    // A reload follows a successful save.
    expect(mockGetVipPerks).toHaveBeenCalledTimes(2)
  })
})
