<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import {
  getJoinAttempts, clearJoinAttempts, setVerboseLogging,
  type JoinAttemptEvent,
} from '@/api/joinAttempts'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import ToggleSwitch from 'primevue/toggleswitch'
import Card from 'primevue/card'

/**
 * Live diagnostic view of LiteNetLib-level connection events on the 7DTD
 * server. Powered by the AuthWrapperServerDiagnostics Harmony patches +
 * JoinAttemptRing in-memory buffer (KitsuneCommand backend).
 *
 * Why this exists: 7DTD's vanilla "Peer disconnected in auth state: ... / 0"
 * log line gives operators zero useful information about WHY a client
 * failed to join. This page surfaces the actual LiteNetLib DisconnectReason
 * (PeerNotFound, Timeout, ConnectionFailed, etc.) plus the auth-state
 * machine context — the data that solved our 10-hour goodtimes NAT
 * investigation in 2026-05-27.
 *
 * Poll interval: 3s. Buffer capacity is 500 events server-side, lost on
 * restart (intentional — diagnostic-not-audit). Verbose-logging toggle
 * controls whether the same events ALSO produce nssm-stdout.log lines
 * (default off; chatty when on).
 */

const { t } = useI18n()
const toast = useToast()

const loading = ref(true)
const events = ref<JoinAttemptEvent[]>([])
const totalRecorded = ref(0)
const verboseLogging = ref(false)
const capacity = ref(500)
const autoRefresh = ref(true)
const refreshInterval = 3000

let pollTimer: number | null = null

async function loadEvents() {
  try {
    const res = await getJoinAttempts(500)
    events.value = res.events
    totalRecorded.value = res.totalRecorded
    verboseLogging.value = res.verboseLogging
    capacity.value = res.capacity
  } catch {
    toast.add({
      severity: 'error',
      summary: t('common.error'),
      detail: t('joinAttempts.failedToLoad', 'Failed to load join attempts'),
      life: 4000,
    })
  } finally {
    loading.value = false
  }
}

async function handleClear() {
  try {
    await clearJoinAttempts()
    toast.add({
      severity: 'success',
      summary: t('common.success'),
      detail: t('joinAttempts.cleared', 'Join-attempt ring cleared.'),
      life: 2000,
    })
    await loadEvents()
  } catch {
    toast.add({
      severity: 'error',
      summary: t('common.error'),
      detail: t('joinAttempts.failedToClear', 'Failed to clear ring.'),
      life: 4000,
    })
  }
}

async function handleToggleVerbose(value: boolean) {
  try {
    const res = await setVerboseLogging(value)
    verboseLogging.value = res.enabled
    toast.add({
      severity: 'info',
      summary: t('joinAttempts.verbose', 'Verbose console logging'),
      detail: res.enabled
        ? t('joinAttempts.verboseOn', 'Now logging each event to nssm-stdout.log as [KC-NetDiag] lines.')
        : t('joinAttempts.verboseOff', 'Console logging disabled; ring buffer recording continues.'),
      life: 3000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: t('common.error'),
      detail: t('joinAttempts.failedToToggle', 'Failed to toggle verbose logging.'),
      life: 4000,
    })
    // Revert toggle if API call failed
    verboseLogging.value = !value
  }
}

function startPolling() {
  if (pollTimer != null) return
  pollTimer = window.setInterval(loadEvents, refreshInterval)
}

function stopPolling() {
  if (pollTimer != null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
}

function toggleAutoRefresh(value: boolean) {
  autoRefresh.value = value
  if (value) startPolling()
  else stopPolling()
}

/** Group consecutive events from the same peer:port into a logical "attempt"
 * for at-a-glance reading. One Direct-Connect click bursts ~10-30 events;
 * grouping makes the table much more digestible. */
const groupedRows = computed(() => {
  const groups: Array<{
    key: string
    peerIp: string | null
    peerPort: number | null
    firstAt: string
    lastAt: string
    count: number
    finalResult: string | null
    eventTypes: Set<string>
    events: JoinAttemptEvent[]
  }> = []

  for (const ev of events.value) {
    if (ev.eventType === 'Update') continue  // skip the noise

    const key = `${ev.peerIp}:${ev.peerPort}`
    const last = groups.length > 0 ? groups[groups.length - 1] : null
    if (last && last.key === key) {
      last.count++
      last.firstAt = ev.timestamp  // events come newest-first, so the "first" of this group is the OLDEST we've seen so far
      last.events.push(ev)
      last.eventTypes.add(ev.eventType)
      if (ev.eventType === 'Disc' && !last.finalResult) last.finalResult = ev.result
    } else {
      groups.push({
        key,
        peerIp: ev.peerIp,
        peerPort: ev.peerPort,
        firstAt: ev.timestamp,
        lastAt: ev.timestamp,
        count: 1,
        finalResult: ev.eventType === 'Disc' ? ev.result : null,
        eventTypes: new Set([ev.eventType]),
        events: [ev],
      })
    }
  }
  return groups
})

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString()
  } catch {
    return iso
  }
}

function resultSeverity(result: string | null): string {
  if (!result) return 'secondary'
  // LiteNetLib reasons that mean "everything went fine, peer left normally"
  if (result === 'RemoteConnectionClose' || result === 'DisconnectPeerCalled') return 'info'
  // LiteNetLib reasons that signal a connection problem worth investigating
  if (result === 'PeerNotFound' || result === 'Timeout' || result === 'ConnectionFailed') return 'danger'
  // Wrapper-level rejections — admin-triggered or rate-limited
  if (result === 'Reject' || result === 'RejectForce' || result === 'ConnectionRejected') return 'warn'
  if (result === 'Accept') return 'success'
  return 'secondary'
}

