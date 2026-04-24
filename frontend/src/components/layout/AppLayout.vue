<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useServerStore } from '@/stores/server'
import { useAppWebSocket } from '@/composables/useAppWebSocket'
import { useI18n } from 'vue-i18n'
import { SUPPORTED_LOCALES, setLocale, type LocaleCode } from '@/i18n'
import Button from 'primevue/button'
import Select from 'primevue/select'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const server = useServerStore()
const appWs = useAppWebSocket()
const { t, locale } = useI18n()
const sidebarCollapsed = ref(false)
const mobileMenuOpen = ref(false)

const navItems = computed(() => [
  { label: t('nav.dashboard'), icon: 'pi pi-home', route: '/' },
  { label: t('nav.players'), icon: 'pi pi-users', route: '/players' },
  { label: t('nav.map'), icon: 'pi pi-map', route: '/map' },
  { label: t('nav.chat'), icon: 'pi pi-comments', route: '/chat' },
  { label: t('nav.tickets'), icon: 'pi pi-ticket', route: '/tickets' },
  { label: t('nav.teleport'), icon: 'pi pi-compass', route: '/teleport/cities' },
  { label: t('nav.vipGifts'), icon: 'pi pi-gift', route: '/vipgifts' },
  { label: t('nav.schedules'), icon: 'pi pi-clock', route: '/schedules' },
  { label: t('nav.cdKeys'), icon: 'pi pi-key', route: '/cdkeys' },
  { label: t('nav.economy'), icon: 'pi pi-wallet', route: '/economy/points' },
  { label: t('nav.itemDatabase'), icon: 'pi pi-database', route: '/items' },
  { type: 'divider', label: t('nav.serverManagement') },
  { label: t('nav.serverControl'), icon: 'pi pi-server', route: '/server' },
  { label: t('nav.serverUpdate'), icon: 'pi pi-sync', route: '/server-update' },
  { label: t('nav.console'), icon: 'pi pi-code', route: '/console' },
  { label: t('nav.configEditor'), icon: 'pi pi-file-edit', route: '/config' },
  { label: t('nav.mods'), icon: 'pi pi-box', route: '/mods' },
  { label: t('nav.backups'), icon: 'pi pi-cloud-download', route: '/backups' },
  { type: 'divider' },
  { label: t('nav.settings'), icon: 'pi pi-cog', route: '/settings' },
])

const localeOptions = SUPPORTED_LOCALES.map((l) => ({ label: l.name, value: l.code }))
const selectedLocale = ref(locale.value as string)

watch(selectedLocale, (val) => {
  setLocale(val as LocaleCode)
})

// Cycle through languages when sidebar is collapsed
const localeIndex = computed(() => SUPPORTED_LOCALES.findIndex((l) => l.code === locale.value))
function cycleLocale() {
  const nextIdx = (localeIndex.value + 1) % SUPPORTED_LOCALES.length
  const next = SUPPORTED_LOCALES[nextIdx].code
  selectedLocale.value = next
  setLocale(next)
}

function toggleSidebar() {
  sidebarCollapsed.value = !sidebarCollapsed.value
}

function toggleMobileMenu() {
  mobileMenuOpen.value = !mobileMenuOpen.value
}

function closeMobileMenu() {
  mobileMenuOpen.value = false
}

// Auto-close mobile sidebar on route change
watch(() => route.path, () => {
  mobileMenuOpen.value = false
})

async function handleLogout() {
  appWs.destroy()
  auth.logout()
  await router.push({ name: 'Login' })
}

onMounted(() => {
  appWs.init()
  server.fetchKcVersion()
})

onUnmounted(() => {
  appWs.destroy()
})
</script>

