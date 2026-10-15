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
  searchSevenDaysMods,
  getSevenDaysModsMetadata,
  type NexusMod,
  type NexusDiscoverSettings,
  type SevenDaysMod,
  type SevenDaysModsMetadata,
} from '@/api/mods'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import SelectButton from 'primevue/selectbutton'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'

/**
 * Browse mods on Nexus Mods or 7daystodiemods.com. Both sources map into the
 * same row shape so the table is shared; each keeps its own sorts and filters.
 * Rows link out to the mod's page on its site — nothing is downloaded here.
 */
type Source = 'nexus' | '7daystodiemods'

interface DiscoverRow {
  key: string
  name: string
  summary: string
  version: string | null
  author: string | null
  authorVerified: boolean
  likes: number
  downloads: number
  pictureUrl: string | null
  updatedAt: string | null
  url: string
  /** Short facts under the summary (server side, game versions). */
  details: string[]
  isAdult: boolean
}

const SOURCE_STORAGE_KEY = 'kc-mod-discover-source'

const { t } = useI18n()
const toast = useToast()

const loading = ref(false)
const rows = ref<DiscoverRow[]>([])
const totalCount = ref(0)
const search = ref('')
const offset = ref(0)
const pageSize = 20
const settings = ref<NexusDiscoverSettings | null>(null)

const source = ref<Source>(readSavedSource())
const sourceOptions = computed(() => [
  { label: t('mods.sourceNexus'), value: 'nexus' },
  { label: t('mods.sourceSevenDaysMods'), value: '7daystodiemods' },
])

const nexusSortOptions = computed(() => [
  { label: t('mods.trending'), value: 'endorsements' },
  { label: t('mods.latestAdded'), value: 'latest' },
  { label: t('mods.recentlyUpdated'), value: 'updated' },
  { label: t('mods.downloads'), value: 'downloads' },
])
const sevenDaysSortOptions = computed(() => [
  { label: t('mods.mostDownloaded'), value: 'downloads' },
  { label: t('mods.mostLiked'), value: 'likes' },
  { label: t('mods.mostViewed'), value: 'views' },
  { label: t('mods.latestAdded'), value: 'newest' },
  { label: t('mods.recentlyUpdated'), value: 'updated' },
])
const sortOptions = computed(() => (source.value === 'nexus' ? nexusSortOptions.value : sevenDaysSortOptions.value))
const sortMode = ref(source.value === 'nexus' ? 'endorsements' : 'downloads')

// 7daystodiemods.com filters (slugs from its /v1/metadata)
const metadata = ref<SevenDaysModsMetadata | null>(null)
const category = ref('')
const gameVersion = ref('')
const serverSide = ref('')
const withAny = (label: string, opts: { slug: string; name: string }[] = []) => [
  { label, value: '' },
  ...opts.map((o) => ({ label: o.name, value: o.slug })),
]
const categoryOptions = computed(() => withAny(t('mods.anyCategory'), metadata.value?.categories))
const gameVersionOptions = computed(() => withAny(t('mods.anyGameVersion'), metadata.value?.gameVersions))
const serverSideOptions = computed(() => withAny(t('mods.anyServerSide'), metadata.value?.serverSides))

// Settings dialog
const settingsVisible = ref(false)
const settingsApiKey = ref('')
const settingsCacheDuration = ref(60)
const validating = ref(false)
const savingSettings = ref(false)

// Debounce timer
let searchTimeout: ReturnType<typeof setTimeout> | null = null

function readSavedSource(): Source {
  try {
    return localStorage.getItem(SOURCE_STORAGE_KEY) === '7daystodiemods' ? '7daystodiemods' : 'nexus'
  } catch {
    return 'nexus'
  }
}

function formatNumber(n: number): string {
  if (n >= 1000000) return (n / 1000000).toFixed(1) + 'M'
  if (n >= 1000) return (n / 1000).toFixed(1) + 'K'
  return String(n)
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return '—'
  return new Date(dateStr).toLocaleDateString()
}