onMounted(async () => {
  await loadEvents()
  startPolling()
})

onUnmounted(stopPolling)
</script>

<template>
  <div class="join-attempts-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('joinAttempts.title', 'Join Attempts') }}</h1>
      <p class="page-subtitle">
        {{ t('joinAttempts.subtitle',
          'Live view of LiteNetLib-layer connection events. Powered by KitsuneCommand\'s in-process ring buffer. Restart clears the ring.') }}
      </p>
    </div>

    <Card class="stats-card">
      <template #content>
        <div class="stats-row">
          <div class="stat">
            <div class="stat-label">{{ t('joinAttempts.totalRecorded', 'Events (lifetime)') }}</div>
            <div class="stat-value">{{ totalRecorded.toLocaleString() }}</div>
          </div>
          <div class="stat">
            <div class="stat-label">{{ t('joinAttempts.bufferUsage', 'Buffer usage') }}</div>
            <div class="stat-value">{{ events.length }} / {{ capacity }}</div>
          </div>
          <div class="stat stat-toggle">
            <div class="stat-label">{{ t('joinAttempts.verbose', 'Verbose console logging') }}</div>
            <ToggleSwitch
              :model-value="verboseLogging"
              @update:model-value="(v: boolean) => handleToggleVerbose(v)"
            />
          </div>
          <div class="stat stat-toggle">
            <div class="stat-label">{{ t('joinAttempts.autoRefresh', 'Auto-refresh') }}</div>
            <ToggleSwitch
              :model-value="autoRefresh"
              @update:model-value="(v: boolean) => toggleAutoRefresh(v)"
            />
          </div>
          <div class="stat stat-actions">
            <Button
              icon="pi pi-refresh"
              :label="t('common.refresh', 'Refresh')"
              size="small"
              text
              @click="loadEvents"
            />
            <Button
              icon="pi pi-trash"
              :label="t('joinAttempts.clear', 'Clear ring')"
              size="small"
              severity="danger"
              text
              @click="handleClear"
            />
          </div>
        </div>
      </template>
    </Card>

    <DataTable
      :value="groupedRows"
      :loading="loading"
      data-key="key"
      :rows="50"
      :paginator="groupedRows.length > 50"
      striped-rows
      class="attempts-table"
      :empty-message="t('joinAttempts.empty', 'No join attempts in the ring buffer. Try clicking Direct Connect on a 7DTD client to populate it.')"
    >
      <Column field="peerIp" :header="t('joinAttempts.peer', 'Peer')" :sortable="true">
        <template #body="{ data }">
          <code>{{ data.peerIp ?? '(null)' }}:{{ data.peerPort ?? '-' }}</code>
        </template>
      </Column>

      <Column :header="t('joinAttempts.time', 'When')" :sortable="false">
        <template #body="{ data }">
          {{ formatTime(data.firstAt) }}
          <span v-if="data.firstAt !== data.lastAt" class="muted"> – {{ formatTime(data.lastAt) }}</span>
        </template>
      </Column>

      <Column field="count" :header="t('joinAttempts.events', 'Events')" :sortable="true">
        <template #body="{ data }">
          <span class="event-count">{{ data.count }}</span>
        </template>
      </Column>

      <Column :header="t('joinAttempts.outcome', 'Outcome')">
        <template #body="{ data }">
          <Tag v-if="data.finalResult" :severity="resultSeverity(data.finalResult)" :value="data.finalResult" />
          <Tag v-else-if="data.eventTypes.has('Conn')" severity="success" value="Connected" />
          <Tag v-else severity="secondary" value="In-flight" />
        </template>
      </Column>

      <Column :header="t('joinAttempts.steps', 'Steps')">
        <template #body="{ data }">
          <span v-for="t in ['ConnReq','Recv','Conn','Disc']" :key="t" class="step" :class="{ active: data.eventTypes.has(t) }">
            {{ t }}
          </span>
        </template>
      </Column>
    </DataTable>
  </div>
</template>

<style scoped>
.join-attempts-view {
  padding: 1.5rem;
}

.page-header {
  margin-bottom: 1.5rem;
}

.page-title {
  margin: 0 0 0.25rem 0;
  font-size: 1.5rem;
}

.page-subtitle {
  margin: 0;
  color: var(--p-text-muted-color, #888);
  font-size: 0.9rem;
}

.stats-card {
  margin-bottom: 1.5rem;
}

.stats-row {
  display: flex;
  gap: 2rem;
  align-items: center;
  flex-wrap: wrap;
}

.stat {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  min-width: 120px;
}

.stat-toggle,
.stat-actions {
  flex-direction: column;
  align-items: flex-start;
}

.stat-actions {
  gap: 0.5rem;
}

.stat-label {
  font-size: 0.8rem;
  color: var(--p-text-muted-color, #888);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.stat-value {
  font-size: 1.4rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.attempts-table code {
  font-family: 'JetBrains Mono', Consolas, monospace;
  font-size: 0.85rem;
}

.muted {
  color: var(--p-text-muted-color, #888);
}

.event-count {
  display: inline-block;
  min-width: 2rem;
  text-align: center;
  font-variant-numeric: tabular-nums;
}

.step {
  display: inline-block;
  margin-right: 0.4rem;
  padding: 0.15rem 0.4rem;
  font-size: 0.75rem;
  border-radius: 3px;
  background: var(--p-surface-200, #eee);
  color: var(--p-text-muted-color, #888);
  font-family: 'JetBrains Mono', Consolas, monospace;
}

.step.active {
  background: var(--p-primary-color, #4a90e2);
  color: var(--p-primary-contrast-color, #fff);
}
</style>
