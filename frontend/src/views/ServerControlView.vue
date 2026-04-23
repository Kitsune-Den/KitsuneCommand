<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { getServerInfo, getServerStats, saveWorld, shutdownServer, restartServer, type ServerInfo, type ServerStats } from '@/api/serverControl'
import Button from 'primevue/button'
import Card from 'primevue/card'
import InputNumber from 'primevue/inputnumber'
import Dialog from 'primevue/dialog'

const { t } = useI18n()
const toast = useToast()

const loading = ref(true)
const info = ref<ServerInfo | null>(null)
const stats = ref<ServerStats | null>(null)
const saving = ref(false)
const restarting = ref(false)
const shutdownDelay = ref(10)
const shutdownDialogVisible = ref(false)
const restartDialogVisible = ref(false)
let refreshInterval: ReturnType<typeof setInterval> | null = null

function formatUptime(seconds: number): string {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  if (h > 0) return `${h}h ${m}m`
  return `${m}m`
}

function formatMemory(mb: number): string {
  if (mb > 1024) return `${(mb / 1024).toFixed(1)} GB`
  return `${mb} MB`
}

async function loadData() {
  try {
    const [infoData, statsData] = await Promise.all([getServerInfo(), getServerStats()])
    info.value = infoData
    stats.value = statsData
  } catch {
    // Silently fail on refresh, show error on initial load
    if (loading.value) {
      toast.add({ severity: 'error', summary: t('common.error'), detail: t('serverControl.failedToLoad'), life: 4000 })
    }
  } finally {
    loading.value = false
  }
}

async function handleSaveWorld() {
  saving.value = true
  try {
    const msg = await saveWorld()
    toast.add({ severity: 'success', summary: t('serverControl.saveSuccess'), detail: msg, life: 4000 })
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('serverControl.saveFailed'), life: 4000 })
  } finally {
    saving.value = false
  }
}

function showShutdownDialog() {
  shutdownDialogVisible.value = true
}

async function handleShutdown() {
  shutdownDialogVisible.value = false
  try {
    await shutdownServer(shutdownDelay.value)
    toast.add({ severity: 'warn', summary: t('serverControl.shutdownInitiated'), detail: t('serverControl.shutdownWarning'), life: 10000 })
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('serverControl.shutdownFailed'), life: 4000 })
  }
}

function showRestartDialog() {
  restartDialogVisible.value = true
}

async function handleRestart() {
  restartDialogVisible.value = false
  restarting.value = true
  try {
    const msg = await restartServer()
    toast.add({ severity: 'warn', summary: t('serverControl.restartInitiated'), detail: msg, life: 10000 })
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('serverControl.restartFailed'), life: 4000 })
  } finally {
    restarting.value = false
  }
}

onMounted(() => {
  loadData()
  refreshInterval = setInterval(loadData, 10000) // Refresh every 10s
})

onUnmounted(() => {
  if (refreshInterval) clearInterval(refreshInterval)
})
</script>