<template>
  <div class="app-layout">
    <!-- Mobile backdrop -->
    <div
      v-if="mobileMenuOpen"
      class="mobile-backdrop"
      @click="closeMobileMenu"
    />

    <!-- Sidebar -->
    <aside class="sidebar" :class="{ collapsed: sidebarCollapsed, 'mobile-open': mobileMenuOpen }">
      <div class="sidebar-header">
        <div class="brand" v-if="!sidebarCollapsed">
          <img
            src="/kitsune-command-logo-transparent.png"
            alt="KitsuneCommand"
            class="brand-logo"
          />
          <h2 class="brand-name">{{ t('layout.brandName') }}</h2>
          <span class="brand-version" v-if="server.kcVersion">v{{ server.kcVersion }}</span>
        </div>
        <Button
          :icon="sidebarCollapsed ? 'pi pi-angle-right' : 'pi pi-angle-left'"
          text
          rounded
          severity="secondary"
          @click="toggleSidebar"
          class="collapse-btn desktop-only"
        />
        <Button
          icon="pi pi-times"
          text
          rounded
          severity="secondary"
          @click="closeMobileMenu"
          class="collapse-btn mobile-only"
        />
      </div>

      <nav class="sidebar-nav">
        <template v-for="(item, idx) in navItems" :key="item.route || `div-${idx}`">
          <div v-if="item.type === 'divider'" class="nav-divider">
            <span v-if="item.label && !sidebarCollapsed" class="divider-label">{{ item.label }}</span>
            <hr v-else class="divider-line" />
          </div>
          <router-link
            v-else
            :to="item.route!"
            class="nav-item"
            :class="{ 'nav-item--active': item.route === '/' ? route.path === '/' : (route.path === item.route || route.path.startsWith(item.route! + '/')) }"
            :title="sidebarCollapsed ? item.label : undefined"
          >
            <i :class="item.icon" class="nav-icon"></i>
            <span v-if="!sidebarCollapsed" class="nav-label">{{ item.label }}</span>
          </router-link>
        </template>
      </nav>

      <div class="sidebar-footer">
        <!-- Language switcher -->
        <div class="lang-switcher" v-if="!sidebarCollapsed">
          <Select
            v-model="selectedLocale"
            :options="localeOptions"
            optionLabel="label"
            optionValue="value"
            class="lang-select"
          />
        </div>
        <Button
          v-else
          icon="pi pi-globe"
          text
          rounded
          severity="secondary"
          @click="cycleLocale"
          class="lang-cycle-btn"
          :title="SUPPORTED_LOCALES[localeIndex]?.name"
        />

        <!-- Connection indicator -->
        <div class="ws-status" v-if="!sidebarCollapsed">
          <i class="pi pi-circle-fill" :class="{ connected: appWs.isConnected.value }" />
          <span>{{ appWs.isConnected.value ? t('layout.live') : t('layout.offline') }}</span>
        </div>
        <div class="user-info" v-if="!sidebarCollapsed">
          <i class="pi pi-user"></i>
          <span>{{ auth.displayName || auth.username }}</span>
          <span class="role-badge" :class="`role-badge--${auth.role}`">{{ auth.role }}</span>
        </div>
        <Button
          :icon="'pi pi-sign-out'"
          :label="sidebarCollapsed ? undefined : t('layout.logout')"
          text
          severity="danger"
          size="small"
          @click="handleLogout"
          class="logout-btn"
        />
      </div>
    </aside>

    <!-- Main Content -->
    <main class="main-content">
      <Button
        icon="pi pi-bars"
        text
        rounded
        severity="secondary"
        @click="toggleMobileMenu"
        class="mobile-hamburger mobile-only"
      />
      <router-view />
    </main>
  </div>
</template>

<style scoped>
.app-layout { display: flex; height: 100vh; overflow: hidden; }

.sidebar {
  width: 240px; background: var(--kc-bg-secondary);
  border-right: 1px solid var(--kc-border);
  display: flex; flex-direction: column; transition: width 0.2s ease;
}

.sidebar.collapsed { width: 64px; }

.sidebar-header {
  display: flex; align-items: center; justify-content: space-between;
  padding: 1rem; border-bottom: 1px solid var(--kc-border); min-height: 64px;
}

/* Brand block — logo + title + version, stacked and centered under the logo */
.brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.25rem;
  flex: 1;
}

.brand-logo {
  width: 72px;
  height: 72px;
  object-fit: contain;
  margin-bottom: 0.25rem;
  /* The PNG has baked-in glow; no need for additional filter.
     Image is responsive to dark theme via transparency. */
}

.brand-name {
  font-size: 1.1rem; font-weight: 700; text-align: center;
  background: linear-gradient(135deg, var(--kc-cyan), var(--kc-orange));
  -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text;
}

.brand-version { font-size: 0.7rem; color: var(--kc-text-secondary); text-align: center; }
.sidebar-nav { flex: 1; padding: 0.5rem; overflow-y: auto; }