function plainText(html: string | null | undefined): string {
  return (html ?? '').replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim()
}

function fromNexus(mod: NexusMod): DiscoverRow {
  return {
    key: `nexus-${mod.modId}`,
    name: mod.name,
    summary: plainText(mod.summary),
    version: mod.version || null,
    author: mod.author || null,
    authorVerified: false,
    likes: mod.endorsements,
    downloads: mod.downloads,
    pictureUrl: mod.pictureUrl,
    updatedAt: mod.updatedAt,
    url: `https://www.nexusmods.com/7daystodie/mods/${mod.modId}`,
    details: [],
    isAdult: false,
  }
}

function fromSevenDaysMods(mod: SevenDaysMod): DiscoverRow {
  return {
    key: `7dtdm-${mod.slug}`,
    name: mod.title,
    summary: plainText(mod.summary),
    version: mod.version,
    author: mod.author,
    authorVerified: mod.authorVerified,
    likes: mod.likes,
    downloads: mod.downloads,
    pictureUrl: mod.thumbnailUrl,
    updatedAt: mod.updatedAt,
    url: mod.url,
    details: [mod.serverSideName, mod.gameVersions.join(', ')].filter((d): d is string => !!d),
    isAdult: mod.isAdult,
  }
}

async function loadSettings() {
  try {
    settings.value = await getDiscoverSettings()
  } catch {
    settings.value = { apiKey: '', hasApiKey: false, cacheDurationMinutes: 60 }
  }
}

async function loadMetadata() {
  if (metadata.value) return
  try {
    metadata.value = await getSevenDaysModsMetadata()
  } catch {
    /* filters just stay at "any" */
  }
}

async function loadMods() {
  loading.value = true
  try {
    if (source.value === 'nexus') {
      const result = await searchDiscoverMods({
        q: search.value.trim(),
        sort: sortMode.value,
        offset: offset.value,
        count: pageSize,
      })
      rows.value = result.mods.map(fromNexus)
      totalCount.value = result.totalCount
    } else {
      const result = await searchSevenDaysMods({
        q: search.value.trim(),
        sort: sortMode.value,
        page: Math.floor(offset.value / pageSize) + 1,
        limit: pageSize,
        category: category.value || undefined,
        gameVersion: gameVersion.value || undefined,
        serverSide: serverSide.value || undefined,
      })
      rows.value = result.mods.map(fromSevenDaysMods)
      totalCount.value = result.totalCount
    }
  } catch (err: any) {
    const fallback = source.value === 'nexus' ? t('mods.failedToLoadDiscover') : t('mods.failedToLoadSevenDaysMods')
    const msg = err?.response?.data?.message || fallback
    toast.add({ severity: 'error', summary: t('common.error'), detail: msg, life: 4000 })
  } finally {
    loading.value = false
  }
}

function reload() {
  offset.value = 0
  loadMods()
}

