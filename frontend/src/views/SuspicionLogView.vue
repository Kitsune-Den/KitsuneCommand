<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import {
  getSuspicionEvents,
  reviewSuspicionEvents,
  getSuspicionSettings,
  updateSuspicionSettings,
  parseSuspicionDetails,
  type SuspicionEvent,
  type SuspicionSettings,
} from '@/api/suspicion'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import ToggleSwitch from 'primevue/toggleswitch'

const { t, te } = useI18n()
const toast = useToast()

const tab = ref<'log' | 'settings'>('log')

// --- Log ---

const loading = ref(false)
const events = ref<SuspicionEvent[]>([])
const total = ref(0)
const unreviewed = ref(0)
const pageIndex = ref(0)
const pageSize = ref(50)
const search = ref('')
const detectorFilter = ref<string | null>(null)
const severityFilter = ref<string | null>(null)
// 'open' = not yet reviewed (the default triage view), 'reviewed', or null for everything.
const statusFilter = ref<string | null>('open')
let searchTimeout: ReturnType<typeof setTimeout> | null = null

const detectors = ['inventory', 'movement', 'progression', 'killrate'] as const
const severities = ['high', 'medium', 'low'] as const

const detectorOptions = computed(() => [
  { label: t('suspicion.allDetectors'), value: null },
  ...detectors.map((d) => ({ label: t(`suspicion.detectors.${d}`), value: d })),
])
const severityOptions = computed(() => [
  { label: t('suspicion.allSeverities'), value: null },
  ...severities.map((s) => ({ label: t(`suspicion.severities.${s}`), value: s })),
])
const statusOptions = computed(() => [
  { label: t('suspicion.statusOpen'), value: 'open' },
  { label: t('suspicion.statusReviewed'), value: 'reviewed' },
  { label: t('suspicion.statusAll'), value: null },
])

async function fetchEvents() {
  loading.value = true
  try {
    const result = await getSuspicionEvents({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      detector: detectorFilter.value || undefined,
      severity: severityFilter.value || undefined,
      reviewed: statusFilter.value === null ? undefined : statusFilter.value === 'reviewed',
      search: search.value.trim() || undefined,
    })
    events.value = result.items
    total.value = result.total
    unreviewed.value = result.unreviewed
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('suspicion.failedToLoad'), life: 3000 })
  } finally {
    loading.value = false
  }
}

function onPage(event: { first: number; rows: number }) {
  pageIndex.value = Math.floor(event.first / event.rows)
  pageSize.value = event.rows
  fetchEvents()
}

function refilter() {
  pageIndex.value = 0
  fetchEvents()
}

watch(search, () => {
  if (searchTimeout) clearTimeout(searchTimeout)
  searchTimeout = setTimeout(refilter, 400)
})
watch([detectorFilter, severityFilter, statusFilter], refilter)

function severityTag(severity: string): string {
  switch (severity) {
    case 'high': return 'danger'
    case 'medium': return 'warn'
    default: return 'secondary'
  }
}

function label(prefix: string, key: string): string {
  const path = `${prefix}.${key}`
  return te(path) ? t(path) : key
}

/** A localized one-liner from the rule and its details; the server's English summary if the rule is unknown. */
function describe(evt: SuspicionEvent): string {
  const path = `suspicion.describe.${evt.rule}`
  if (!te(path)) return evt.summary || evt.rule
  const details = parseSuspicionDetails(evt)
  const params: Record<string, string> = {}
  for (const [k, v] of Object.entries(details)) params[k] = String(v)
  return t(path, params)
}

function formatDate(value: string | null): string {
  if (!value) return ''
  const d = new Date(value.replace(' ', 'T') + (value.endsWith('Z') ? '' : 'Z'))
  if (isNaN(d.getTime())) return value
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function formatPosition(evt: SuspicionEvent): string | null {
  if (evt.posX == null || evt.posZ == null) return null
  return `${Math.round(evt.posX)}, ${Math.round(evt.posY ?? 0)}, ${Math.round(evt.posZ)}`
}

async function setReviewed(ids: number[], reviewed: boolean, note?: string) {
  if (ids.length === 0) return
  try {
    await reviewSuspicionEvents(ids, reviewed, note)
    toast.add({
      severity: 'success',
      summary: reviewed ? t('suspicion.markedReviewed', { count: ids.length }) : t('suspicion.reopened'),
      life: 2000,
    })
    await fetchEvents()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('suspicion.failedToUpdate'), life: 3000 })
  }
}