<template>
  <div class="server-control-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('serverControl.title') }}</h1>
      <div class="status-indicator">
        <i class="pi pi-circle-fill status-dot" />
        <span>{{ t('serverControl.running') }}</span>
      </div>
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem" />
      <p>{{ t('common.loading') }}</p>
    </div>

    <template v-else>
      <!-- Quick Actions -->
      <div class="actions-row">
        <Button
          :label="t('serverControl.saveWorld')"
          icon="pi pi-save"
          severity="info"
          :loading="saving"
          @click="handleSaveWorld"
        />
        <Button
          :label="t('serverControl.restart')"
          icon="pi pi-refresh"
          severity="warn"
          :loading="restarting"
          @click="showRestartDialog"
        />
        <Button
          :label="t('serverControl.shutdown')"
          icon="pi pi-power-off"
          severity="danger"
          @click="showShutdownDialog"
        />
      </div>

      <!-- Stats Grid -->
      <div class="stats-grid">
        <Card class="stat-card">
          <template #content>
            <div class="stat-content">
              <i class="pi pi-users stat-icon" />
              <div>
                <div class="stat-value">{{ info?.onlinePlayers ?? 0 }} / {{ info?.maxPlayers ?? 0 }}</div>
                <div class="stat-label">{{ t('serverControl.players') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat-content">
              <i class="pi pi-gauge stat-icon" />
              <div>
                <div class="stat-value">{{ stats?.fps?.toFixed(1) ?? '—' }}</div>
                <div class="stat-label">{{ t('serverControl.fps') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat-content">
              <i class="pi pi-clock stat-icon" />
              <div>
                <div class="stat-value">{{ stats ? formatUptime(stats.uptime) : '—' }}</div>
                <div class="stat-label">{{ t('serverControl.uptime') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat-content">
              <i class="pi pi-microchip stat-icon" />
              <div>
                <div class="stat-value">{{ stats ? formatMemory(stats.workingSetMemory) : '—' }}</div>
                <div class="stat-label">{{ t('serverControl.memory') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat-content">
              <i class="pi pi-sun stat-icon" />
              <div>
                <div class="stat-value">{{ t('serverControl.dayTime', { day: info?.currentDay ?? 0, time: info?.currentTime ?? '—' }) }}</div>
                <div class="stat-label">{{ t('serverControl.gameDay') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat-content">
              <i class="pi pi-objects-column stat-icon" />
              <div>
                <div class="stat-value">{{ stats?.entityCount ?? 0 }}</div>
                <div class="stat-label">{{ t('serverControl.entities') }}</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Server Information -->
      <Card class="info-card">
        <template #title>{{ t('serverControl.serverInfo') }}</template>
        <template #content>
          <div class="info-grid">
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.serverName') }}</span>
              <span class="info-value">{{ info?.serverName }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.version') }}</span>
              <span class="info-value">{{ info?.version }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.world') }}</span>
              <span class="info-value">{{ info?.gameWorld }} ({{ info?.gameName }})</span>
            </div>
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.difficulty') }}</span>
              <span class="info-value">{{ info?.difficulty }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.gameMode') }}</span>
              <span class="info-value">{{ info?.gameMode }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.port') }}</span>
              <span class="info-value">{{ info?.serverPort }}</span>
            </div>
          </div>
        </template>
      </Card>

      <!-- System Information -->
      <Card class="info-card" v-if="stats?.system">
        <template #title>{{ t('serverControl.system') }}</template>
        <template #content>
          <div class="info-grid">
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.os') }}</span>
              <span class="info-value">{{ stats.system.os }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.cpu') }}</span>
              <span class="info-value">{{ stats.system.processorCount }} {{ t('serverControl.cores') }} ({{ stats.system.is64Bit ? '64-bit' : '32-bit' }})</span>
            </div>
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.gcMemory') }}</span>
              <span class="info-value">{{ formatMemory(stats.gcMemory) }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.peakMemory') }}</span>
              <span class="info-value">{{ formatMemory(stats.peakWorkingSetMemory) }}</span>
            </div>
            <div class="info-item">
              <span class="info-label">{{ t('serverControl.threads') }}</span>
              <span class="info-value">{{ stats.threadCount }}</span>
            </div>
          </div>
        </template>
      </Card>
    </template>

    <!-- Restart confirmation dialog -->
    <Dialog v-model:visible="restartDialogVisible" :header="t('serverControl.restartConfirm')" modal :style="{ width: '460px' }">
      <div class="shutdown-dialog">
        <i class="pi pi-refresh" style="font-size: 2.5rem; color: #f59e0b" />
        <p class="shutdown-warning" style="color: #f59e0b">{{ t('serverControl.restartWarning') }}</p>
        <p style="font-size: 0.85rem; color: var(--kc-text-secondary); text-align: center;">{{ t('serverControl.restartHint') }}</p>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" severity="secondary" text @click="restartDialogVisible = false" />
        <Button :label="t('serverControl.restart')" severity="warn" icon="pi pi-refresh" @click="handleRestart" />
      </template>
    </Dialog>

    <!-- Shutdown confirmation dialog -->
    <Dialog v-model:visible="shutdownDialogVisible" :header="t('serverControl.shutdownConfirm')" modal :style="{ width: '460px' }">
      <div class="shutdown-dialog">
        <i class="pi pi-exclamation-triangle" style="font-size: 2.5rem; color: #ef4444" />
        <p class="shutdown-warning">{{ t('serverControl.shutdownWarning') }}</p>
        <div class="delay-input">
          <label>{{ t('serverControl.delay') }}</label>
          <InputNumber v-model="shutdownDelay" :min="0" :max="300" suffix=" sec" />
        </div>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" severity="secondary" text @click="shutdownDialogVisible = false" />
        <Button :label="t('serverControl.shutdown')" severity="danger" icon="pi pi-power-off" @click="handleShutdown" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.server-control-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 600;
}

.status-indicator {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.9rem;
  font-weight: 600;
  color: #22c55e;
}

.status-dot {
  font-size: 0.6rem;
  color: #22c55e;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  padding: 3rem;
  color: var(--kc-text-secondary);
}

.actions-row {
  display: flex;
  gap: 0.75rem;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 0.75rem;
}

.stat-card {
  background: var(--kc-bg-secondary);
  border: 1px solid var(--kc-border);
}

.stat-content {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.stat-icon {
  font-size: 1.5rem;
  color: var(--kc-cyan);
  opacity: 0.8;
}

.stat-value {
  font-size: 1.25rem;
  font-weight: 700;
}

.stat-label {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.info-card {
  background: var(--kc-bg-secondary);
  border: 1px solid var(--kc-border);
}

.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 0.75rem;
}

.info-item {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.info-label {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.info-value {
  font-size: 0.9rem;
  word-break: break-word;
}

.shutdown-dialog {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  text-align: center;
}

.shutdown-warning {
  color: #ef4444;
  font-weight: 500;
}

.delay-input {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  width: 100%;
}

.delay-input label {
  font-size: 0.85rem;
  color: var(--kc-text-secondary);
}

@media (max-width: 768px) {
  .stats-grid { grid-template-columns: repeat(2, 1fr); }
  .info-grid { grid-template-columns: 1fr; }
  .actions-row { flex-direction: column; }
}
</style>
