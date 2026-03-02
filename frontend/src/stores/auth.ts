import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { login as apiLogin, type TokenResponse } from '@/api/auth'

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(localStorage.getItem('kc_access_token'))
  const refreshToken = ref<string | null>(localStorage.getItem('kc_refresh_token'))
  const username = ref<string | null>(localStorage.getItem('kc_username'))
  const displayName = ref<string | null>(localStorage.getItem('kc_display_name'))
  const role = ref<string | null>(localStorage.getItem('kc_role'))

  const isAuthenticated = computed(() => !!accessToken.value)

  function setTokens(response: TokenResponse) {
    accessToken.value = response.access_token
    refreshToken.value = response.refresh_token ?? null
    username.value = response.username
    displayName.value = response.display_name
    role.value = response.role

    localStorage.setItem('kc_access_token', response.access_token)
    if (response.refresh_token) {
      localStorage.setItem('kc_refresh_token', response.refresh_token)
    }
    localStorage.setItem('kc_username', response.username)
    localStorage.setItem('kc_display_name', response.display_name)
    localStorage.setItem('kc_role', response.role)
  }

  async function login(user: string, password: string) {
    const response = await apiLogin(user, password)
    setTokens(response)
  }

  function logout() {
    accessToken.value = null
    refreshToken.value = null
    username.value = null
    displayName.value = null
    role.value = null

    localStorage.removeItem('kc_access_token')
    localStorage.removeItem('kc_refresh_token')
    localStorage.removeItem('kc_username')
    localStorage.removeItem('kc_display_name')
    localStorage.removeItem('kc_role')
  }

  return {
    accessToken,
    refreshToken,
    username,
    displayName,
    role,
    isAuthenticated,
    login,
    logout,
    setTokens,
  }
})