const openOnPage = computed(() => events.value.filter((e) => !e.reviewed).map((e) => e.id))

// --- Details dialog ---

const selected = ref<SuspicionEvent | null>(null)
const reviewNote = ref('')
const selectedDetails = computed(() => (selected.value ? Object.entries(parseSuspicionDetails(selected.value)) : []))

function openDetails(evt: SuspicionEvent) {
  selected.value = evt
  reviewNote.value = evt.reviewNote || ''
}

async function reviewSelected(reviewed: boolean) {
  if (!selected.value) return
  const id = selected.value.id
  selected.value = null
  await setReviewed([id], reviewed, reviewed ? reviewNote.value.trim() || undefined : undefined)
}

// --- Settings ---

const settings = ref<SuspicionSettings | null>(null)
const loadingSettings = ref(false)
const savingSettings = ref(false)
const exemptText = ref('')
const allowText = ref('')

type Section = 'inventory' | 'movement' | 'progression' | 'killRate'
interface NumberField { key: string; min: number; max: number; step?: number }
interface SectionDef { key: Section; toggles: string[]; numbers: NumberField[] }

// Data-driven so each detector card stays a few lines of template. Labels and
// hints live under suspicion.settings.<section>.<key> / <key>Hint.
const sections: SectionDef[] = [
  {
    key: 'inventory',
    toggles: ['flagDevItems', 'flagHiddenItems', 'flagOverQuality', 'flagOverStack', 'flagCountJumps'],
    numbers: [
      { key: 'scanIntervalSeconds', min: 15, max: 3600 },
      { key: 'maxQuality', min: 0, max: 100 },
      { key: 'countJumpThreshold', min: 0, max: 1000000 },
    ],
  },
  {
    key: 'movement',
    toggles: ['flagFlying'],
    numbers: [
      { key: 'maxHorizontalSpeed', min: 0, max: 1000, step: 0.5 },
      { key: 'speedSamplesRequired', min: 1, max: 20 },
      { key: 'jumpDistance', min: 0, max: 100000 },
      { key: 'flyHeight', min: 0, max: 1000 },
      { key: 'flySamplesRequired', min: 1, max: 100 },
      { key: 'graceSeconds', min: 0, max: 300 },
    ],
  },
  {
    key: 'progression',
    toggles: [],
    numbers: [
      { key: 'windowMinutes', min: 1, max: 1440 },
      { key: 'maxLevelGain', min: 0, max: 1000 },
      { key: 'maxUnexplainedSkillPoints', min: 0, max: 1000 },
      { key: 'warmupSeconds', min: 0, max: 3600 },
    ],
  },
  {
    key: 'killRate',
    toggles: [],
    numbers: [{ key: 'maxZombieKillsPerMinute', min: 0, max: 10000 }],
  },
]

function section(key: Section): Record<string, any> {
  return (settings.value as any)[key]
}

const lines = (text: string) => text.split('\n').map((l) => l.trim()).filter(Boolean)

function applySettings(s: SuspicionSettings) {
  settings.value = s
  exemptText.value = (s.exemptPlayerIds || []).join('\n')
  allowText.value = (s.inventory?.allowList || []).join('\n')
}

async function fetchSettings() {
  loadingSettings.value = true
  try {
    applySettings(await getSuspicionSettings())
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('suspicion.failedToLoadSettings'), life: 3000 })
  } finally {
    loadingSettings.value = false
  }
}

async function saveSettings() {
  if (!settings.value) return
  savingSettings.value = true
  try {
    const payload: SuspicionSettings = {
      ...settings.value,
      exemptPlayerIds: lines(exemptText.value),
      inventory: { ...settings.value.inventory, allowList: lines(allowText.value) },
    }
    applySettings(await updateSuspicionSettings(payload))
    toast.add({ severity: 'success', summary: t('suspicion.settingsSaved'), life: 2000 })
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('suspicion.failedToSaveSettings'), life: 3000 })
  } finally {
    savingSettings.value = false
  }
}

