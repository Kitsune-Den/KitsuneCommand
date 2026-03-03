<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { usePermissions } from '@/composables/usePermissions'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import { getHomes, deleteHome, teleportToHome } from '@/api/teleport'
import type { HomeLocation } from '@/types'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'

const router = useRouter()
const toast = useToast()
const confirmService = useConfirm()
const { canManageTeleport, canExecuteTeleport } = usePermissions()

const loading = ref(true)
const homes = ref<HomeLocation[]>([])
const totalHomes = ref(0)
const pageIndex = ref(0)
const pageSize = ref(50)
const searchFilter = ref('')
let searchTimeout: ReturnType<typeof setTimeout> | null = null

async function fetchHomes() {
  loading.value = true
  try {
    const result = await getHomes({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      search: searchFilter.value || undefined,
    })
    homes.value = result.items
    totalHomes.value = result.total
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load home locations', life: 3000 })
  } finally {
    loading.value = false
  }
}

function onPage(event: { first: number; rows: number }) {
  pageIndex.value = Math.floor(event.first / event.rows)
  pageSize.value = event.rows
  fetchHomes()
}

function onSearch() {
  if (searchTimeout) clearTimeout(searchTimeout)
  searchTimeout = setTimeout(() => {
    pageIndex.value = 0
    fetchHomes()
  }, 400)
}

function confirmDeleteHome(home: HomeLocation) {
  confirmService.require({
    message: `Delete home "${home.homeName}" for ${home.playerName || home.playerId}?`,
    header: 'Confirm Delete',
    icon: 'pi pi-trash',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteHome(home.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: `Home "${home.homeName}" deleted`, life: 3000 })
        fetchHomes()
      } catch {
        toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to delete home', life: 3000 })
      }
    },
  })
}

async function handleTeleportOwner(home: HomeLocation) {
  try {
    const result = await teleportToHome(home.id)
    toast.add({ severity: 'success', summary: 'Teleported', detail: result.message, life: 4000 })
  } catch (err: any) {
    const msg = err?.response?.data?.message || 'Failed to teleport player'
    toast.add({ severity: 'error', summary: 'Error', detail: msg, life: 4000 })
  }
}

function navigateTo(tab: string) {
  if (tab === 'cities') router.push({ name: 'TeleportCities' })
  else if (tab === 'history') router.push({ name: 'TeleportHistory' })
}

watch(searchFilter, onSearch)
onMounted(fetchHomes)
</script>

<template>
  <div class="teleport-view">
    <div class="page-header">
      <h1 class="page-title">Teleport</h1>
    </div>

    <!-- Sub-tab navigation -->
    <div class="sub-tabs">
      <button class="sub-tab" @click="navigateTo('cities')">Cities</button>
      <button class="sub-tab sub-tab--active">Homes</button>
      <button class="sub-tab" @click="navigateTo('history')">History</button>
    </div>

    <div class="toolbar">
      <span class="p-input-icon-left search-wrapper">
        <i class="pi pi-search" />
        <InputText v-model="searchFilter" placeholder="Search by player or home name..." class="search-input" />
      </span>
      <Button icon="pi pi-refresh" text severity="secondary" @click="fetchHomes" :loading="loading" />
    </div>

    <DataTable
      :value="homes"
      :loading="loading"
      stripedRows
      :paginator="true"
      :rows="pageSize"
      :totalRecords="totalHomes"
      :lazy="true"
      :rowsPerPageOptions="[25, 50, 100]"
      @page="onPage"
    >
      <Column field="playerName" header="Player">
        <template #body="{ data }">
          <div class="player-name">
            <i class="pi pi-user" />
            <span>{{ data.playerName || data.playerId }}</span>
          </div>
        </template>
      </Column>

      <Column field="homeName" header="Home Name" />

      <Column field="position" header="Position" style="width: 220px">
        <template #body="{ data }">
          <code class="position-text">{{ data.position }}</code>
        </template>
      </Column>

      <Column header="Actions" style="width: 120px">
        <template #body="{ data }">
          <div class="action-buttons">
            <Button
              v-if="canExecuteTeleport"
              icon="pi pi-send"
              text
              severity="info"
              size="small"
              @click="handleTeleportOwner(data)"
              title="Teleport Owner to Home"
            />
            <Button
              v-if="canManageTeleport"
              icon="pi pi-trash"
              text
              severity="danger"
              size="small"
              @click="confirmDeleteHome(data)"
              title="Delete"
            />
          </div>
        </template>
      </Column>

      <template #empty>
        <div class="empty-state">
          <i class="pi pi-home" style="font-size: 2rem; color: var(--kc-text-secondary)" />
          <p>No home locations yet</p>
          <span class="empty-hint">Player homes will appear here once players save their locations.</span>
        </div>
      </template>
    </DataTable>
  </div>
</template>

<style scoped>
.teleport-view { display: flex; flex-direction: column; gap: 1rem; }
.page-header { display: flex; align-items: center; gap: 1rem; }
.page-title { font-size: 1.5rem; font-weight: 600; }

.sub-tabs { display: flex; gap: 0.25rem; border-bottom: 1px solid var(--kc-border); }
.sub-tab { padding: 0.5rem 1rem; border: none; background: none; color: var(--kc-text-secondary); cursor: pointer; border-bottom: 2px solid transparent; font-size: 0.9rem; transition: all 0.15s ease; }
.sub-tab:hover { color: var(--kc-text-primary); }
.sub-tab--active { color: var(--kc-cyan); border-bottom-color: var(--kc-cyan); }

.toolbar { display: flex; align-items: center; gap: 0.5rem; }
.search-wrapper { flex: 1; max-width: 350px; }
.search-input { width: 100%; }

.player-name { display: flex; align-items: center; gap: 0.5rem; }
.action-buttons { display: flex; gap: 0.25rem; }
.position-text { font-size: 0.85rem; color: var(--kc-cyan); background: rgba(0, 212, 255, 0.08); padding: 0.15rem 0.5rem; border-radius: 4px; }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 2rem; color: var(--kc-text-secondary); }
.empty-hint { font-size: 0.85rem; color: var(--kc-text-secondary); }

@media (max-width: 768px) {
  .toolbar { flex-wrap: wrap; width: 100%; }
  .search-wrapper { max-width: none; flex: 1 1 100%; }
}

@media (max-width: 640px) {
  .sub-tabs { overflow-x: auto; white-space: nowrap; }
}
</style>
