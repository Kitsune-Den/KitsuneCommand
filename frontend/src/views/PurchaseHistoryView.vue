<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { getPurchaseHistory } from '@/api/store'
import type { PurchaseRecord } from '@/types'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'

const router = useRouter()
const toast = useToast()

const loading = ref(true)
const records = ref<PurchaseRecord[]>([])
const totalRecords = ref(0)
const pageIndex = ref(0)
const pageSize = ref(50)
const playerFilter = ref('')
let filterTimeout: ReturnType<typeof setTimeout> | null = null

async function fetchHistory() {
  loading.value = true
  try {
    const result = await getPurchaseHistory({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      playerId: playerFilter.value || undefined,
    })
    records.value = result.items
    totalRecords.value = result.total
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load purchase history', life: 3000 })
  } finally {
    loading.value = false
  }
}

function onPage(event: { first: number; rows: number }) {
  pageIndex.value = Math.floor(event.first / event.rows)
  pageSize.value = event.rows
  fetchHistory()
}

function onFilterChange() {
  if (filterTimeout) clearTimeout(filterTimeout)
  filterTimeout = setTimeout(() => {
    pageIndex.value = 0
    fetchHistory()
  }, 400)
}

function formatDate(dateStr: string): string {
  const d = new Date(dateStr + 'Z')
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function navigateTo(tab: string) {
  if (tab === 'points') router.push({ name: 'Points' })
  else if (tab === 'store') router.push({ name: 'Store' })
}

watch(playerFilter, onFilterChange)
onMounted(fetchHistory)
</script>

<template>
  <div class="history-view">
    <div class="page-header">
      <h1 class="page-title">Economy</h1>
    </div>

    <!-- Sub-tab navigation -->
    <div class="sub-tabs">
      <button class="sub-tab" @click="navigateTo('points')">Points</button>
      <button class="sub-tab" @click="navigateTo('store')">Store</button>
      <button class="sub-tab sub-tab--active">History</button>
    </div>

    <div class="toolbar">
      <span class="p-input-icon-left search-wrapper">
        <i class="pi pi-search" />
        <InputText v-model="playerFilter" placeholder="Filter by player ID..." class="search-input" />
      </span>
      <Button icon="pi pi-refresh" text severity="secondary" @click="fetchHistory" :loading="loading" />
    </div>

    <DataTable
      :value="records"
      :loading="loading"
      stripedRows
      :paginator="true"
      :rows="pageSize"
      :totalRecords="totalRecords"
      :lazy="true"
      :rowsPerPageOptions="[25, 50, 100]"
      @page="onPage"
    >
      <Column field="createdAt" header="Date" style="width: 180px">
        <template #body="{ data }">
          <span class="date-text">{{ formatDate(data.createdAt) }}</span>
        </template>
      </Column>

      <Column field="playerName" header="Player">
        <template #body="{ data }">
          <div class="player-name">
            <i class="pi pi-user" />
            <span>{{ data.playerName }}</span>
          </div>
        </template>
      </Column>

      <Column field="goodsName" header="Item" />

      <Column field="price" header="Price" style="width: 120px">
        <template #body="{ data }">
          <Tag :value="`${data.price} pts`" severity="warn" />
        </template>
      </Column>

      <template #empty>
        <div class="empty-state">
          <i class="pi pi-history" style="font-size: 2rem; color: var(--kc-text-secondary)" />
          <p>No purchase history yet</p>
        </div>
      </template>
    </DataTable>
  </div>
</template>

<style scoped>
.history-view { display: flex; flex-direction: column; gap: 1rem; }
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
.date-text { font-size: 0.85rem; color: var(--kc-text-secondary); }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 2rem; color: var(--kc-text-secondary); }

@media (max-width: 768px) {
  .toolbar { flex-wrap: wrap; width: 100%; }
  .search-wrapper { max-width: none; flex: 1 1 100%; }
}

@media (max-width: 640px) {
  .sub-tabs { overflow-x: auto; white-space: nowrap; }
}
</style>
