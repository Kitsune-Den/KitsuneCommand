<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getServerInfo, getServerStats, type ServerInfo, type ServerStats } from '@/api/server'
import Card from 'primevue/card'

const serverInfo = ref<ServerInfo | null>(null)
const serverStats = ref<ServerStats | null>(null)
const loading = ref(true)
const error = ref('')

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
  // Refresh stats every 5 seconds
  const interval = setInterval(async () => {
    try {
      serverStats.value = await getServerStats()
    } catch {
      // Silently fail for periodic updates
    }
  }, 5000)

  // Cleanup on unmount
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
                <div class="stat-value">{{ serverStats?.playerCount ?? 0 }}</div>
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
              <span class="info-label">Game Day</span>
              <span class="info-value">Day {{ serverInfo.currentDay }} - {{ serverInfo.currentTime }}</span>
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
    </template>
  </div>
</template>

<style scoped>
.page-title {
  font-size: 1.5rem;
  font-weight: 600;
  margin-bottom: 1.5rem;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.stat-card {
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
}

.stat {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.stat-icon {
  font-size: 1.75rem;
}

.stat-icon.cyan {
  color: var(--kc-cyan);
}

.stat-icon.orange {
  color: var(--kc-orange);
}

.stat-value {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--kc-text-primary);
}

.stat-label {
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.info-card {
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.info-label {
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.info-value {
  font-size: 1rem;
  color: var(--kc-text-primary);
}

.loading-state,
.error-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  padding: 4rem 0;
  color: var(--kc-text-secondary);
}
</style>
