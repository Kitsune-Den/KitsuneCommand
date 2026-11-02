// The "Can't sign in?" help on the login page (#110): hidden until asked for,
// toggled by its button, and opened on its own after a failed sign-in.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import PrimeVue from 'primevue/config'

const mockLogin = vi.fn()
const mockPush = vi.fn()

vi.mock('vue-router', () => ({ useRoute: () => ({ query: {} }), useRouter: () => ({ push: mockPush, replace: vi.fn() }) }))
vi.mock('@/api/auth', () => ({ getSteamLoginEnabled: () => Promise.resolve(false), steamLoginUrl: '/api/auth/steam/login' }))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ login: mockLogin }) }))
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
        InputText: { name: 'InputText', props: ['modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
        Password: { name: 'Password', props: ['modelValue'], template: '<input type="password" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
        Button: { name: 'Button', props: ['label'], template: '<button type="submit">{{ label }}</button>' },
        Message: { name: 'Message', template: '<div class="msg"><slot /></div>' },
        Select: true,
      },
    },
  })
}

describe('LoginView — sign-in help', () => {
  beforeEach(() => {
    mockLogin.mockReset()
    mockPush.mockReset()
  })

  it('is hidden until the toggle is clicked, and the toggle closes it again', async () => {
    const wrapper = mountView()
    const toggle = wrapper.find('.help-toggle')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(wrapper.find('#login-help').exists()).toBe(false)

    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')
    const help = wrapper.find('#login-help')
    expect(help.exists()).toBe(true)
    expect(help.text()).toContain('FIRST_RUN_PASSWORD.txt')
    expect(help.text()).toContain('kcresetpw admin')

    await toggle.trigger('click')
    expect(wrapper.find('#login-help').exists()).toBe(false)
  })

  it('opens on its own after a failed sign-in', async () => {
    mockLogin.mockRejectedValue({ response: { data: { error_description: 'Invalid username or password.' } } })
    const wrapper = mountView()
    const inputs = wrapper.findAll('input')
    await inputs[0].setValue('admin')
    await inputs[1].setValue('admin')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockLogin).toHaveBeenCalledWith('admin', 'admin')
    expect(wrapper.find('.msg').text()).toBe('Invalid username or password.')
    expect(wrapper.find('#login-help').exists()).toBe(true)
  })

  it('stays closed after a successful sign-in', async () => {
    mockLogin.mockResolvedValue(undefined)
    const wrapper = mountView()
    const inputs = wrapper.findAll('input')
    await inputs[0].setValue('admin')
    await inputs[1].setValue('correct-horse')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(mockPush).toHaveBeenCalledWith({ name: 'Dashboard' })
    expect(wrapper.find('#login-help').exists()).toBe(false)
  })
})
