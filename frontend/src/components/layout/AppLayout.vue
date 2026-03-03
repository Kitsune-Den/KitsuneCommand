<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { useAppWebSocket } from '@/composables/useAppWebSocket'
import Button from 'primevue/button'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const appWs = useAppWebSocket()
const sidebarCollapsed = ref(false)
const mobileMenuOpen = ref(false)

const navItems = [
  { label: 'Dashboard', icon: 'pi pi-home', route: '/' },
  { label: 'Players', icon: 'pi pi-users', route: '/players' },
  { label: 'Console', icon: 'pi pi-code', route: '/console' },
  { label: 'Map', icon: 'pi pi-map', route: '/map' },
  { label: 'Chat', icon: 'pi pi-comments', route: '/chat' },
  { label: 'Teleport', icon: 'pi pi-compass', route: '/teleport/cities' },
  { label: 'CD Keys', icon: 'pi pi-key', route: '/cdkeys' },
  { label: 'Economy', icon: 'pi pi-wallet', route: '/economy/points' },
  { label: 'Settings', icon: 'pi pi-cog', route: '/settings' },
]

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
          <h2 class="brand-name">KitsuneCommand</h2>
          <span class="brand-version">v2.0.0</span>
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
        <router-link
          v-for="item in navItems"
          :key="item.route"
          :to="item.route"
          class="nav-item"
          active-class="nav-item--active"
          :title="sidebarCollapsed ? item.label : undefined"
        >
          <i :class="item.icon" class="nav-icon"></i>
          <span v-if="!sidebarCollapsed" class="nav-label">{{ item.label }}</span>
        </router-link>
      </nav>

      <div class="sidebar-footer">
        <!-- Connection indicator -->
        <div class="ws-status" v-if="!sidebarCollapsed">
          <i class="pi pi-circle-fill" :class="{ connected: appWs.isConnected.value }" />
          <span>{{ appWs.isConnected.value ? 'Live' : 'Offline' }}</span>
        </div>
        <div class="user-info" v-if="!sidebarCollapsed">
          <i class="pi pi-user"></i>
          <span>{{ auth.displayName || auth.username }}</span>
          <span class="role-badge" :class="`role-badge--${auth.role}`">{{ auth.role }}</span>
        </div>
        <Button
          :icon="'pi pi-sign-out'"
          :label="sidebarCollapsed ? undefined : 'Logout'"
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

.brand-name {
  font-size: 1.1rem; font-weight: 700;
  background: linear-gradient(135deg, var(--kc-cyan), var(--kc-orange));
  -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text;
}

.brand-version { font-size: 0.7rem; color: var(--kc-text-secondary); }
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
.sidebar-footer { padding: 1rem; border-top: 1px solid var(--kc-border); }

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
