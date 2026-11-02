<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useI18n } from 'vue-i18n'
import { SUPPORTED_LOCALES, setLocale, type LocaleCode } from '@/i18n'
import { getPublishedModpack, publicModpackDownloadUrl } from '@/api/modpack'
import { getSteamLoginEnabled, steamLoginUrl } from '@/api/auth'
import type { PublicModpack } from '@/types'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import Select from 'primevue/select'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const { t, locale } = useI18n()

const username = ref('')
const password = ref('')
const loading = ref(false)
const error = ref('')

// "Can't sign in?" help (#110). There's no default password — first boot
// generates a random one — so a fresh install's operator needs to know where
// to find it. Static text only: nothing here reveals server paths or whether
// the install is fresh to an anonymous visitor. Opens itself after a failed
// sign-in, since that's exactly when someone is stuck.
const showHelp = ref(false)

// Player-facing modpack download CTA. Fetched anonymously; resolves to null
// (silently) if no pack is published. We export the URL constant rather than
// fetching the file via axios — the browser handles the streaming download.
const publishedModpack = ref<PublicModpack | null>(null)
const modpackDownloadUrl = publicModpackDownloadUrl

const localeOptions = SUPPORTED_LOCALES.map((l) => ({ label: l.name, value: l.code }))
const selectedLocale = ref(locale.value as string)

function onLocaleChange(val: string) {
  selectedLocale.value = val
  setLocale(val as LocaleCode)
}

function formatModpackSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

// "Sign in through Steam" — only offered once an admin has turned it on. Steam
// sends the browser back here as #/login?steam_code=… (or ?steam_error=…).
const steamEnabled = ref(false)
const steamSigningIn = ref(false)
const STEAM_LOGIN_ERRORS = ['not_linked', 'cancelled', 'expired', 'disabled', 'session_mismatch', 'steam_unreachable', 'invalid']

async function finishSteamLogin() {
  const { steam_code: code, steam_error: steamError } = route.query
  if (typeof steamError === 'string') {
    const key = STEAM_LOGIN_ERRORS.includes(steamError) ? steamError : 'invalid'
    error.value = t(`login.steamErrors.${key}`)
  }
  if (typeof code !== 'string') {
    if (steamError) router.replace({ query: {} })
    return
  }

  steamSigningIn.value = true
  try {
    await auth.loginWithSteamCode(code)
    await router.push({ name: 'Dashboard' })
  } catch {
    error.value = t('login.steamErrors.expired')
    router.replace({ query: {} })
  } finally {
    steamSigningIn.value = false
  }
}

onMounted(async () => {
  finishSteamLogin()
  getSteamLoginEnabled().then((enabled) => (steamEnabled.value = enabled))
  publishedModpack.value = await getPublishedModpack()
})

async function handleLogin() {
  if (!username.value || !password.value) {
    error.value = t('login.errorBothRequired')
    return
  }

  loading.value = true
  error.value = ''

  try {
    await auth.login(username.value, password.value)
    await router.push({ name: 'Dashboard' })
  } catch (e: unknown) {
    const err = e as { response?: { data?: { error_description?: string } } }
    error.value = err.response?.data?.error_description || t('login.loginFailed')
    showHelp.value = true
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="login-container">
    <!--
      Modpack download CTA — top-right corner, only renders when admin has
      published a pack. No auth needed; clicking goes straight to the
      streamed-zip endpoint. Title attribute shows size + mod count on hover.
    -->
    <a
      v-if="publishedModpack"
      :href="modpackDownloadUrl"
      class="modpack-cta"
      :title="t('modpack.ctaTooltip', {
        size: formatModpackSize(publishedModpack.sizeBytes),
        n: publishedModpack.modCount,
      })"
    >
      <i class="pi pi-download modpack-cta-icon" />
      <span class="modpack-cta-text">
        <span class="modpack-cta-name">{{ publishedModpack.name }}</span>
        <span class="modpack-cta-version">v{{ publishedModpack.version }}</span>
      </span>
    </a>

    <div class="login-card">
      <div class="login-header">
        <img
          src="/kitsune-command-logo-transparent.png"
          alt="KitsuneCommand"
          class="login-logo"
        />
        <h1 class="login-title">{{ t('login.title') }}</h1>
        <p class="login-subtitle">{{ t('login.subtitle') }}</p>
      </div>

      <form @submit.prevent="handleLogin" class="login-form">
        <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>

        <div class="field">
          <label for="username">{{ t('login.username') }}</label>
          <InputText
            id="username"
            v-model="username"
            :placeholder="t('login.usernamePlaceholder')"
            :disabled="loading"
            class="w-full"
          />
        </div>

        <div class="field">
          <label for="password">{{ t('login.password') }}</label>
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
          :label="t('login.signIn')"
          :loading="loading"
          class="w-full login-button"
          icon="pi pi-sign-in"
        />

        <template v-if="steamEnabled">
          <div class="login-divider"><span>{{ t('login.or') }}</span></div>
          <a
            :href="steamLoginUrl"
            class="steam-button"
            :class="{ busy: steamSigningIn }"
            :aria-disabled="steamSigningIn"
          >
            <i :class="steamSigningIn ? 'pi pi-spin pi-spinner' : 'pi pi-external-link'" />
            {{ t('login.signInWithSteam') }}
          </a>
        </template>

        <button
          type="button"
          class="help-toggle"
          :aria-expanded="showHelp"
          aria-controls="login-help"
          @click="showHelp = !showHelp"
        >
          <i :class="showHelp ? 'pi pi-chevron-down' : 'pi pi-question-circle'" />
          {{ t('login.cantSignIn') }}
        </button>

        <div v-if="showHelp" id="login-help" class="login-help">
          <p>{{ t('login.helpFirstRun') }}</p>
          <ul>
            <li>{{ t('login.helpConsole') }} <code>FIRST RUN</code></li>
            <li><code>FIRST_RUN_PASSWORD.txt</code> {{ t('login.helpFile') }}</li>
          </ul>
          <p>{{ t('login.helpLockedOut') }}</p>
          <code class="help-command">kcresetpw admin &lt;new-password&gt;</code>
        </div>
      </form>

      <div class="login-footer">
        <span class="footer-label">{{ t('login.footer') }}</span>
        <Select
          :modelValue="selectedLocale"
          @update:modelValue="onLocaleChange"
          :options="localeOptions"
          optionLabel="label"
          optionValue="value"
          class="lang-select"
          size="small"
        />
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
  position: relative;
}

