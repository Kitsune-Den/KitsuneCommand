<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { getServerInfo, getServerStats, getDashboardStats, type ServerInfo, type ServerStats } from '@/api/server'
import { forceSkipBloodMoon } from '@/api/bloodmoonvote'
import type { DashboardStats } from '@/types'
import { usePlayersStore } from '@/stores/players'
import { useServerStore } from '@/stores/server'
import { usePermissions } from '@/composables/usePermissions'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import ProgressBar from 'primevue/progressbar'
import Button from 'primevue/button'

const { t } = useI18n()
const toast = useToast()
const confirmDialog = useConfirm()
const { isAdmin } = usePermissions()

const serverInfo = ref<ServerInfo | null>(null)
const serverStats = ref<ServerStats | null>(null)
const dashboardStats = ref<DashboardStats | null>(null)
const loading = ref(true)
const error = ref('')
const playersStore = usePlayersStore()
const serverStore = useServerStore()

const recentActivity = computed(() => serverStore.activity.slice(0, 15))

const gameTimeDisplay = computed(() => {
  if (serverStore.gameDay === 0 && serverStore.gameHour === 0) {
    return serverInfo.value ? `Day ${serverInfo.value.currentDay} - ${serverInfo.value.currentTime}` : t('dashboard.na')
  }
  const h = String(serverStore.gameHour).padStart(2, '0')
  const m = String(serverStore.gameMinute).padStart(2, '0')
  return `Day ${serverStore.gameDay} - ${h}:${m}`
})

const uptimeDisplay = computed(() => {
  if (!serverStats.value) return t('dashboard.na')
  const totalSeconds = Math.floor(serverStats.value.uptime)
  const days = Math.floor(totalSeconds / 86400)
  const hours = Math.floor((totalSeconds % 86400) / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  if (days > 0) return `${days}d ${hours}h ${minutes}m`
  if (hours > 0) return `${hours}h ${minutes}m`
  return `${minutes}m`
})

const bloodMoonDisplay = computed(() => {
  if (!serverInfo.value) return null
  const info = serverInfo.value as any
  const freq = info.bloodMoonFrequency
  const currentDay = serverStore.gameDay || info.currentDay || 0
  if (!freq || freq <= 0 || currentDay <= 0) return null
  const nextBM = Math.ceil(currentDay / freq) * freq
  const daysUntil = nextBM - currentDay
  return {
    frequency: freq,
    nextDay: nextBM,
    daysUntil,
    isTonight: daysUntil === 0,
  }
})

const votePercent = computed(() => {
  const vote = serverStore.bloodMoonVote
  if (vote.requiredVotes <= 0) return 0
  return Math.min(100, Math.round((vote.currentVotes / vote.requiredVotes) * 100))
})

// Reachability derived from Steam/EOS master-server registration state.
// If ServerVisibility=0 (hidden), we don't expect Steam registration and just say 'Hidden'.
// If ServerVisibility!=0 but Steam didn't register, we're not reachable via browse list.
const reachabilityClass = computed(() => {
  const info = serverInfo.value
  if (!info) return 'reachability--unknown'
  if (info.serverVisibility === 0) return 'reachability--hidden'
  if (info.steamRegistered) return 'reachability--ok'
  return 'reachability--bad'
})
const reachabilityIcon = computed(() => {
  const c = reachabilityClass.value
  if (c === 'reachability--ok') return 'pi pi-check-circle'
  if (c === 'reachability--hidden') return 'pi pi-eye-slash'
  if (c === 'reachability--bad') return 'pi pi-exclamation-triangle'
  return 'pi pi-question-circle'
})
const reachabilityLabel = computed(() => {
  const c = reachabilityClass.value
  if (c === 'reachability--ok') return t('dashboard.reachableOk')
  if (c === 'reachability--hidden') return t('dashboard.reachableHidden')
  if (c === 'reachability--bad') return t('dashboard.reachableBad')
  return t('dashboard.reachableUnknown')
})
const reachabilityTooltip = computed(() => {
  const info = serverInfo.value
  if (!info) return ''
  const parts: string[] = []
  parts.push(`ServerVisibility: ${info.serverVisibility}`)
  parts.push(`Steam registered: ${info.steamRegistered ? 'yes' : 'no'}`)
  parts.push(`EOS registered: ${info.eosRegistered ? 'yes' : 'no'}`)
  return parts.join(' · ')
})

function confirmForceSkip() {
  confirmDialog.require({
    message: t('dashboard.forceSkipConfirm'),
    header: t('dashboard.forceSkipHeader'),
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await forceSkipBloodMoon()
        toast.add({ severity: 'success', summary: t('common.success'), detail: t('dashboard.bloodMoonSkipped'), life: 3000 })
      } catch {
        toast.add({ severity: 'error', summary: t('common.error'), detail: t('dashboard.forceSkipFailed'), life: 3000 })
      }
    },
  })
}