const discordSeverityOptions = computed(() =>
  severities.map((s) => ({ label: t(`suspicion.severities.${s}`), value: s })),
)

watch(tab, (value) => {
  if (value === 'settings' && !settings.value) fetchSettings()
})

onMounted(fetchEvents)
</script>

<template>
  <div class="suspicion-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('suspicion.title') }}</h1>
      <Tag v-if="unreviewed > 0" class="unreviewed-tag" severity="warn" :value="t('suspicion.unreviewedCount', { count: unreviewed })" />
    </div>
    <p class="page-intro">{{ t('suspicion.intro') }}</p>

    <div class="sub-tabs">
      <button class="sub-tab" :class="{ 'sub-tab--active': tab === 'log' }" data-test="tab-log" @click="tab = 'log'">
        {{ t('suspicion.tabLog') }}
      </button>
      <button class="sub-tab" :class="{ 'sub-tab--active': tab === 'settings' }" data-test="tab-settings" @click="tab = 'settings'">
        {{ t('suspicion.tabSettings') }}
      </button>
    </div>

    <!-- Log -->
    <template v-if="tab === 'log'">
      <div class="toolbar">
        <span class="p-input-icon-left search-wrapper">
          <i class="pi pi-search" />
          <InputText v-model="search" :placeholder="t('suspicion.searchPlaceholder')" class="search-input" />
        </span>
        <Select v-model="detectorFilter" :options="detectorOptions" optionLabel="label" optionValue="value" class="filter" />
        <Select v-model="severityFilter" :options="severityOptions" optionLabel="label" optionValue="value" class="filter" />
        <Select v-model="statusFilter" :options="statusOptions" optionLabel="label" optionValue="value" class="filter" />
        <Button icon="pi pi-refresh" text severity="secondary" :loading="loading" @click="fetchEvents" />
        <Button
          v-if="openOnPage.length > 0"
          icon="pi pi-check-square"
          :label="t('suspicion.markPageReviewed')"
          severity="secondary"
          size="small"
          data-test="mark-page"
          @click="setReviewed(openOnPage, true)"
        />
      </div>

      <DataTable
        :value="events"
        :loading="loading"
        stripedRows
        :paginator="true"
        :rows="pageSize"
        :totalRecords="total"
        :lazy="true"
        :rowsPerPageOptions="[25, 50, 100]"
        dataKey="id"
        @page="onPage"
      >
        <Column field="createdAt" :header="t('common.date')" style="width: 150px">
          <template #body="{ data }">
            <span class="date-text">{{ formatDate(data.createdAt) }}</span>
          </template>
        </Column>

        <Column field="playerName" :header="t('common.player')">
          <template #body="{ data }">
            <div class="player-cell">
              <span class="player-name">{{ data.playerName || data.playerId || '—' }}</span>
              <span v-if="data.playerName && data.playerId" class="player-id">{{ data.playerId }}</span>
            </div>
          </template>
        </Column>

        <Column field="rule" :header="t('suspicion.check')">
          <template #body="{ data }">
            <div class="check-cell">
              <div class="check-head">
                <Tag :value="label('suspicion.detectors', data.detector)" severity="info" />
                <span class="rule-name">{{ label('suspicion.rules', data.rule) }}</span>
              </div>
              <span class="describe" data-test="describe">{{ describe(data) }}</span>
            </div>
          </template>
        </Column>

        <Column field="severity" :header="t('suspicion.severity')" style="width: 110px">
          <template #body="{ data }">
            <Tag :value="label('suspicion.severities', data.severity)" :severity="severityTag(data.severity)" />
          </template>
        </Column>

        <Column field="posX" :header="t('common.position')" style="width: 150px">
          <template #body="{ data }">
            <code v-if="formatPosition(data)" class="position-text">{{ formatPosition(data) }}</code>
            <span v-else class="empty-text">—</span>
          </template>
        </Column>

        <Column field="reviewed" :header="t('common.status')" style="width: 150px">
          <template #body="{ data }">
            <span v-if="data.reviewed" class="reviewed-text" data-test="reviewed">
              <i class="pi pi-check" /> {{ data.reviewedBy || t('suspicion.reviewed') }}
            </span>
            <Tag v-else :value="t('suspicion.open')" severity="warn" />
          </template>
        </Column>

        <Column :header="t('common.actions')" style="width: 110px">
          <template #body="{ data }">
            <div class="row-actions">
              <Button icon="pi pi-eye" text rounded size="small" :aria-label="t('suspicion.details')" data-test="details" @click="openDetails(data)" />
              <Button
                v-if="!data.reviewed"
                icon="pi pi-check"
                text
                rounded
                size="small"
                severity="success"
                :aria-label="t('suspicion.markReviewed')"
                data-test="review"
                @click="setReviewed([data.id], true)"
              />
              <Button
                v-else
                icon="pi pi-undo"
                text
                rounded
                size="small"
                severity="secondary"
                :aria-label="t('suspicion.reopen')"
                data-test="reopen"
                @click="setReviewed([data.id], false)"
              />
            </div>
          </template>
        </Column>

        <template #empty>
          <div class="empty-state" data-test="empty">
            <i class="pi pi-shield" style="font-size: 2rem; color: var(--kc-text-secondary)" />
            <p>{{ t('suspicion.empty') }}</p>
          </div>
        </template>
      </DataTable>
    </template>

    <!-- Settings -->
    <template v-else>
      <div v-if="loadingSettings || !settings" class="loading-state">{{ t('common.loading') }}</div>
      <div v-else class="settings-grid" data-test="settings">
        <div class="settings-card">
          <h3 class="card-title">{{ t('suspicion.settings.general') }}</h3>
          <div class="toggle-row">
            <label>{{ t('suspicion.settings.enabled') }}</label>
            <ToggleSwitch v-model="settings.enabled" />
          </div>
          <div class="toggle-row">
            <label>{{ t('suspicion.settings.ignoreAdmins') }}</label>
            <ToggleSwitch v-model="settings.ignoreAdmins" />
          </div>
          <div class="form-group">
            <label class="form-label">{{ t('suspicion.settings.exemptPlayerIds') }}</label>
            <Textarea v-model="exemptText" rows="3" class="form-input mono" />
            <small class="settings-hint">{{ t('suspicion.settings.exemptPlayerIdsHint') }}</small>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">{{ t('suspicion.settings.repeatCooldownMinutes') }}</label>
              <InputNumber v-model="settings.repeatCooldownMinutes" :min="0" :max="1440" class="form-input" />
            </div>
            <div class="form-group">
              <label class="form-label">{{ t('suspicion.settings.retentionDays') }}</label>
              <InputNumber v-model="settings.retentionDays" :min="0" :max="3650" class="form-input" />
            </div>
          </div>
          <small class="settings-hint">{{ t('suspicion.settings.retentionDaysHint') }}</small>
        </div>

        <div class="settings-card">
          <h3 class="card-title">{{ t('suspicion.settings.discord') }}</h3>
          <div class="form-group">
            <label class="form-label">{{ t('suspicion.settings.discordWebhookUrl') }}</label>
            <InputText v-model="settings.discordWebhookUrl" placeholder="https://discord.com/api/webhooks/..." class="form-input" />
          </div>
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">{{ t('suspicion.settings.discordMinSeverity') }}</label>
              <Select v-model="settings.discordMinSeverity" :options="discordSeverityOptions" optionLabel="label" optionValue="value" class="form-input" />
            </div>
            <div class="form-group">
              <label class="form-label">{{ t('suspicion.settings.discordCooldownMinutes') }}</label>
              <InputNumber v-model="settings.discordCooldownMinutes" :min="0" :max="1440" class="form-input" />
            </div>
          </div>
          <small class="settings-hint">{{ t('suspicion.settings.discordHint') }}</small>
        </div>

        <div v-for="sec in sections" :key="sec.key" class="settings-card" :data-test="`section-${sec.key}`">
          <h3 class="card-title">{{ t(`suspicion.settings.${sec.key}.title`) }}</h3>
          <p class="card-subtitle">{{ t(`suspicion.settings.${sec.key}.subtitle`) }}</p>
          <div class="toggle-row">
            <label>{{ t('common.enabled') }}</label>
            <ToggleSwitch v-model="section(sec.key).enabled" />
          </div>
          <div v-for="toggle in sec.toggles" :key="toggle" class="toggle-row toggle-row--sub">
            <label>{{ t(`suspicion.settings.${sec.key}.${toggle}`) }}</label>
            <ToggleSwitch v-model="section(sec.key)[toggle]" :disabled="!section(sec.key).enabled" />
          </div>
          <div v-for="field in sec.numbers" :key="field.key" class="form-group">
            <label class="form-label">{{ t(`suspicion.settings.${sec.key}.${field.key}`) }}</label>
            <InputNumber
              v-model="section(sec.key)[field.key]"
              :min="field.min"
              :max="field.max"
              :step="field.step ?? 1"
              :minFractionDigits="field.step && field.step < 1 ? 1 : 0"
              :maxFractionDigits="field.step && field.step < 1 ? 1 : 0"
              :disabled="!section(sec.key).enabled"
              class="form-input"
            />
            <small v-if="te(`suspicion.settings.${sec.key}.${field.key}Hint`)" class="settings-hint">
              {{ t(`suspicion.settings.${sec.key}.${field.key}Hint`) }}
            </small>
          </div>
          <div v-if="sec.key === 'inventory'" class="form-group">
            <label class="form-label">{{ t('suspicion.settings.inventory.allowList') }}</label>
            <Textarea v-model="allowText" rows="5" class="form-input mono" :disabled="!settings.inventory.enabled" />
            <small class="settings-hint">{{ t('suspicion.settings.inventory.allowListHint') }}</small>
          </div>
        </div>

        <Button
          :label="t('common.save')"
          icon="pi pi-save"
          class="save-btn"
          :loading="savingSettings"
          data-test="save-settings"
          @click="saveSettings"
        />
      </div>
    </template>

    <Dialog
      :visible="selected !== null"
      modal
      :header="selected ? label('suspicion.rules', selected.rule) : ''"
      :style="{ width: '520px' }"
      @update:visible="(v: boolean) => { if (!v) selected = null }"
    >
      <div v-if="selected" class="details" data-test="details-dialog">
        <p class="describe">{{ describe(selected) }}</p>
        <table class="details-table">
          <tbody>
            <tr>
              <th>{{ t('common.player') }}</th>
              <td>{{ selected.playerName || '—' }} <span class="player-id">{{ selected.playerId }}</span></td>
            </tr>
            <tr>
              <th>{{ t('common.date') }}</th>
              <td>{{ formatDate(selected.createdAt) }}</td>
            </tr>
            <tr v-for="[key, value] in selectedDetails" :key="key">
              <th>{{ key }}</th>
              <td><code>{{ value }}</code></td>
            </tr>
            <tr v-if="selected.reviewed">
              <th>{{ t('suspicion.reviewed') }}</th>
              <td>{{ selected.reviewedBy }} · {{ formatDate(selected.reviewedAt) }}</td>
            </tr>
          </tbody>
        </table>
        <div v-if="!selected.reviewed" class="form-group">
          <label class="form-label">{{ t('suspicion.reviewNote') }}</label>
          <Textarea v-model="reviewNote" rows="2" class="form-input" :placeholder="t('suspicion.reviewNotePlaceholder')" />
        </div>
        <p v-else-if="selected.reviewNote" class="review-note">{{ selected.reviewNote }}</p>
      </div>
      <template #footer>
        <Button :label="t('common.close')" text severity="secondary" @click="selected = null" />
        <Button
          v-if="selected && !selected.reviewed"
          :label="t('suspicion.markReviewed')"
          icon="pi pi-check"
          data-test="dialog-review"
          @click="reviewSelected(true)"
        />
        <Button
          v-else-if="selected"
          :label="t('suspicion.reopen')"
          icon="pi pi-undo"
          severity="secondary"
          @click="reviewSelected(false)"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.suspicion-view { display: flex; flex-direction: column; gap: 1rem; }
