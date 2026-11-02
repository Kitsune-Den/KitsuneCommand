// "Sign in through Steam" in SettingsView: the link/unlink card on the Account
// tab (value "0") and the admin's on/off + panel-address card on the Users tab
// (value "1"). Other tabs' data sources are mocked only so onMounted doesn't throw.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, h, inject, provide } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'

const mockGetSteamAccount = vi.fn()
const mockStartSteamLink = vi.fn()
const mockUnlinkSteam = vi.fn()
const mockGetSteamSettings = vi.fn()
const mockUpdateSteamSettings = vi.fn()
const mockToastAdd = vi.fn()
const mockReplace = vi.fn()
const mockConfirmRequire = vi.fn()
const mockGetUsers = vi.fn()
const mockUnlinkUserSteam = vi.fn()
let routeQuery: Record<string, string> = {}

vi.mock('@/api/auth', () => ({
  changePassword: vi.fn(),
  getSteamAccount: () => mockGetSteamAccount(),
  startSteamLink: () => mockStartSteamLink(),
  unlinkSteam: () => mockUnlinkSteam(),
  getSteamLoginSettings: () => mockGetSteamSettings(),
  updateSteamLoginSettings: (s: unknown) => mockUpdateSteamSettings(s),
}))
vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeQuery }),
  useRouter: () => ({ replace: mockReplace }),
}))
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: mockToastAdd }) }))
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: mockConfirmRequire }) }))
vi.mock('@/api/users', () => ({
  getUsers: () => mockGetUsers(), createUser: vi.fn(), updateUser: vi.fn(),
  resetUserPassword: vi.fn(), deleteUser: vi.fn(), unlinkUserSteam: (id: number) => mockUnlinkUserSteam(id),
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
vi.mock('@/api/vipperks', () => ({
  getVipPerksSettings: () => Promise.resolve({ enabled: false, tiers: [], tierGifts: [] }),
  updateVipPerksSettings: vi.fn(),
  getVipTiers: () => Promise.resolve([]),
}))
vi.mock('@/api/restart', () => ({ getRestartSettings: () => Promise.resolve({}), updateRestartSettings: vi.fn(), triggerRestartNow: vi.fn() }))
vi.mock('@/api/server', () => ({ getTimezones: () => Promise.resolve([]) }))
vi.mock('@/api/tickets', () => ({ getTicketSettings: () => Promise.resolve({}), updateTicketSettings: vi.fn() }))
vi.mock('@/api/discord', () => ({
  getDiscordSettings: () => Promise.resolve({}), updateDiscordSettings: vi.fn(),
  getDiscordStatus: () => Promise.resolve({ isConnected: false }), testDiscordConnection: vi.fn(),
}))
vi.mock('@/api/serverControl', () => ({ restartServer: vi.fn() }))
vi.mock('@/composables/usePermissions', () => ({ usePermissions: () => ({ isAdmin: { value: true } }) }))

import SettingsView from '@/views/SettingsView.vue'

const slotStub = (name: string) => ({ name, template: '<div><slot /></div>' })

// A DataTable that renders its Column children once per row, handing each
// Column's #body slot that row — enough to reach the per-user action buttons.
const RowScope = defineComponent({
  props: { row: { type: Object, required: true } },
  setup(props, { slots }) {
    provide('row', props.row)
    return () => h('div', { class: 'row' }, slots.default?.())
  },
})
const DataTableStub = defineComponent({
  name: 'DataTable',
  props: { value: { type: Array, default: () => [] } },
  setup(props, { slots }) {
    return () => h('div', (props.value as object[]).map((row) => h(RowScope, { row }, () => slots.default?.())))
  },
})
const ColumnStub = defineComponent({
  name: 'Column',
  setup(_, { slots }) {
    const row = inject('row', null)
    return () => (row && slots.body ? h('div', slots.body({ data: row })) : null)
  },
})

function mountView(panel: '0' | '1') {
  return mount(SettingsView, {
    global: {
      plugins: [createPinia(), [PrimeVue, { theme: 'none' }]],
      stubs: {
        Tabs: slotStub('Tabs'),
        TabList: slotStub('TabList'),
        TabPanels: slotStub('TabPanels'),
        Tab: { name: 'Tab', template: '<div><slot /></div>', props: ['value'] },
        TabPanel: { name: 'TabPanel', props: ['value'], template: `<div v-if="value === '${panel}'"><slot /></div>` },
        Card: { name: 'Card', template: '<div class="card"><slot name="title" /><slot name="content" /></div>' },
        Button: { name: 'Button', props: ['label', 'icon'], template: '<button :data-icon="icon">{{ label }}</button>' },
        InputText: { name: 'InputText', props: ['modelValue'], template: '<input class="text" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
        ToggleSwitch: { name: 'ToggleSwitch', props: ['modelValue'], template: '<input type="checkbox" class="toggle" :checked="modelValue" @change="$emit(\'update:modelValue\', $event.target.checked)" />' },
        DataTable: DataTableStub,
        Column: ColumnStub,
        Select: true, Message: true, InputNumber: true, Tag: true, Dialog: true,
      },
    },
  })
}

const byLabel = (wrapper: ReturnType<typeof mountView>, label: string) =>
  wrapper.findAll('button').find((b) => b.text() === label)

describe('SettingsView — Steam sign-in', () => {
  const originalLocation = window.location

  beforeEach(() => {
    routeQuery = {}
    mockGetSteamAccount.mockReset().mockResolvedValue({ enabled: true, linked: false })
    mockStartSteamLink.mockReset()
    mockUnlinkSteam.mockReset().mockResolvedValue(undefined)
    mockGetSteamSettings.mockReset().mockResolvedValue({ enabled: false, publicUrl: null })
    mockUpdateSteamSettings.mockReset()
    mockToastAdd.mockReset()
    mockReplace.mockReset()
    mockConfirmRequire.mockReset()
    mockGetUsers.mockReset().mockResolvedValue([])
    mockUnlinkUserSteam.mockReset().mockResolvedValue(undefined)
  })

  afterEach(() => {
    Object.defineProperty(window, 'location', { value: originalLocation, configurable: true })
  })

  describe('Account tab', () => {
    it('hides the card when Steam sign-in is off and nothing is linked', async () => {
      mockGetSteamAccount.mockResolvedValue({ enabled: false, linked: false })
      const wrapper = mountView('0')
      await flushPromises()
      expect(wrapper.text()).not.toContain('settings.steamSignIn')
    })

    it('sends the browser to Steam when linking', async () => {
      const fakeLocation = { href: 'http://panel.test/#/settings', origin: 'http://panel.test', pathname: '/' }
      Object.defineProperty(window, 'location', { value: fakeLocation, configurable: true })
      mockStartSteamLink.mockResolvedValue('https://steamcommunity.com/openid/login?x=1')

      const wrapper = mountView('0')
      await flushPromises()
      await byLabel(wrapper, 'settings.steamLink')!.trigger('click')
      await flushPromises()

      expect(mockStartSteamLink).toHaveBeenCalled()
      expect(fakeLocation.href).toBe('https://steamcommunity.com/openid/login?x=1')
    })

    it('shows the linked SteamID and unlinks after confirmation', async () => {
      mockGetSteamAccount.mockResolvedValue({ enabled: true, linked: true, steamId: '76561197960287930' })
      const wrapper = mountView('0')
      await flushPromises()

      expect(wrapper.text()).toContain('76561197960287930')
      await byLabel(wrapper, 'settings.steamUnlink')!.trigger('click')
      expect(mockConfirmRequire).toHaveBeenCalled()

      await mockConfirmRequire.mock.calls[0][0].accept()
      expect(mockUnlinkSteam).toHaveBeenCalled()
      expect(mockGetSteamAccount).toHaveBeenCalledTimes(2)
    })

    it('still offers unlinking when Steam sign-in was turned off after linking', async () => {
      mockGetSteamAccount.mockResolvedValue({ enabled: false, linked: true, steamId: '76561197960287930' })
      const wrapper = mountView('0')
      await flushPromises()
      expect(byLabel(wrapper, 'settings.steamUnlink')).toBeTruthy()
    })

    it("shows the server's reason when linking can't start", async () => {
      mockStartSteamLink.mockRejectedValue({ response: { data: { error_description: 'Steam sign-in is not enabled.' } } })
      const wrapper = mountView('0')
      await flushPromises()
      await byLabel(wrapper, 'settings.steamLink')!.trigger('click')
      await flushPromises()

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', detail: 'Steam sign-in is not enabled.' }))
    })

    it('falls back to a generic message when linking fails without a reason', async () => {
      mockStartSteamLink.mockRejectedValue(new Error('network'))
      const wrapper = mountView('0')
      await flushPromises()
      await byLabel(wrapper, 'settings.steamLink')!.trigger('click')
      await flushPromises()

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ detail: 'settings.steamLinkFailed' }))
    })

    it('reports a failed unlink', async () => {
      mockGetSteamAccount.mockResolvedValue({ enabled: true, linked: true, steamId: '76561197960287930' })
      mockUnlinkSteam.mockRejectedValue(new Error('500'))
      const wrapper = mountView('0')
      await flushPromises()
      await byLabel(wrapper, 'settings.steamUnlink')!.trigger('click')
      await mockConfirmRequire.mock.calls[0][0].accept()

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', detail: 'settings.steamUnlinkFailed' }))
    })

    it('hides the card when the account lookup fails', async () => {
      mockGetSteamAccount.mockRejectedValue(new Error('401'))
      const wrapper = mountView('0')
      await flushPromises()
      expect(wrapper.text()).not.toContain('settings.steamSignIn')
    })

    it('confirms a successful link round trip', async () => {
      routeQuery = { steam_linked: '1' }
      mountView('0')
      await flushPromises()

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success', detail: 'settings.steamLinked' }))
      expect(mockReplace).toHaveBeenCalledWith({ query: {} })
    })

    it('maps an unknown steam_error to the generic message', async () => {
      routeQuery = { steam_error: 'something_new' }
      mountView('0')
      await flushPromises()

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ detail: 'settings.steamErrors.invalid' }))
    })

    it('reports the result of a link round trip, then clears it from the URL', async () => {
      routeQuery = { steam_error: 'already_linked' }
      mountView('0')
      await flushPromises()

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({
        severity: 'error',
        detail: 'settings.steamErrors.already_linked',
      }))
      expect(mockReplace).toHaveBeenCalledWith({ query: {} })
    })
  })

  describe('Users tab (admin)', () => {
    it('prefills the panel address from the current page and saves it', async () => {
      Object.defineProperty(window, 'location', {
        value: { href: 'https://panel.example.com/#/settings', origin: 'https://panel.example.com', pathname: '/' },
        configurable: true,
      })
      mockUpdateSteamSettings.mockImplementation((s) => Promise.resolve(s))

      const wrapper = mountView('1')
      await flushPromises()

      const url = wrapper.find('input.text').element as HTMLInputElement
      expect(url.value).toBe('https://panel.example.com')

      await wrapper.find('input.toggle').setValue(true)
      await byLabel(wrapper, 'common.save')!.trigger('click')
      await flushPromises()

      expect(mockUpdateSteamSettings).toHaveBeenCalledWith({ enabled: true, publicUrl: 'https://panel.example.com' })
      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }))
    })

    it('keeps the defaults when the settings fail to load', async () => {
      mockGetSteamSettings.mockRejectedValue(new Error('403'))
      const wrapper = mountView('1')
      await flushPromises()

      expect((wrapper.find('input.text').element as HTMLInputElement).value).toBe('')
    })

    it('falls back to a generic message when saving fails without a reason', async () => {
      mockUpdateSteamSettings.mockRejectedValue(new Error('network'))
      const wrapper = mountView('1')
      await flushPromises()
      await byLabel(wrapper, 'common.save')!.trigger('click')
      await flushPromises()

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ detail: 'settings.failedToSaveSettings' }))
    })

    it("lets an admin remove another user's Steam link", async () => {
      mockGetUsers.mockResolvedValue([
        { id: 7, username: 'mod1', displayName: 'Mod', role: 'moderator', lastLoginAt: null, isActive: true, createdAt: '', steamId: '76561197960287930' },
        { id: 8, username: 'viewer1', displayName: 'V', role: 'viewer', lastLoginAt: null, isActive: true, createdAt: '', steamId: null },
      ])
      const wrapper = mountView('1')
      await flushPromises()

      const unlinkButtons = wrapper.findAll('button[data-icon="pi pi-link"]')
      expect(unlinkButtons).toHaveLength(1)

      await unlinkButtons[0].trigger('click')
      await mockConfirmRequire.mock.calls[0][0].accept()
      await flushPromises()

      expect(mockUnlinkUserSteam).toHaveBeenCalledWith(7)
      expect(mockGetUsers).toHaveBeenCalledTimes(2)
    })

    it("reports a failed admin unlink", async () => {
      mockGetUsers.mockResolvedValue([
        { id: 7, username: 'mod1', displayName: 'Mod', role: 'moderator', lastLoginAt: null, isActive: true, createdAt: '', steamId: '76561197960287930' },
      ])
      mockUnlinkUserSteam.mockRejectedValue(new Error('500'))
      const wrapper = mountView('1')
      await flushPromises()

      await wrapper.find('button[data-icon="pi pi-link"]').trigger('click')
      await mockConfirmRequire.mock.calls[0][0].accept()

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', detail: 'settings.steamUnlinkFailed' }))
    })

    it("shows the server's reason when saving is refused", async () => {
      mockGetSteamSettings.mockResolvedValue({ enabled: false, publicUrl: 'https://panel.example.com' })
      mockUpdateSteamSettings.mockRejectedValue({ response: { data: { message: 'Public URL must be an absolute http:// or https:// address.' } } })

      const wrapper = mountView('1')
      await flushPromises()
      await byLabel(wrapper, 'common.save')!.trigger('click')
      await flushPromises()

      expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({
        severity: 'error',
        detail: 'Public URL must be an absolute http:// or https:// address.',
      }))
    })
  })
})
