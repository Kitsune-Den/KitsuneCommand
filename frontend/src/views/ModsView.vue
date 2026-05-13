<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { getMods, uploadMod, deleteMod, toggleMod, type ModInfo } from '@/api/mods'
import {
  getModpackState,
  saveModpackDraft,
  buildModpack,
  publishModpack,
  archiveModpack,
  unarchiveModpack,
  deleteModpack,
  publicModpackDownloadUrl,
} from '@/api/modpack'
import type { ModpackState, ModpackInstalledMod } from '@/types'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import MultiSelect from 'primevue/multiselect'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import FileUpload from 'primevue/fileupload'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import ModDiscoveryTab from '@/components/ModDiscoveryTab.vue'

const { t } = useI18n()
const toast = useToast()

const activeTab = ref('0')
const loading = ref(true)
const mods = ref<ModInfo[]>([])
const search = ref('')
const confirmDeleteVisible = ref(false)
const modToDelete = ref<ModInfo | null>(null)
const uploading = ref(false)

const filteredMods = computed(() => {
  if (!search.value.trim()) return mods.value
  const q = search.value.toLowerCase()
  return mods.value.filter(m =>
    m.displayName.toLowerCase().includes(q) ||
    m.folderName.toLowerCase().includes(q) ||
    (m.author?.toLowerCase().includes(q)) ||
    (m.description?.toLowerCase().includes(q))
  )
})

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

async function loadMods() {
  loading.value = true
  try {
    mods.value = await getMods()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('mods.failedToLoad'), life: 4000 })
  } finally {
    loading.value = false
  }
}

async function handleUpload(event: { files: File[] }) {
  const file = event.files?.[0]
  if (!file) return

  uploading.value = true
  try {
    const result = await uploadMod(file)
    const installedCount = result.installedMods.length
    const names = result.installedMods.map((m) => m.displayName || m.folderName).join(', ')

    // Success toast — shape depends on how many mods landed
    if (installedCount === 0) {
      toast.add({
        severity: 'warn',
        summary: 'Nothing installed',
        detail: result.warnings.length
          ? result.warnings[0]
          : 'The zip extracted but no mods were detected.',
        life: 6000,
      })
    } else if (installedCount === 1) {
      toast.add({
        severity: 'success',
        summary: 'Mod installed',
        detail: names,
        life: 4000,
      })
    } else {
      toast.add({
        severity: 'success',
        summary: `${installedCount} mods installed`,
        detail: names,
        life: 5000,
      })
    }

    // Surface any warnings (replaced-existing, missing-ModInfo, etc.) separately
    // so the user sees them without them hiding behind the success toast.
    for (const warning of result.warnings) {
      toast.add({
        severity: 'warn',
        summary: 'Upload note',
        detail: warning,
        life: 7000,
      })
    }

    await loadMods()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('mods.failedToUpload'), life: 4000 })
  } finally {
    uploading.value = false
  }
}

function confirmDelete(mod: ModInfo) {
  modToDelete.value = mod
  confirmDeleteVisible.value = true
}

async function handleDelete() {
  if (!modToDelete.value) return
  const name = modToDelete.value.folderName
  confirmDeleteVisible.value = false

  try {
    await deleteMod(name)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('mods.deleteSuccess'), life: 4000 })
    await loadMods()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('mods.failedToDelete'), life: 4000 })
  }
}

async function handleToggle(mod: ModInfo) {
  try {
    await toggleMod(mod.folderName)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('mods.toggleSuccess'), life: 3000 })
    await loadMods()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('mods.failedToToggle'), life: 4000 })
  }
}

// ─── Modpack tab ──────────────────────────────────────────────
// Single-record modpack feature: admin picks installed mods, gives the
// pack a name + version, builds a zip, then publishes it so the login
// page download CTA appears.
const modpackLoading = ref(false)
const modpackSaving = ref(false)
const modpackBuilding = ref(false)
const modpackState = ref<ModpackState>({ modpack: null, modList: [], installedMods: [] })
const modpackForm = ref({
  name: '',
  version: '',
  modList: [] as string[],
  description: '',
})
const modpackConfirmDeleteVisible = ref(false)

const modpackStatusSeverity = computed<'success' | 'info' | 'secondary'>(() => {
  switch (modpackState.value.modpack?.status) {
    case 'published': return 'success'
    case 'draft': return 'info'
    default: return 'secondary'
  }
})

const modpackIsBuilt = computed(
  () => !!modpackState.value.modpack?.filename && (modpackState.value.modpack?.sizeBytes ?? 0) > 0,
)

const modpackOptions = computed<ModpackInstalledMod[]>(() =>
  modpackState.value.installedMods,
)