.nav-item {
  display: flex; align-items: center; gap: 0.75rem;
  padding: 0.75rem 1rem; border-radius: 8px;
  color: var(--kc-text-secondary); text-decoration: none;
  transition: all 0.15s ease; margin-bottom: 2px;
}

.nav-item:hover { background: rgba(0, 212, 255, 0.08); color: var(--kc-text-primary); }
.nav-item--active { background: rgba(0, 212, 255, 0.15); color: var(--kc-cyan); }
.nav-icon { font-size: 1.1rem; width: 20px; text-align: center; }

.nav-divider { padding: 0.5rem 1rem 0.25rem; }
.divider-label {
  font-size: 0.65rem; text-transform: uppercase; letter-spacing: 0.08em;
  color: var(--kc-text-secondary); font-weight: 600; opacity: 0.7;
}
.divider-line {
  border: none; border-top: 1px solid var(--kc-border); margin: 0.25rem 0;
}
.sidebar-footer { padding: 1rem; border-top: 1px solid var(--kc-border); }

.lang-switcher { margin-bottom: 0.5rem; }
.lang-select { width: 100%; font-size: 0.8rem; }
.lang-cycle-btn { margin-bottom: 0.5rem; }

.ws-status {
  display: flex; align-items: center; gap: 0.5rem;
  font-size: 0.75rem; color: var(--kc-text-secondary); margin-bottom: 0.5rem;
}

.ws-status i { font-size: 0.5rem; color: #ef4444; }
.ws-status i.connected { color: #22c55e; }

.user-info {
  display: flex; align-items: center; gap: 0.5rem;
  color: var(--kc-text-secondary); font-size: 0.85rem; margin-bottom: 0.5rem;
}

.role-badge {
  font-size: 0.6rem; text-transform: uppercase; letter-spacing: 0.05em;
  padding: 0.1rem 0.4rem; border-radius: 4px; font-weight: 600; margin-left: auto;
}

.role-badge--admin { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
.role-badge--moderator { background: rgba(245, 158, 11, 0.15); color: #f59e0b; }
.role-badge--viewer { background: rgba(0, 188, 212, 0.15); color: var(--kc-cyan); }

.logout-btn { width: 100%; }
.main-content { flex: 1; overflow-y: auto; padding: 1.5rem; position: relative; }
.collapsed .nav-item { justify-content: center; padding: 0.75rem; }
.collapsed .sidebar-footer { display: flex; flex-direction: column; align-items: center; }

/* Mobile / desktop visibility helpers */
.mobile-only { display: none; }
.desktop-only { display: inline-flex; }

/* Mobile backdrop */
.mobile-backdrop {
  display: none;
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  z-index: 99;
}

/* Hamburger button */
.mobile-hamburger {
  position: absolute;
  top: 0.75rem;
  left: 0.75rem;
  z-index: 10;
}

/* ── Tablet breakpoint ≤768px ── */
@media (max-width: 768px) {
  .mobile-only { display: inline-flex; }
  .desktop-only { display: none; }

  .mobile-backdrop { display: block; }

  .sidebar {
    position: fixed;
    left: 0;
    top: 0;
    height: 100vh;
    z-index: 100;
    width: 260px;
    transform: translateX(-100%);
    transition: transform 0.25s ease;
  }

  .sidebar.mobile-open {
    transform: translateX(0);
  }

  /* Never show collapsed state on mobile — always full sidebar */
  .sidebar.collapsed {
    width: 260px;
    transform: translateX(-100%);
  }

  .sidebar.collapsed.mobile-open {
    transform: translateX(0);
  }

  /* Force show labels even if collapsed on desktop */
  .sidebar.collapsed .nav-label { display: inline; }
  .sidebar.collapsed .nav-item { justify-content: flex-start; padding: 0.75rem 1rem; }
  .sidebar.collapsed .sidebar-footer { align-items: stretch; }
  .sidebar.collapsed .brand { display: block; }
  .sidebar.collapsed .ws-status,
  .sidebar.collapsed .user-info { display: flex; }
  .sidebar.collapsed .logout-btn .p-button-label { display: inline; }

  .main-content {
    padding: 1rem;
    padding-top: 3.5rem; /* room for hamburger */
  }
}

/* ── Phone breakpoint ≤640px ── */
@media (max-width: 640px) {
  .main-content {
    padding: 0.75rem;
    padding-top: 3.5rem;
  }
}
</style>