.page-header { display: flex; align-items: center; gap: 0.75rem; }
.page-title { font-size: 1.5rem; font-weight: 600; }
.page-intro { margin: -0.5rem 0 0; color: var(--kc-text-secondary); font-size: 0.9rem; max-width: 800px; }

.sub-tabs { display: flex; gap: 0.25rem; border-bottom: 1px solid var(--kc-border); }
.sub-tab { padding: 0.5rem 1rem; border: none; background: none; color: var(--kc-text-secondary); cursor: pointer; border-bottom: 2px solid transparent; font-size: 0.9rem; transition: all 0.15s ease; }
.sub-tab:hover { color: var(--kc-text-primary); }
.sub-tab--active { color: var(--kc-cyan); border-bottom-color: var(--kc-cyan); }

.toolbar { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; }
.search-wrapper { flex: 1; min-width: 200px; max-width: 320px; }
.search-input { width: 100%; }
.filter { min-width: 150px; }

.player-cell { display: flex; flex-direction: column; }
.player-name { font-weight: 500; }
.player-id { font-size: 0.75rem; color: var(--kc-text-secondary); }
.check-cell { display: flex; flex-direction: column; gap: 0.25rem; }
.check-head { display: flex; align-items: center; gap: 0.5rem; }
.rule-name { font-weight: 500; }
.describe { font-size: 0.85rem; color: var(--kc-text-secondary); }
.date-text { font-size: 0.85rem; color: var(--kc-text-secondary); }
.position-text { font-size: 0.8rem; color: var(--kc-cyan); background: rgba(0, 212, 255, 0.08); padding: 0.15rem 0.5rem; border-radius: 4px; }
.empty-text { color: var(--kc-text-secondary); }
.reviewed-text { font-size: 0.85rem; color: var(--kc-text-secondary); }
.row-actions { display: flex; gap: 0.15rem; }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 2rem; color: var(--kc-text-secondary); }
.loading-state { padding: 2rem; color: var(--kc-text-secondary); }