.login-header {
  text-align: center;
  margin-bottom: 2rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
}

.login-logo {
  width: 110px;
  height: 110px;
  object-fit: contain;
  margin-bottom: 0.5rem;
}

.login-title {
  font-size: 2rem;
  font-weight: 700;
  background: linear-gradient(135deg, var(--kc-cyan), var(--kc-orange));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  margin: 0;
}

.lang-select {
  width: 130px;
  font-size: 0.8rem;
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

.login-divider {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  color: var(--kc-text-secondary);
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.login-divider::before,
.login-divider::after {
  content: '';
  flex: 1;
  border-top: 1px solid var(--kc-border);
}

/* Steam's own dark slate, so it reads as "the Steam option" next to our cyan. */
.steam-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  padding: 0.65rem 1rem;
  border-radius: 6px;
  border: 1px solid #2a475e;
  background: #171a21;
  color: #c7d5e0;
  font-weight: 600;
  text-decoration: none;
  transition: background-color 150ms ease, border-color 150ms ease;
}

.steam-button:hover,
.steam-button:focus-visible {
  background: #1b2838;
  border-color: #66c0f4;
  color: #fff;
}

.steam-button.busy {
  pointer-events: none;
  opacity: 0.7;
}

.help-toggle {
  align-self: center;
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  background: none;
  border: none;
  padding: 0.25rem;
  color: var(--kc-text-secondary);
  font: inherit;
  font-size: 0.85rem;
  cursor: pointer;
}

.help-toggle:hover,
.help-toggle:focus-visible {
  color: var(--kc-cyan);
}

.login-help {
  border: 1px solid var(--kc-border);
  border-radius: 8px;
  padding: 0.9rem 1rem;
  font-size: 0.85rem;
  line-height: 1.5;
  color: var(--kc-text-secondary);
}

.login-help p {
  margin: 0 0 0.5rem;
}

.login-help ul {
  margin: 0 0 0.75rem;
  padding-left: 1.2rem;
}

.login-help li + li {
  margin-top: 0.35rem;
}

.login-help code {
  font-size: 0.8rem;
  color: var(--kc-text-primary, #fff);
  overflow-wrap: anywhere;
}

.help-command {
  display: block;
  padding: 0.45rem 0.6rem;
  border-radius: 6px;
  background: rgba(0, 0, 0, 0.3);
}

.login-footer {
  margin-top: 1.5rem;
  color: var(--kc-text-secondary);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
}

.footer-label {
  font-size: 0.8rem;
  letter-spacing: 0.1em;
  text-transform: uppercase;
}

/* PrimeVue Password wraps its input — make the outer wrapper AND the inner
   input both span the full width so it matches the username field pixel-for-pixel
   and the eye-toggle sits inside the input instead of floating next to it. */
:deep(.p-password) {
  width: 100%;
}

:deep(.p-password-input),
:deep(.p-password .p-inputtext) {
  width: 100%;
}

/* Top-right modpack download CTA. Pinned to the page corner rather than
   inside the login card so it's visible even when the card is animating
   in / out and doesn't compete with the form fields. */
.modpack-cta {
  position: absolute;
  top: 1.25rem;
  right: 1.25rem;
  display: inline-flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.55rem 0.95rem;
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
  border-radius: 999px;
  color: var(--kc-text-secondary);
  text-decoration: none;
  font-size: 0.85rem;
  font-weight: 500;
  transition: background-color 150ms ease, border-color 150ms ease, color 150ms ease;
}

.modpack-cta:hover {
  background: var(--kc-bg-elevated, rgba(255, 255, 255, 0.04));
  border-color: var(--kc-cyan);
  color: var(--kc-text-primary, #fff);
}

.modpack-cta-icon {
  font-size: 1rem;
  color: var(--kc-cyan);
}

.modpack-cta-text { display: inline-flex; align-items: baseline; gap: 0.4rem; }
.modpack-cta-name { font-weight: 600; }
.modpack-cta-version {
  font-variant-numeric: tabular-nums;
  color: var(--kc-text-secondary);
  font-size: 0.78rem;
}

@media (max-width: 640px) {
  .login-card { padding: 1.5rem; margin: 1rem; }
  .login-title { font-size: 1.5rem; }
  .modpack-cta {
    top: 0.75rem;
    right: 0.75rem;
    padding: 0.45rem 0.8rem;
    font-size: 0.78rem;
  }
}
</style>
