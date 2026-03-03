<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'

const router = useRouter()
const auth = useAuthStore()

const username = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')

async function handleLogin() {
  if (!username.value || !password.value) {
    error.value = 'Please enter both username and password.'
    return
  }

  loading.value = true
  error.value = ''

  try {
    await auth.login(username.value, password.value)
    await router.push({ name: 'Dashboard' })
  } catch (e: unknown) {
    const err = e as { response?: { data?: { error_description?: string } } }
    error.value = err.response?.data?.error_description || 'Login failed. Check your credentials.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-container">
    <div class="login-card">
      <div class="login-header">
        <h1 class="login-title">KitsuneCommand</h1>
        <p class="login-subtitle">7D2D Server Management</p>
      </div>

      <form @submit.prevent="handleLogin" class="login-form">
        <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>

        <div class="field">
          <label for="username">Username</label>
          <InputText
            id="username"
            v-model="username"
            placeholder="admin"
            :disabled="loading"
            class="w-full"
          />
        </div>

        <div class="field">
          <label for="password">Password</label>
          <Password
            id="password"
            v-model="password"
            :feedback="false"
            :toggleMask="true"
            :disabled="loading"
            class="w-full"
            inputClass="w-full"
            @keyup.enter="handleLogin"
          />
        </div>

        <Button
          type="submit"
          label="Sign In"
          :loading="loading"
          class="w-full login-button"
          icon="pi pi-sign-in"
        />
      </form>

      <div class="login-footer">
        <span>Monitoring | Management | Map</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.login-container {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: linear-gradient(135deg, var(--kc-bg-primary) 0%, #0a1628 100%);
}

.login-card {
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
  border-radius: 12px;
  padding: 2.5rem;
  width: 100%;
  max-width: 420px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
}

.login-header {
  text-align: center;
  margin-bottom: 2rem;
}

.login-title {
  font-size: 2rem;
  font-weight: 700;
  background: linear-gradient(135deg, var(--kc-cyan), var(--kc-orange));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.login-subtitle {
  color: var(--kc-text-secondary);
  margin-top: 0.25rem;
  font-size: 0.9rem;
}

.login-form {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.field label {
  font-size: 0.875rem;
  color: var(--kc-text-secondary);
  font-weight: 500;
}

.w-full {
  width: 100%;
}

.login-button {
  margin-top: 0.5rem;
  background: linear-gradient(135deg, var(--kc-cyan-dark), var(--kc-cyan));
  border: none;
  font-weight: 600;
}

.login-button:hover {
  background: linear-gradient(135deg, var(--kc-cyan), var(--kc-cyan-dark));
}

.login-footer {
  text-align: center;
  margin-top: 1.5rem;
  color: var(--kc-text-secondary);
  font-size: 0.8rem;
  letter-spacing: 0.1em;
  text-transform: uppercase;
}

@media (max-width: 640px) {
  .login-card { padding: 1.5rem; margin: 1rem; }
  .login-title { font-size: 1.5rem; }
}
</style>
