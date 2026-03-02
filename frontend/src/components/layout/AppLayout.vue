<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import Button from 'primevue/button'

const router = useRouter()
const auth = useAuthStore()
const sidebarCollapsed = ref(false)

const navItems = [
  { label: 'Dashboard', icon: 'pi pi-home', route: '/' },
  { label: 'Players', icon: 'pi pi-users', route: '/players' },
  { label: 'Console', icon: 'pi pi-code', route: '/console' },
  { label: 'Map', icon: 'pi pi-map', route: '/map' },
  { label: 'Chat', icon: 'pi pi-comments', route: '/chat' },
  { label: 'Settings', icon: 'pi pi-cog', route: '/settings' },
]

function toggleSidebar() {
  sidebarCollapsed.value = !sidebarCollapsed.value
}

async function handleLogout() {
  auth.logout()
  await router.push({ name: 'Login' })
}
</script>

<template>
  <div class="app-layout">
    <!-- Sidebar -->
    <aside class="sidebar" :class="{ collapsed: sidebarCollapsed }">
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
          class="collapse-btn"
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
        <div class="user-info" v-if="!sidebarCollapsed">
          <i class="pi pi-user"></i>
          <span>{{ auth.displayName || auth.username }}</span>
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
      <router-view />
    </main>
  </div>
</template>

<style scoped>
.app-layout {
  display: flex;
  height: 100vh;
  overflow: hidden;
}

.sidebar {
  width: 240px;
  background: var(--kc-bg-secondary);
  border-right: 1px solid var(--kc-border);
  display: flex;
  flex-direction: column;
  transition: width 0.2s ease;
}

.sidebar.collapsed {
  width: 64px;
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1rem;
  border-bottom: 1px solid var(--kc-border);
  min-height: 64px;
}

.brand-name {
  font-size: 1.1rem;
  font-weight: 700;
  background: linear-gradient(135deg, var(--kc-cyan), var(--kc-orange));
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

.brand-version {
  font-size: 0.7rem;
  color: var(--kc-text-secondary);
}

.sidebar-nav {
  flex: 1;
  padding: 0.5rem;
  overflow-y: auto;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  border-radius: 8px;
  color: var(--kc-text-secondary);
  text-decoration: none;
  transition: all 0.15s ease;
  margin-bottom: 2px;
}

.nav-item:hover {
  background: rgba(0, 212, 255, 0.08);
  color: var(--kc-text-primary);
}

.nav-item--active {
  background: rgba(0, 212, 255, 0.15);
  color: var(--kc-cyan);
}

.nav-icon {
  font-size: 1.1rem;
  width: 20px;
  text-align: center;
}

.sidebar-footer {
  padding: 1rem;
  border-top: 1px solid var(--kc-border);
}

.user-info {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: var(--kc-text-secondary);
  font-size: 0.85rem;
  margin-bottom: 0.5rem;
}

.logout-btn {
  width: 100%;
}

.main-content {
  flex: 1;
  overflow-y: auto;
  padding: 1.5rem;
}

.collapsed .nav-item {
  justify-content: center;
  padding: 0.75rem;
}

.collapsed .sidebar-footer {
  display: flex;
  flex-direction: column;
  align-items: center;
}
</style>
