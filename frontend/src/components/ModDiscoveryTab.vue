<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import {
  searchDiscoverMods,
  getDiscoverSettings,
  updateDiscoverSettings,
  validateNexusKey,
  clearDiscoverCache,
  type NexusMod,
  type NexusDiscoverSettings,
} from '@/api/mods'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'

const { t } = useI18n()
const toast = useToast()

const loading = ref(false)
const mods = ref<NexusMod[]>([])
const totalCount = ref(0)
const search = ref('')
const offset = ref(0)
const pageSize = 20
const settings = ref<NexusDiscoverSettings | null>(null)

const sortMode = ref('endorsements')
const sortOptions = computed(() => [
  { label: t('mods.trending'), value: 'endorsements' },
  { label: t('mods.latestAdded'), value: 'latest' },
  { label: t('mods.recentlyUpdated'), value: 'updated' },
  { label: t('mods.downloads'), value: 'downloads' },
])

// Settings dialog
const settingsVisible = ref(false)
const settingsApiKey = ref('')
const settingsCacheDuration = ref(60)
const validating = ref(false)
const savingSettings = ref(false)

// Debounce timer
let searchTimeout: ReturnType<typeof setTimeout> | null = null

function formatNumber(n: number): string {
  if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M'
  if (n >= 1000) return (n / 1000).toFixed(1) + 'K'
  return String(n)
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '—'
  return new Date(dateStr).toLocaleDateString()
}

function nexusModUrl(mod: NexusMod): string {
  return `https://www.nexusmods.com/7daystodie/mods/${mod.modId}`
}

async function loadSettings() {
  try {
    settings.value = await getDiscoverSettings()
  } catch {
    settings.value = { apiKey: '', hasApiKey: false, cacheDurationMinutes: 60 }
  }
}

async function loadMods() {
  loading.value = true
  try {
    const result = await searchDiscoverMods({
      q: search.value.trim(),
      sort: sortMode.value,
      offset: offset.value,
      count: pageSize,
    })
    mods.value = result.mods
    totalCount.value = result.totalCount
  } catch (err: any) {
    const msg = err?.response?.data?.message || t('mods.failedToLoadDiscover')
    toast.add({ severity: 'error', summary: t('common.error'), detail: msg, life: 4000 })
  } finally {
    loading.value = false
  }
}

function onSearchInput() {
  if (searchTimeout) clearTimeout(searchTimeout)
  searchTimeout = setTimeout(() => {
    offset.value = 0
    loadMods()
  }, 400)
}

function onPage(event: { first: number; rows: number }) {
  offset.value = event.first
  loadMods()
}

function openSettings() {
  settingsApiKey.value = ''
  settingsCacheDuration.value = settings.value?.cacheDurationMinutes ?? 60
  settingsVisible.value = true
}

async function handleValidateKey() {
  if (!settingsApiKey.value.trim()) return

  validating.value = true
  try {
    const result = await validateNexusKey(settingsApiKey.value.trim())
    if (result.isValid) {
      toast.add({ severity: 'success', summary: t('common.success'), detail: `${t('mods.keyValid')} — ${result.name}`, life: 4000 })
    } else {
      toast.add({ severity: 'error', summary: t('common.error'), detail: t('mods.keyInvalid'), life: 4000 })
    }
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('mods.keyInvalid'), life: 4000 })
  } finally {
    validating.value = false
  }
}

async function handleSaveSettings() {
  savingSettings.value = true
  try {
    const payload: { apiKey?: string; cacheDurationMinutes?: number } = {
      cacheDurationMinutes: settingsCacheDuration.value,
    }
    if (settingsApiKey.value.trim()) {
      payload.apiKey = settingsApiKey.value.trim()
    }
    await updateDiscoverSettings(payload)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('mods.settingsSaved'), life: 3000 })
    settingsVisible.value = false
    await loadSettings()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('mods.failedToSaveSettings'), life: 4000 })
  } finally {
    savingSettings.value = false
  }
}

async function handleClearCache() {
  try {
    await clearDiscoverCache()
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('mods.cacheCleared'), life: 3000 })
    await loadMods()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('common.error'), life: 4000 })
  }
}

watch(sortMode, () => {
  offset.value = 0
  loadMods()
})

onMounted(async () => {
  await loadSettings()
  await loadMods()
})
</script>

