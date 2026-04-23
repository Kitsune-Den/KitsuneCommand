<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { getMods, uploadMod, deleteMod, toggleMod, type ModInfo } from '@/api/mods'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
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

onMounted(loadMods)
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
      </TabPanels>
    </Tabs>

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
