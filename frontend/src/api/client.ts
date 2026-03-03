import axios from 'axios'
import { useAuthStore } from '@/stores/auth'
import { useToast } from 'primevue/usetoast'
import router from '@/router'

const apiClient = axios.create({
  baseURL: '/',
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
})

// Request interceptor: attach auth token
apiClient.interceptors.request.use((config) => {
  const auth = useAuthStore()
  if (auth.accessToken) {
    config.headers.Authorization = `Bearer ${auth.accessToken}`
  }
  return config
})

// Response interceptor: handle 401 (token expired) and 403 (forbidden)
apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    if (error.response?.status === 401) {
      const auth = useAuthStore()
      auth.logout()
      await router.push({ name: 'Login' })
    } else if (error.response?.status === 403) {
      try {
        const toast = useToast()
        toast.add({
          severity: 'error',
          summary: 'Access Denied',
          detail: 'You do not have permission to perform this action.',
          life: 4000,
        })
      } catch {
        // Toast may not be available outside component context
      }
    }
    return Promise.reject(error)
  }
)

export default apiClient