async function loadModpack() {
  modpackLoading.value = true
  try {
    modpackState.value = await getModpackState()
    if (modpackState.value.modpack) {
      modpackForm.value.name = modpackState.value.modpack.name
      modpackForm.value.version = modpackState.value.modpack.version
      modpackForm.value.modList = modpackState.value.modList
      modpackForm.value.description = modpackState.value.modpack.description ?? ''
    }
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('modpack.failedToLoad'), life: 4000 })
  } finally {
    modpackLoading.value = false
  }
}

async function handleSaveModpack() {
  modpackSaving.value = true
  try {
    await saveModpackDraft({
      name: modpackForm.value.name,
      version: modpackForm.value.version,
      modList: modpackForm.value.modList,
      description: modpackForm.value.description || null,
    })
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('modpack.saved'), life: 3000 })
    await loadModpack()
  } catch (err: any) {
    const detail = err.response?.data?.message ?? t('modpack.failedToSave')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 5000 })
  } finally {
    modpackSaving.value = false
  }
}

async function handleBuildModpack() {
  modpackBuilding.value = true
  try {
    await buildModpack()
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('modpack.built'), life: 4000 })
    await loadModpack()
  } catch (err: any) {
    const detail = err.response?.data?.message ?? t('modpack.failedToBuild')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 5000 })
  } finally {
    modpackBuilding.value = false
  }
}

async function handlePublishModpack() {
  try {
    await publishModpack()
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('modpack.published'), life: 3000 })
    await loadModpack()
  } catch (err: any) {
    const detail = err.response?.data?.message ?? t('modpack.failedToPublish')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 5000 })
  }
}

async function handleArchiveModpack() {
  try {
    await archiveModpack()
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('modpack.archived'), life: 3000 })
    await loadModpack()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('modpack.failedToArchive'), life: 4000 })
  }
}

async function handleUnarchiveModpack() {
  try {
    await unarchiveModpack()
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('modpack.unarchived'), life: 3000 })
    await loadModpack()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('modpack.failedToArchive'), life: 4000 })
  }
}

async function handleDeleteModpack() {
  modpackConfirmDeleteVisible.value = false
  try {
    await deleteModpack()
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('modpack.deleted'), life: 3000 })
    modpackForm.value = { name: '', version: '', modList: [], description: '' }
    await loadModpack()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('modpack.failedToDelete'), life: 4000 })
  }
}

onMounted(() => {
  loadMods()
  loadModpack()
})
</script>