.settings-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 1rem; align-items: start; }
.settings-card { background: var(--kc-bg-card); border: 1px solid var(--kc-border); border-radius: 8px; padding: 1rem 1.25rem; }
.card-title { font-size: 1.05rem; font-weight: 600; margin: 0 0 0.25rem; }
.card-subtitle { font-size: 0.85rem; color: var(--kc-text-secondary); margin: 0 0 1rem; }
.toggle-row { display: flex; align-items: center; justify-content: space-between; gap: 1rem; margin-bottom: 0.85rem; }
.toggle-row label { font-weight: 500; }
.toggle-row--sub label { font-weight: 400; font-size: 0.9rem; }
.form-group { margin-bottom: 0.85rem; }
.form-row { display: flex; gap: 1rem; }
.form-row .form-group { flex: 1; }
.form-label { display: block; font-size: 0.8rem; color: var(--kc-text-secondary); text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 0.35rem; }
.form-input { width: 100%; }
.mono { font-family: monospace; font-size: 0.85rem; }
.settings-hint { display: block; font-size: 0.8rem; color: var(--kc-text-secondary); margin-top: 0.25rem; }
.save-btn { grid-column: 1 / -1; justify-self: start; }

.details { display: flex; flex-direction: column; gap: 0.75rem; }
.details-table { width: 100%; border-collapse: collapse; font-size: 0.9rem; }
.details-table th { text-align: left; font-weight: 500; color: var(--kc-text-secondary); padding: 0.25rem 0.75rem 0.25rem 0; white-space: nowrap; vertical-align: top; }
.details-table td { padding: 0.25rem 0; word-break: break-word; }
.review-note { font-style: italic; color: var(--kc-text-secondary); margin: 0; }

@media (max-width: 768px) {
  .search-wrapper { max-width: none; flex: 1 1 100%; }
  .filter { flex: 1 1 45%; min-width: 0; }
  .form-row { flex-direction: column; gap: 0; }
  .settings-grid { grid-template-columns: 1fr; }
}
</style>