function copyToClipboard(text: string) {
  navigator.clipboard.writeText(text)
  toast.add({ severity: 'info', summary: t('dashboard.copied'), life: 2000 })
}

function formatNumber(num: number): string {
  if (num >= 1000000) return `${(num / 1000000).toFixed(1)}M`
  if (num >= 1000) return `${(num / 1000).toFixed(1)}K`
  return num.toString()
}

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
  if (seconds < 60) return t('dashboard.justNow')
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`
  return `${Math.floor(seconds / 3600)}h ago`
}

async function fetchData() {
  try {
    const [info, stats] = await Promise.all([getServerInfo(), getServerStats()])
    serverInfo.value = info
    serverStats.value = stats
    // Load dashboard stats separately (non-blocking)
    getDashboardStats().then(s => { dashboardStats.value = s }).catch(() => {})
  } catch {
    error.value = t('dashboard.failedToFetch')
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
    <h1 class="page-title">{{ t('dashboard.title') }}</h1>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>{{ t('dashboard.connectingToServer') }}</p>
    </div>

    <div v-else-if="error" class="error-state">
      <i class="pi pi-exclamation-triangle" style="font-size: 2rem; color: var(--kc-orange)"></i>
      <p>{{ error }}</p>
    </div>

    <template v-else>
      <!-- Primary Stats Cards -->
      <div class="stats-grid">
        <Card class="stat-card">
          <template #content>
            <div class="stat">
              <i class="pi pi-users stat-icon cyan"></i>
              <div>
                <div class="stat-value">{{ playersStore.onlineCount || serverStats?.playerCount || 0 }}</div>
                <div class="stat-label">{{ t('dashboard.playersOnline') }}</div>
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
                <div class="stat-label">{{ t('dashboard.serverFps') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat">
              <i class="pi pi-clock stat-icon cyan"></i>
              <div>
                <div class="stat-value">{{ uptimeDisplay }}</div>
                <div class="stat-label">{{ t('dashboard.uptime') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat">
              <i class="pi pi-box stat-icon orange"></i>
              <div>
                <div class="stat-value">{{ serverStats?.entityCount ?? 0 }}</div>
                <div class="stat-label">{{ t('dashboard.entities') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card">
          <template #content>
            <div class="stat">
              <i class="pi pi-server stat-icon cyan"></i>
              <div>
                <div class="stat-value">{{ serverStats?.gcMemory ?? 0 }} MB</div>
                <div class="stat-label">{{ t('dashboard.memory') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="stat-card" :class="{ 'blood-moon-card': serverStore.isBloodMoon || bloodMoonDisplay?.isTonight }">
          <template #content>
            <div class="stat">
              <i class="pi pi-moon stat-icon" :class="serverStore.isBloodMoon || bloodMoonDisplay?.isTonight ? 'red' : 'orange'"></i>
              <div style="flex: 1">
                <div class="stat-value" v-if="serverStore.isBloodMoon">{{ t('dashboard.bloodMoonActive') }}</div>
                <div class="stat-value" v-else-if="bloodMoonDisplay?.isTonight">{{ t('dashboard.tonight') }}</div>
                <div class="stat-value" v-else-if="bloodMoonDisplay">{{ bloodMoonDisplay.daysUntil }}d</div>
                <div class="stat-value" v-else>{{ t('dashboard.na') }}</div>
                <div class="stat-label">{{ t('dashboard.bloodMoon') }}</div>
              </div>
            </div>
            <!-- Blood Moon Vote Status -->
            <div v-if="serverStore.bloodMoonVote.isActive" class="bm-vote-status">
              <div class="bm-vote-info">
                <span class="bm-vote-text">{{ t('dashboard.voteProgress', { current: serverStore.bloodMoonVote.currentVotes, required: serverStore.bloodMoonVote.requiredVotes }) }}</span>
              </div>
              <ProgressBar :value="votePercent" :showValue="false" style="height: 6px; margin-top: 0.35rem" />
              <Button
                v-if="isAdmin"
                :label="t('dashboard.forceSkip')"
                icon="pi pi-forward"
                size="small"
                severity="danger"
                text
                @click="confirmForceSkip"
                style="margin-top: 0.5rem; padding: 0.25rem 0.5rem"
              />
            </div>
          </template>
        </Card>
      </div>

      <div class="dashboard-panels">
        <!-- Server Info -->
        <Card class="info-card" v-if="serverInfo">
          <template #title>{{ t('dashboard.serverInformation') }}</template>
          <template #content>
            <div class="info-grid">
              <div class="info-item">
                <span class="info-label">{{ t('dashboard.serverName') }}</span>
                <span class="info-value">{{ serverInfo.serverName }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('dashboard.world') }}</span>
                <span class="info-value">{{ serverInfo.gameWorld }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('dashboard.gameTime') }}</span>
                <span class="info-value">
                  {{ gameTimeDisplay }}
                  <span v-if="serverStore.isBloodMoon" style="color: #ef4444; margin-left: 0.5rem">{{ t('dashboard.bloodMoonLabel') }}</span>
                </span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('dashboard.maxPlayers') }}</span>
                <span class="info-value">{{ serverInfo.maxPlayers }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('dashboard.difficulty') }}</span>
                <span class="info-value">{{ serverInfo.difficulty }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('dashboard.gameVersion') }}</span>
                <span class="info-value">{{ serverInfo.version }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('dashboard.kitsuneCommand') }}</span>
                <span class="info-value">v{{ serverInfo.kitsuneCommandVersion }}</span>
              </div>
              <div class="info-item" v-if="bloodMoonDisplay">
                <span class="info-label">{{ t('dashboard.bloodMoonFrequency') }}</span>
                <span class="info-value">{{ t('dashboard.everyDays', { n: bloodMoonDisplay.frequency, day: bloodMoonDisplay.nextDay }) }}</span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('dashboard.localIp') }}</span>
                <span class="info-value monospace copyable" @click="copyToClipboard(`${serverInfo.localIp}:${serverInfo.serverPort}`)">
                  {{ serverInfo.localIp }}<span class="port-suffix">:{{ serverInfo.serverPort }}</span>
                  <i class="pi pi-copy copy-icon" />
                </span>
              </div>
              <div class="info-item">
                <span class="info-label">{{ t('dashboard.publicIp') }}</span>
                <span class="info-value monospace copyable" @click="copyToClipboard(`${serverInfo.publicIp}:${serverInfo.serverPort}`)">
                  {{ serverInfo.publicIp }}<span class="port-suffix">:{{ serverInfo.serverPort }}</span>
                  <i class="pi pi-copy copy-icon" />
                </span>
                <span class="reachability" :class="reachabilityClass" :title="reachabilityTooltip">
                  <i :class="reachabilityIcon" />
                  {{ reachabilityLabel }}
                </span>
              </div>
            </div>
          </template>
        </Card>

        <!-- Activity Feed -->
        <Card class="activity-card">
          <template #title>{{ t('dashboard.recentActivity') }}</template>
          <template #content>
            <div v-if="recentActivity.length === 0" class="empty-activity">
              <p>{{ t('dashboard.noActivityYet') }}</p>
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

      <!-- Feature Overview -->
      <div class="feature-overview" v-if="dashboardStats">
        <Card class="overview-card">
          <template #title>{{ t('dashboard.economyOverview') }}</template>
          <template #content>
            <div class="overview-grid">
              <div class="overview-item">
                <i class="pi pi-wallet overview-icon cyan"></i>
                <div class="overview-value">{{ formatNumber(dashboardStats.totalPointsInCirculation) }}</div>
                <div class="overview-label">{{ t('dashboard.pointsInCirculation') }}</div>
              </div>
              <div class="overview-item">
                <i class="pi pi-users overview-icon orange"></i>
                <div class="overview-value">{{ dashboardStats.totalPlayers }}</div>
                <div class="overview-label">{{ t('dashboard.registeredPlayers') }}</div>
              </div>
              <div class="overview-item">
                <i class="pi pi-shopping-cart overview-icon cyan"></i>
                <div class="overview-value">{{ dashboardStats.totalPurchases }}</div>
                <div class="overview-label">{{ t('dashboard.storePurchases') }}</div>
              </div>
              <div class="overview-item">
                <i class="pi pi-money-bill overview-icon orange"></i>
                <div class="overview-value">{{ formatNumber(dashboardStats.totalPointsSpent) }}</div>
                <div class="overview-label">{{ t('dashboard.pointsSpent') }}</div>
              </div>
            </div>
          </template>
        </Card>

        <Card class="overview-card">
          <template #title>{{ t('dashboard.featureSummary') }}</template>
          <template #content>
            <div class="overview-grid">
              <div class="overview-item">
                <i class="pi pi-map-marker overview-icon cyan"></i>
                <div class="overview-value">{{ dashboardStats.totalTeleports }}</div>
                <div class="overview-label">{{ t('dashboard.teleportsUsed') }}</div>
              </div>
              <div class="overview-item">
                <i class="pi pi-map overview-icon orange"></i>
                <div class="overview-value">{{ dashboardStats.totalCities }}</div>
                <div class="overview-label">{{ t('dashboard.cityWaypoints') }}</div>
              </div>
              <div class="overview-item">
                <i class="pi pi-key overview-icon cyan"></i>
                <div class="overview-value">{{ dashboardStats.totalRedemptions }}</div>
                <div class="overview-label">{{ t('dashboard.keyRedemptions') }}</div>
              </div>
              <div class="overview-item">
                <i class="pi pi-gift overview-icon orange"></i>
                <div class="overview-value">{{ dashboardStats.totalVipGifts }}</div>
                <div class="overview-label">{{ t('dashboard.vipGiftsLabel') }}</div>
              </div>
              <div class="overview-item">
                <i class="pi pi-clock overview-icon cyan"></i>
                <div class="overview-value">{{ dashboardStats.activeSchedules }}/{{ dashboardStats.totalSchedules }}</div>
                <div class="overview-label">{{ t('dashboard.activeSchedules') }}</div>
              </div>
              <div class="overview-item">
                <i class="pi pi-comments overview-icon orange"></i>
                <div class="overview-value">{{ formatNumber(dashboardStats.totalChatMessages) }}</div>
                <div class="overview-label">{{ t('dashboard.chatMessages') }}</div>
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
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 1rem;
  margin-bottom: 1.5rem;
}

.stat-card { background: var(--kc-bg-card); border: 1px solid var(--kc-border); }
.stat-card.blood-moon-card { border-color: #ef4444; background: rgba(239, 68, 68, 0.05); }
.stat { display: flex; align-items: center; gap: 1rem; }
.stat-icon { font-size: 1.75rem; }
.stat-icon.cyan { color: var(--kc-cyan); }
.stat-icon.orange { color: var(--kc-orange); }
.stat-icon.red { color: #ef4444; }
.stat-value { font-size: 1.5rem; font-weight: 700; color: var(--kc-text-primary); }
.stat-label { font-size: 0.8rem; color: var(--kc-text-secondary); text-transform: uppercase; letter-spacing: 0.05em; }

.dashboard-panels {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
  margin-bottom: 1.5rem;
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

.port-suffix { color: var(--kc-text-secondary); font-weight: normal; }

.reachability {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.72rem;
  margin-top: 0.2rem;
  padding: 0.1rem 0.5rem;
  border-radius: 0.35rem;
  width: fit-content;
  letter-spacing: 0.02em;
  cursor: help;
}
.reachability--ok      { color: #22c55e; background: rgba(34, 197, 94, 0.1); }
.reachability--bad     { color: #ef4444; background: rgba(239, 68, 68, 0.1); }
.reachability--hidden  { color: var(--kc-text-secondary); background: rgba(148, 163, 184, 0.1); }
.reachability--unknown { color: var(--kc-text-secondary); background: rgba(148, 163, 184, 0.1); }
.info-label { font-size: 0.8rem; color: var(--kc-text-secondary); text-transform: uppercase; letter-spacing: 0.05em; }
.info-value { font-size: 1rem; }
.info-value.monospace { font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace; }
.info-value.copyable { cursor: pointer; display: inline-flex; align-items: center; gap: 0.4rem; transition: color 0.15s; }
.info-value.copyable:hover { color: var(--kc-cyan); }
.copy-icon { font-size: 0.8rem; opacity: 0.4; transition: opacity 0.15s; }
.info-value.copyable:hover .copy-icon { opacity: 1; }

.activity-list { display: flex; flex-direction: column; gap: 0.5rem; max-height: 300px; overflow-y: auto; }
.activity-item { display: flex; align-items: center; gap: 0.75rem; padding: 0.4rem 0; font-size: 0.85rem; }
.activity-text { flex: 1; color: var(--kc-text-primary); }
.activity-time { color: var(--kc-text-secondary); font-size: 0.75rem; white-space: nowrap; }
.empty-activity { color: var(--kc-text-secondary); font-size: 0.9rem; padding: 1rem 0; }

/* Feature Overview */
.feature-overview {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}

@media (max-width: 900px) {
  .feature-overview { grid-template-columns: 1fr; }
}

.overview-card { background: var(--kc-bg-card); border: 1px solid var(--kc-border); }

.overview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
  gap: 1.25rem;
}

.overview-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 0.35rem;
}

.overview-icon {
  font-size: 1.35rem;
  margin-bottom: 0.15rem;
}

.overview-icon.cyan { color: var(--kc-cyan); }
.overview-icon.orange { color: var(--kc-orange); }

.overview-value {
  font-size: 1.3rem;
  font-weight: 700;
  color: var(--kc-text-primary);
}

.overview-label {
  font-size: 0.72rem;
  color: var(--kc-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

/* Blood Moon Vote Status */
.bm-vote-status {
  margin-top: 0.75rem;
  padding-top: 0.65rem;
  border-top: 1px solid var(--kc-border);
}

.bm-vote-info {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.bm-vote-text {
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
  font-weight: 500;
}

.loading-state, .error-state {
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: 1rem; padding: 4rem 0; color: var(--kc-text-secondary);
}
</style>