<template>
  <div class="mods-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('mods.title') }}</h1>
    </div>

    <Tabs v-model:value="activeTab">
      <TabList>
        <Tab value="0"><i class="pi pi-box" style="margin-right: 0.5rem" />{{ t('mods.installed') }}</Tab>
        <Tab value="1"><i class="pi pi-compass" style="margin-right: 0.5rem" />{{ t('mods.discover') }}</Tab>
        <Tab value="2"><i class="pi pi-folder" style="margin-right: 0.5rem" />{{ t('modpack.tabLabel') }}</Tab>
      </TabList>
      <TabPanels>
        <TabPanel value="0">
          <div class="tab-content">
            <div class="installed-header">
              <FileUpload
                mode="basic"
                accept=".zip"
                :auto="true"
                :maxFileSize="200000000"
                chooseLabel="Upload Mod (.zip)"
                chooseIcon="pi pi-upload"
                :disabled="uploading"
                @select="handleUpload"
                class="upload-btn"
              />
            </div>

            <Message severity="warn" :closable="false" class="restart-banner">
              {{ t('mods.restartRequired') }}
            </Message>

            <div class="search-bar">
              <span class="p-input-icon-left" style="width: 100%">
                <i class="pi pi-search" />
                <InputText v-model="search" :placeholder="t('mods.searchPlaceholder')" class="search-input" />
              </span>
            </div>

            <DataTable
              :value="filteredMods"
              :loading="loading"
              stripedRows
              :paginator="filteredMods.length > 20"
              :rows="20"
              class="mods-table"
            >
              <Column field="displayName" :header="t('mods.name')" sortable>
                <template #body="{ data }">
                  <div class="mod-name-cell">
                    <span class="mod-name">{{ data.displayName }}</span>
                    <Tag v-if="data.isProtected" severity="info" value="KitsuneCommand" class="protected-tag" />
                    <Tag v-if="!data.isEnabled" severity="warn" :value="t('common.disabled')" />
                  </div>
                  <span v-if="data.description" class="mod-desc">{{ data.description }}</span>
                </template>
              </Column>
              <Column field="version" :header="t('mods.version')" style="width: 100px">
                <template #body="{ data }">
                  {{ data.version || '—' }}
                </template>
              </Column>
              <Column field="author" :header="t('mods.author')" style="width: 140px">
                <template #body="{ data }">
                  {{ data.author || '—' }}
                </template>
              </Column>
              <Column field="folderSize" :header="t('mods.size')" sortable style="width: 100px">
                <template #body="{ data }">
                  {{ formatSize(data.folderSize) }}
                </template>
              </Column>
              <Column :header="t('common.actions')" style="width: 120px">
                <template #body="{ data }">
                  <div class="action-buttons" v-if="!data.isProtected">
                    <Button
                      :icon="data.isEnabled ? 'pi pi-eye-slash' : 'pi pi-eye'"
                      text
                      rounded
                      size="small"
                      :severity="data.isEnabled ? 'warn' : 'success'"
                      @click="handleToggle(data)"
                      :title="data.isEnabled ? t('mods.disable') : t('mods.enable')"
                    />
                    <Button
                      icon="pi pi-trash"
                      text
                      rounded
                      size="small"
                      severity="danger"
                      @click="confirmDelete(data)"
                      :title="t('common.delete')"
                    />
                  </div>
                  <Tag v-else value="Protected" severity="secondary" />
                </template>
              </Column>
              <template #empty>
                <div class="empty-state">
                  <i class="pi pi-box" style="font-size: 2rem; color: var(--kc-text-secondary)" />
                  <p>{{ t('mods.noMods') }}</p>
                </div>
              </template>
            </DataTable>
          </div>
        </TabPanel>

        <TabPanel value="1">
          <ModDiscoveryTab />
        </TabPanel>

        <TabPanel value="2">
          <div class="tab-content modpack-tab">
            <Message severity="info" :closable="false">
              {{ t('modpack.intro') }}
            </Message>

            <div v-if="modpackLoading" class="empty-state">
              <i class="pi pi-spin pi-spinner" style="font-size: 2rem; color: var(--kc-text-secondary)" />
              <p>{{ t('common.loading') }}</p>
            </div>

            <div v-else class="modpack-form">
              <!-- Current status + summary card -->
              <div v-if="modpackState.modpack" class="modpack-status-card">
                <div class="modpack-status-row">
                  <Tag :severity="modpackStatusSeverity" :value="t('modpack.status.' + modpackState.modpack.status)" />
                  <span class="modpack-status-name">
                    {{ modpackState.modpack.name }} <span class="modpack-version">v{{ modpackState.modpack.version }}</span>
                  </span>
                </div>
                <div class="modpack-status-meta">
                  <span>{{ t('modpack.modCount', { n: modpackState.modpack.modCount }) }}</span>
                  <span v-if="modpackIsBuilt">{{ formatSize(modpackState.modpack.sizeBytes) }}</span>
                  <span v-if="modpackIsBuilt">{{ t('modpack.downloadsCount', { n: modpackState.modpack.downloadCount }) }}</span>
                </div>
                <div v-if="modpackState.modpack.status === 'published' && modpackIsBuilt" class="modpack-public-link">
                  <i class="pi pi-link" style="margin-right: 0.4rem" />
                  <a :href="publicModpackDownloadUrl" target="_blank" rel="noopener">
                    {{ t('modpack.publicDownloadLink') }}
                  </a>
                </div>
              </div>

              <!-- Edit form -->
              <div class="form-grid">
                <div class="form-group">
                  <label class="form-label">{{ t('modpack.fields.name') }}</label>
                  <InputText v-model="modpackForm.name" class="form-input" :placeholder="t('modpack.placeholders.name')" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('modpack.fields.version') }}</label>
                  <InputText v-model="modpackForm.version" class="form-input" placeholder="1.0.0" />
                </div>
              </div>

              <div class="form-group">
                <label class="form-label">{{ t('modpack.fields.modList') }}</label>
                <MultiSelect
                  v-model="modpackForm.modList"
                  :options="modpackOptions"
                  optionLabel="displayName"
                  optionValue="name"
                  :placeholder="t('modpack.placeholders.modList')"
                  filter
                  display="chip"
                  class="form-input modpack-multiselect"
                />
                <small class="form-hint">{{ t('modpack.modListHint') }}</small>
              </div>

              <div class="form-group">
                <label class="form-label">{{ t('modpack.fields.description') }}</label>
                <Textarea v-model="modpackForm.description" rows="3" class="form-input" :placeholder="t('modpack.placeholders.description')" />
              </div>

              <!-- Action row. Buttons gate themselves on current status. -->
              <div class="modpack-actions">
                <Button
                  :label="t('modpack.actions.saveDraft')"
                  icon="pi pi-save"
                  severity="info"
                  :loading="modpackSaving"
                  @click="handleSaveModpack"
                />
                <Button
                  :label="t('modpack.actions.build')"
                  icon="pi pi-box"
                  severity="secondary"
                  :loading="modpackBuilding"
                  :disabled="!modpackState.modpack || modpackForm.modList.length === 0"
                  @click="handleBuildModpack"
                />
                <Button
                  v-if="modpackState.modpack?.status !== 'published'"
                  :label="t('modpack.actions.publish')"
                  icon="pi pi-cloud-upload"
                  severity="success"
                  :disabled="!modpackIsBuilt"
                  @click="handlePublishModpack"
                />
                <Button
                  v-if="modpackState.modpack?.status === 'published'"
                  :label="t('modpack.actions.archive')"
                  icon="pi pi-inbox"
                  severity="warn"
                  @click="handleArchiveModpack"
                />
                <Button
                  v-if="modpackState.modpack?.status === 'archived'"
                  :label="t('modpack.actions.unarchive')"
                  icon="pi pi-undo"
                  severity="secondary"
                  @click="handleUnarchiveModpack"
                />
                <Button
                  v-if="modpackState.modpack"
                  :label="t('modpack.actions.delete')"
                  icon="pi pi-trash"
                  severity="danger"
                  text
                  @click="modpackConfirmDeleteVisible = true"
                />
              </div>
            </div>
          </div>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- Modpack delete confirmation -->
    <Dialog
      v-model:visible="modpackConfirmDeleteVisible"
      :header="t('common.confirmDelete')"
      modal
      :style="{ width: '420px' }"
    >
      <p>{{ t('modpack.confirmDelete') }}</p>
      <template #footer>
        <Button :label="t('common.cancel')" severity="secondary" text @click="modpackConfirmDeleteVisible = false" />
        <Button :label="t('common.delete')" severity="danger" @click="handleDeleteModpack" />
      </template>
    </Dialog>

    <!-- Delete confirmation -->
    <Dialog
      v-model:visible="confirmDeleteVisible"
      :header="t('common.confirmDelete')"
      modal
      :style="{ width: '420px' }"
    >
      <p>{{ t('mods.confirmDelete', { name: modToDelete?.displayName }) }}</p>
      <template #footer>
        <Button :label="t('common.cancel')" severity="secondary" text @click="confirmDeleteVisible = false" />
        <Button :label="t('common.delete')" severity="danger" @click="handleDelete" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.mods-view {
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

.tab-content {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.installed-header {
  display: flex;
  justify-content: flex-end;
}

.restart-banner {
  margin: 0;
}

.search-bar {
  display: flex;
}

.search-input {
  width: 100%;
}

/* Modpack tab — admin form for the player-facing modpack zip. */
.modpack-tab { max-width: 760px; }
.modpack-form { display: flex; flex-direction: column; gap: 1rem; }
.modpack-status-card {
  background: var(--kc-surface-1, rgba(255,255,255,0.03));
  border: 1px solid var(--kc-border, rgba(255,255,255,0.08));
  border-radius: 8px;
  padding: 0.9rem 1.1rem;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}
.modpack-status-row { display: flex; align-items: center; gap: 0.7rem; flex-wrap: wrap; }
.modpack-status-name { font-weight: 600; font-size: 1.05rem; }
.modpack-version { color: var(--kc-text-secondary); font-weight: 400; }
.modpack-status-meta {
  display: flex;
  gap: 1.25rem;
  color: var(--kc-text-secondary);
  font-size: 0.85rem;
  flex-wrap: wrap;
}
.modpack-public-link { font-size: 0.85rem; }
.modpack-public-link a {
  color: var(--kc-accent, #ff8800);
  text-decoration: none;
}
.modpack-public-link a:hover { text-decoration: underline; }
.form-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 1rem;
}
@media (max-width: 640px) {
  .form-grid { grid-template-columns: 1fr; }
}
.form-group { display: flex; flex-direction: column; gap: 0.35rem; }
.form-label {
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}
.form-input { width: 100%; }
.form-hint { color: var(--kc-text-secondary); font-size: 0.8rem; }
.modpack-multiselect :deep(.p-multiselect-label) { white-space: normal; }
.modpack-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; margin-top: 0.5rem; }

.mod-name-cell {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.mod-name {
  font-weight: 600;
}

.mod-desc {
  display: block;
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  margin-top: 0.15rem;
}

.protected-tag {
  font-size: 0.65rem;
}

.action-buttons {
  display: flex;
  gap: 0.25rem;
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
  .page-header { flex-direction: column; align-items: flex-start; gap: 0.5rem; }
  .installed-header { justify-content: flex-start; }
}
</style>
