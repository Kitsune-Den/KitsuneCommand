<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { getServerInfo, getServerStats, type ServerInfo, type ServerStats } from '@/api/server'
import { usePlayersStore } from '@/stores/players'
import { useServerStore } from '@/stores/server'
import Card from 'primevue/card'

const serverInfo = ref<ServerInfo | null>(null)
const serverStats = ref<ServerStats | null>(null)
const loading = ref(true)
const error = ref('')
const playersStore = usePlayersStore()
const serverStore = useServerStore()

const recentActivity = computed(() => serverStore.activity.slice(0, 15))

const gameTimeDisplay = computed(() => {
  if (serverStore.gameDay === 0 && serverStore.gameHour === 0) {
    return serverInfo.value ? `Day ${serverInfo.value.currentDay} - ${serverInfo.value.currentTime}` : 'N/A'
  }
  const h = String(serverStore.gameHour).padStart(2, '0')
  const m = String(serverStore.gameMinute).padStart(2, '0')
  return `Day ${serverStore.gameDay} - ${h}:${m}`
})

function activityIcon(type: string) {
  switch (type) {
    case 'login': return 'pi pi-sign-in'
    case 'logout': return 'pi pi-sign-out'
    case 'chat': return 'pi pi-comment'
    case 'kill': return 'pi pi-bolt'
    default: return 'pi pi-info-circle'
  }
}

function activityColor(type: string) {
  switch (type) {
    case 'login': return 'color: #22c55e'
    case 'logout': return 'color: #ef4444'
    case 'chat': return 'color: var(--kc-cyan)'
    case 'kill': return 'color: var(--kc-orange)'
    default: return 'color: var(--kc-text-secondary)'
  }
}

function timeAgo(date: Date): string {
  const seconds = Math.floor((Date.now() - date.getTime()) / 1000)
  if (seconds < 60) return 'just now'
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`
  return `${Math.floor(seconds / 3600)}h ago`
}

async function fetchData() {
  try {
    const [info, stats] = await Promise.all([getServerInfo(), getServerStats()])
    serverInfo.value = info
    serverStats.value = stats
  } catch {
    error.value = 'Failed to fetch server data. Is the game server running?'
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  fetchData()
  const interval = setInterval(async () => {
    try {
      serverStats.value = await getServerStats()
    } catch { /* silently fail */ }
  }, 5000)
  return () => clearInterval(interval)
})
</script>

<template>
  <div class="dashboard">
    <h1 class="page-title">Dashboard</h1>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Connecting to server...</p>
    </div>

    <div v-else-if="error" class="error-state">
      <i class="pi pi-exclamation-triangle" style="font-size: 2rem; color: var(--kc-orange)"></i>
      <p>{{ error }}</p>
    </div>

    <template v-else>
      <!-- Stats Cards -->
      <div class="stats-grid">
        <Card class="stat-card">
          <template #content>
            <div class="stat">
              <i class="pi pi-users stat-icon cyan"></i>
              <div>
                <div class="stat-value">{{ playersStore.onlineCount || serverStats?.playerCount || 0 }}</div>
                <div class="stat-label">Players Online</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat">
              <i class="pi pi-gauge stat-icon orange"></i>
              <div>
                <div class="stat-value">{{ serverStats?.fps?.toFixed(1) ?? '0' }}</div>
                <div class="stat-label">Server FPS</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat">
              <i class="pi pi-box stat-icon cyan"></i>
              <div>
                <div class="stat-value">{{ serverStats?.entityCount ?? 0 }}</div>
                <div class="stat-label">Entities</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat">
              <i class="pi pi-server stat-icon orange"></i>
              <div>
                <div class="stat-value">{{ serverStats?.gcMemory ?? 0 }} MB</div>
                <div class="stat-label">Memory</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <div class="dashboard-panels">
        <!-- Server Info -->
        <Card class="info-card" v-if="serverInfo">
          <template #title>Server Information</template>
          <template #content>
            <div class="info-grid">
              <div class="info-item">
                <span class="info-label">Server Name</span>
                <span class="info-value">{{ serverInfo.serverName }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">World</span>
                <span class="info-value">{{ serverInfo.gameWorld }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">Game Time</span>
                <span class="info-value">
                  {{ gameTimeDisplay }}
                  <span v-if="serverStore.isBloodMoon" style="color: #ef4444; margin-left: 0.5rem">BLOOD MOON</span>
                </span>
              </div>
              <div class="info-item">
                <span class="info-label">Max Players</span>
                <span class="info-value">{{ serverInfo.maxPlayers }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">Difficulty</span>
                <span class="info-value">{{ serverInfo.difficulty }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">Game Version</span>
                <span class="info-value">{{ serverInfo.version }}</span>
              </div>
            </div>
          </template>
        </Card>

        <!-- Activity Feed -->
        <Card class="activity-card">
          <template #title>Recent Activity</template>
          <template #content>
            <div v-if="recentActivity.length === 0" class="empty-activity">
              <p>No activity yet. Events will appear here in real-time.</p>
            </div>
            <div v-else class="activity-list">
              <div v-for="item in recentActivity" :key="item.id" class="activity-item">
                <i :class="activityIcon(item.type)" :style="activityColor(item.type)" />
                <span class="activity-text">{{ item.message }}</span>
                <span class="activity-time">{{ timeAgo(item.timestamp) }}</span>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </template>
  </div>
</template>

<style scoped>
.page-title { font-size: 1.5rem; font-weight: 600; margin-bottom: 1.5rem; }

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.stat-card { background: var(--kc-bg-card); border: 1px solid var(--kc-border); }
.stat { display: flex; align-items: center; gap: 1rem; }
.stat-icon { font-size: 1.75rem; }
.stat-icon.cyan { color: var(--kc-cyan); }
.stat-icon.orange { color: var(--kc-orange); }
.stat-value { font-size: 1.5rem; font-weight: 700; color: var(--kc-text-primary); }
.stat-label { font-size: 0.8rem; color: var(--kc-text-secondary); text-transform: uppercase; letter-spacing: 0.05em; }

.dashboard-panels {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

@media (max-width: 900px) {
  .dashboard-panels { grid-template-columns: 1fr; }
}

@media (max-width: 640px) {
  .stats-grid { grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); }
}

.info-card, .activity-card { background: var(--kc-bg-card); border: 1px solid var(--kc-border); }
.info-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1rem; }
.info-item { display: flex; flex-direction: column; gap: 0.25rem; }
.info-label { font-size: 0.8rem; color: var(--kc-text-secondary); text-transform: uppercase; letter-spacing: 0.05em; }
.info-value { font-size: 1rem; color: var(--kc-text-primary); }

.activity-list { display: flex; flex-direction: column; gap: 0.5rem; max-height: 300px; overflow-y: auto; }
.activity-item { display: flex; align-items: center; gap: 0.75rem; padding: 0.4rem 0; font-size: 0.85rem; }
.activity-text { flex: 1; color: var(--kc-text-primary); }
.activity-time { color: var(--kc-text-secondary); font-size: 0.75rem; white-space: nowrap; }
.empty-activity { color: var(--kc-text-secondary); font-size: 0.9rem; padding: 1rem 0; }

.loading-state, .error-state {
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: 1rem; padding: 4rem 0; color: var(--kc-text-secondary);
}
</style>
