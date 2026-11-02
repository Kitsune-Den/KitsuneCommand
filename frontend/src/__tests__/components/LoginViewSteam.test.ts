// "Sign in through Steam" on the login page: the button only appears once an
// admin turns it on, and the page finishes the round trip Steam sends back as
// #/login?steam_code=… or ?steam_error=….
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import PrimeVue from 'primevue/config'

const mockLoginWithSteamCode = vi.fn()
const mockPush = vi.fn()
const mockReplace = vi.fn()
const mockSteamEnabled = vi.fn()
let routeQuery: Record<string, string> = {}

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: routeQuery }),
  useRouter: () => ({ push: mockPush, replace: mockReplace }),
}))
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ login: vi.fn(), loginWithSteamCode: mockLoginWithSteamCode }),
}))
vi.mock('@/api/auth', () => ({
  getSteamLoginEnabled: () => mockSteamEnabled(),
  steamLoginUrl: '/api/auth/steam/login',
}))
vi.mock('@/api/modpack', () => ({
  getPublishedModpack: () => Promise.resolve(null),
  publicModpackDownloadUrl: '/api/modpack/public/download',
}))
vi.mock('@/i18n', () => ({ SUPPORTED_LOCALES: [{ code: 'en', name: 'English' }], setLocale: vi.fn() }))

import LoginView from '@/views/LoginView.vue'

function mountView() {
  return mount(LoginView, {
    global: {
      plugins: [[PrimeVue, { theme: 'none' }]],
      stubs: {
        InputText: true,
        Password: true,
        Button: { name: 'Button', props: ['label'], template: '<button type="submit">{{ label }}</button>' },
        Message: { name: 'Message', template: '<div class="msg"><slot /></div>' },
        Select: true,
      },
    },
  })
}

describe('LoginView — Sign in through Steam', () => {
  beforeEach(() => {
    routeQuery = {}
    mockLoginWithSteamCode.mockReset()
    mockPush.mockReset()
    mockReplace.mockReset()
    mockSteamEnabled.mockReset().mockResolvedValue(false)
  })

  it('hides the Steam button while Steam sign-in is off', async () => {
    const wrapper = mountView()
    await flushPromises()
    expect(wrapper.find('.steam-button').exists()).toBe(false)
  })

  it('shows a Steam button that starts the round trip on the login listener', async () => {
    mockSteamEnabled.mockResolvedValue(true)
    const wrapper = mountView()
    await flushPromises()

    const button = wrapper.find('a.steam-button')
    expect(button.exists()).toBe(true)
    expect(button.attributes('href')).toBe('/api/auth/steam/login')
    expect(button.text()).toBe('login.signInWithSteam')
  })

  it('trades a steam_code for a session and goes to the dashboard', async () => {
    routeQuery = { steam_code: 'one-time-code' }
    mockLoginWithSteamCode.mockResolvedValue(undefined)

    mountView()
    await flushPromises()

    expect(mockLoginWithSteamCode).toHaveBeenCalledWith('one-time-code')
    expect(mockPush).toHaveBeenCalledWith({ name: 'Dashboard' })
  })

  it('reports an expired code and clears it from the URL', async () => {
    routeQuery = { steam_code: 'stale' }
    mockLoginWithSteamCode.mockRejectedValue({ response: { status: 400 } })

    const wrapper = mountView()
    await flushPromises()

    expect(mockPush).not.toHaveBeenCalled()
    expect(wrapper.find('.msg').text()).toBe('login.steamErrors.expired')
    expect(mockReplace).toHaveBeenCalledWith({ query: {} })
  })

  it('explains a steam_error from the callback', async () => {
    routeQuery = { steam_error: 'not_linked' }
    const wrapper = mountView()
    await flushPromises()

    expect(mockLoginWithSteamCode).not.toHaveBeenCalled()
    expect(wrapper.find('.msg').text()).toBe('login.steamErrors.not_linked')
    expect(mockReplace).toHaveBeenCalledWith({ query: {} })
  })

  it('falls back to a generic message for an unknown steam_error', async () => {
    routeQuery = { steam_error: '<script>' }
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.find('.msg').text()).toBe('login.steamErrors.invalid')
  })
})