<template>
  <div class="discover-tab">
    <!-- Controls bar -->
    <div class="controls-bar">
      <div class="search-bar">
        <span class="p-input-icon-left" style="flex: 1">
          <i class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('mods.searchPlaceholder')"
            class="search-input"
            @input="onSearchInput"
          />
        </span>
      </div>
      <div class="controls-right">
        <Select
          v-model="sortMode"
          :options="sortOptions"
          optionLabel="label"
          optionValue="value"
          class="sort-select"
        />
        <Button icon="pi pi-refresh" text rounded :title="t('mods.cacheCleared')" @click="handleClearCache" />
        <Button icon="pi pi-cog" text rounded :title="t('mods.configure')" @click="openSettings" />
      </div>
    </div>

    <!-- Total count -->
    <div v-if="totalCount > 0 && !loading" class="result-count">
      {{ totalCount.toLocaleString() }} {{ t('mods.modsFound') }}
    </div>

    <!-- Mods table -->
    <DataTable
      :value="mods"
      :loading="loading"
      lazy
      :paginator="true"
      :rows="pageSize"
      :totalRecords="totalCount"
      :first="offset"
      @page="onPage"
      stripedRows
      class="discover-table"
    >
      <Column field="name" :header="t('mods.name')" style="min-width: 280px">
        <template #body="{ data }">
          <div class="discover-mod-row">
            <img
              v-if="data.pictureUrl"
              :src="data.pictureUrl"
              :alt="data.name"
              class="mod-thumbnail"
            />
            <div class="mod-info">
              <div class="mod-name-cell">
                <span class="mod-name">{{ data.name }}</span>
                <Tag v-if="data.version" :value="'v' + data.version" severity="info" class="version-tag" />
              </div>
              <span class="mod-summary">{{ data.summary?.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim() }}</span>
              <span class="mod-author">{{ t('mods.author') }}: {{ data.author || '—' }}</span>
            </div>
          </div>
        </template>
      </Column>
      <Column field="endorsements" :header="t('mods.endorsements')" sortable style="width: 120px">
        <template #body="{ data }">
          <span class="stat-cell">
            <i class="pi pi-thumbs-up" />
            {{ formatNumber(data.endorsements) }}
          </span>
        </template>
      </Column>
      <Column field="downloads" :header="t('mods.downloads')" sortable style="width: 120px">
        <template #body="{ data }">
          <span class="stat-cell">
            <i class="pi pi-download" />
            {{ formatNumber(data.downloads) }}
          </span>
        </template>
      </Column>
      <Column field="updatedAt" :header="t('common.date')" style="width: 110px">
        <template #body="{ data }">
          {{ formatDate(data.updatedAt) }}
        </template>
      </Column>
      <Column :header="t('common.actions')" style="width: 100px">
        <template #body="{ data }">
          <a :href="nexusModUrl(data)" target="_blank" rel="noopener noreferrer" class="nexus-link">
            <Button
              :label="t('mods.viewOnNexus')"
              icon="pi pi-external-link"
              text
              size="small"
            />
          </a>
        </template>
      </Column>
      <template #empty>
        <div class="empty-state">
          <i class="pi pi-compass" style="font-size: 2rem; color: var(--kc-text-secondary)" />
          <p>{{ t('mods.noResults') }}</p>
        </div>
      </template>
    </DataTable>

    <!-- Settings dialog -->
    <Dialog
      v-model:visible="settingsVisible"
      :header="t('mods.nexusSettings')"
      modal
      :style="{ width: '480px' }"
    >
      <div class="settings-form">
        <div class="form-field">
          <label>{{ t('mods.apiKey') }}</label>
          <div class="api-key-row">
            <InputText
              v-model="settingsApiKey"
              type="password"
              :placeholder="settings?.hasApiKey ? t('mods.apiKeyPlaceholder') : t('mods.apiKeyPlaceholderNew')"
              class="api-key-input"
            />
            <Button
              :label="t('mods.validateKey')"
              icon="pi pi-check-circle"
              size="small"
              severity="info"
              :loading="validating"
              :disabled="!settingsApiKey.trim()"
              @click="handleValidateKey"
            />
          </div>
          <small class="form-hint">
            {{ t('mods.apiKeyHint') }}
          </small>
        </div>
        <div class="form-field">
          <label>{{ t('mods.cacheDuration') }}</label>
          <InputNumber v-model="settingsCacheDuration" :min="5" :max="1440" suffix=" min" />
        </div>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" severity="secondary" text @click="settingsVisible = false" />
        <Button :label="t('common.save')" :loading="savingSettings" @click="handleSaveSettings" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.discover-tab {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.controls-bar {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.search-bar {
  display: flex;
  flex: 1;
  min-width: 200px;
}

.search-input {
  width: 100%;
}

.controls-right {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.sort-select {
  min-width: 180px;
}

.result-count {
  font-size: 0.85rem;
  color: var(--kc-text-secondary);
}

.discover-mod-row {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
}

.mod-thumbnail {
  width: 64px;
  height: 64px;
  object-fit: cover;
  border-radius: 6px;
  flex-shrink: 0;
}

.mod-info {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}

.mod-name-cell {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.mod-name {
  font-weight: 600;
}

.version-tag {
  font-size: 0.65rem;
}

.mod-summary {
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.mod-author {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
}

.stat-cell {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.85rem;
}

.stat-cell i {
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
}

.nexus-link {
  text-decoration: none;
}

.settings-form {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.form-field label {
  font-weight: 600;
  font-size: 0.85rem;
}

.form-hint {
  color: var(--kc-text-secondary);
  font-size: 0.75rem;
}

.api-key-row {
  display: flex;
  gap: 0.5rem;
}

.api-key-input {
  flex: 1;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  padding: 2rem;
  color: var(--kc-text-secondary);
}

@media (max-width: 768px) {
  .controls-bar { flex-direction: column; align-items: stretch; }
  .controls-right { justify-content: flex-end; }
  .mod-thumbnail { width: 48px; height: 48px; }
  .api-key-row { flex-direction: column; }
}
</style>
