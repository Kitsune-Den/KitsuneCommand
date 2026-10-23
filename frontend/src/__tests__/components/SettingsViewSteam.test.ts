// "Sign in through Steam" in SettingsView: the link/unlink card on the Account
// tab (value "0") and the admin's on/off + panel-address card on the Users tab
// (value "1"). Other tabs' data sources are mocked only so onMounted doesn't throw.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
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
  getUsers: () => Promise.resolve([]), createUser: vi.fn(), updateUser: vi.fn(),
  resetUserPassword: vi.fn(), deleteUser: vi.fn(), unlinkUserSteam: vi.fn(),
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
        Select: true, Message: true, InputNumber: true, DataTable: true, Column: true, Tag: true, Dialog: true,
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