function onSearchInput() {
  if (searchTimeout) clearTimeout(searchTimeout)
  searchTimeout = setTimeout(reload, 400)
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

watch(source, async (next) => {
  try {
    localStorage.setItem(SOURCE_STORAGE_KEY, next)
  } catch {
    /* private window: the choice just isn't remembered */
  }
  sortMode.value = next === 'nexus' ? 'endorsements' : 'downloads'
  if (next === '7daystodiemods') await loadMetadata()
  reload()
})

onMounted(async () => {
  await loadSettings()
  if (source.value === '7daystodiemods') await loadMetadata()
  await loadMods()
})
</script>

<template>
  <div class="discover-tab">
    <!-- Source -->
    <div class="source-bar">
      <SelectButton
        v-model="source"
        :options="sourceOptions"
        optionLabel="label"
        optionValue="value"
        :allowEmpty="false"
        class="source-select"
      />
      <span class="source-hint">{{ source === 'nexus' ? t('mods.sourceNexusHint') : t('mods.sourceSevenDaysModsHint') }}</span>
    </div>

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
          @change="reload"
        />
        <Button icon="pi pi-refresh" text rounded :title="t('mods.cacheCleared')" @click="handleClearCache" />
        <Button v-if="source === 'nexus'" icon="pi pi-cog" text rounded :title="t('mods.configure')" @click="openSettings" />
      </div>
    </div>

    <!-- 7daystodiemods.com filters -->
    <div v-if="source === '7daystodiemods'" class="filters-bar">
      <Select v-model="category" :options="categoryOptions" optionLabel="label" optionValue="value" class="filter-select" @change="reload" />
      <Select v-model="gameVersion" :options="gameVersionOptions" optionLabel="label" optionValue="value" class="filter-select" @change="reload" />
      <Select v-model="serverSide" :options="serverSideOptions" optionLabel="label" optionValue="value" class="filter-select" @change="reload" />
    </div>

    <!-- Total count -->
    <div v-if="totalCount > 0 && !loading" class="result-count">
      {{ totalCount.toLocaleString() }} {{ t('mods.modsFound') }}
    </div>

    <!-- Mods table -->
    <DataTable
      :value="rows"
      :loading="loading"
      lazy
      :paginator="true"
      :rows="pageSize"
      :totalRecords="totalCount"
      :first="offset"
      dataKey="key"
      @page="onPage"
      stripedRows
      class="discover-table"
    >
      <Column field="name" :header="t('mods.name')" style="min-width: 280px">
        <template #body="{ data }">
          <div class="discover-mod-row">
            <img
              v-if="data.pictureUrl && !data.isAdult"
              :src="data.pictureUrl"
              :alt="data.name"
              class="mod-thumbnail"
              loading="lazy"
            />
            <div class="mod-info">
              <div class="mod-name-cell">
                <span class="mod-name">{{ data.name }}</span>
                <Tag v-if="data.version" :value="String(data.version).startsWith('v') ? data.version : 'v' + data.version" severity="info" class="version-tag" />
                <Tag v-if="data.isAdult" value="18+" severity="danger" class="version-tag" />
              </div>
              <span class="mod-summary">{{ data.summary }}</span>
              <span class="mod-author">
                {{ t('mods.author') }}: {{ data.author || '—' }}
                <i v-if="data.authorVerified" class="pi pi-verified verified-icon" :title="t('mods.verifiedAuthor')" />
              </span>
              <span v-if="data.details.length" class="mod-details">{{ data.details.join(' · ') }}</span>
            </div>
          </div>
        </template>
      </Column>
      <Column field="likes" :header="source === 'nexus' ? t('mods.endorsements') : t('mods.likes')" style="width: 120px">
        <template #body="{ data }">
          <span class="stat-cell">
            <i :class="source === 'nexus' ? 'pi pi-thumbs-up' : 'pi pi-heart'" />
            {{ formatNumber(data.likes) }}
          </span>
        </template>
      </Column>
      <Column field="downloads" :header="t('mods.downloads')" style="width: 120px">
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
          <a :href="data.url" target="_blank" rel="noopener noreferrer" class="nexus-link">
            <Button
              :label="source === 'nexus' ? t('mods.viewOnNexus') : t('mods.viewOnSevenDaysMods')"
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

.source-bar {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.source-hint {
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
}

.filters-bar {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.filter-select {
  min-width: 170px;
}

.mod-details {
  font-size: 0.72rem;
  color: var(--kc-text-secondary);
  opacity: 0.85;
}

.verified-icon {
  font-size: 0.75rem;
  color: var(--kc-cyan);
  margin-left: 0.2rem;
}

@media (max-width: 768px) {
  .controls-bar { flex-direction: column; align-items: stretch; }
  .controls-right { justify-content: flex-end; }
  .mod-thumbnail { width: 48px; height: 48px; }
  .api-key-row { flex-direction: column; }
  .filter-select { flex: 1 1 100%; }
}
</style>
