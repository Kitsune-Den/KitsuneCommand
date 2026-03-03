<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useEconomyStore } from '@/stores/economy'
import { usePermissions } from '@/composables/usePermissions'
import { getPointsList, adjustPoints } from '@/api/points'
import { useToast } from 'primevue/usetoast'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Dialog from 'primevue/dialog'
import Textarea from 'primevue/textarea'
import Tag from 'primevue/tag'
import type { PointsInfo } from '@/api/points'

const router = useRouter()
const toast = useToast()
const economyStore = useEconomyStore()
const { canAdjustPoints } = usePermissions()

const loading = ref(true)
const searchFilter = ref('')
const pageIndex = ref(0)
const pageSize = ref(50)
const totalRecords = ref(0)
let searchTimeout: ReturnType<typeof setTimeout> | null = null

// Adjust dialog
const showAdjustDialog = ref(false)
const adjustTarget = ref<PointsInfo | null>(null)
const adjustAmount = ref(0)
const adjustReason = ref('')
const adjustLoading = ref(false)

const activeTab = ref('points')

async function fetchPoints() {
  loading.value = true
  try {
    const result = await getPointsList({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      search: searchFilter.value || undefined,
    })
    economyStore.setPointsList(result.items, result.total)
    totalRecords.value = result.total
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load points', life: 3000 })
  } finally {
    loading.value = false
  }
}

function onSearch() {
  if (searchTimeout) clearTimeout(searchTimeout)
  searchTimeout = setTimeout(() => {
    pageIndex.value = 0
    fetchPoints()
  }, 400)
}

function onPage(event: { first: number; rows: number }) {
  pageIndex.value = Math.floor(event.first / event.rows)
  pageSize.value = event.rows
  fetchPoints()
}

function openAdjustDialog(player: PointsInfo) {
  adjustTarget.value = player
  adjustAmount.value = 0
  adjustReason.value = ''
  showAdjustDialog.value = true
}

async function submitAdjust() {
  if (!adjustTarget.value || adjustAmount.value === 0) return

  adjustLoading.value = true
  try {
    await adjustPoints(adjustTarget.value.id, adjustAmount.value, adjustReason.value || undefined)
    toast.add({
      severity: 'success',
      summary: 'Points Adjusted',
      detail: `${adjustAmount.value > 0 ? '+' : ''}${adjustAmount.value} points for ${adjustTarget.value.playerName}`,
      life: 3000,
    })
    showAdjustDialog.value = false
    fetchPoints()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to adjust points', life: 3000 })
  } finally {
    adjustLoading.value = false
  }
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return 'Never'
  const d = new Date(dateStr + 'Z')
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function navigateTo(tab: string) {
  if (tab === 'store') router.push({ name: 'Store' })
  else if (tab === 'history') router.push({ name: 'PurchaseHistory' })
}

watch(searchFilter, onSearch)
onMounted(fetchPoints)
</script>

<template>
  <div class="points-view">
    <div class="page-header">
      <h1 class="page-title">Economy</h1>
    </div>

    <!-- Sub-tab navigation -->
    <div class="sub-tabs">
      <button class="sub-tab sub-tab--active" @click="activeTab = 'points'">Points</button>
      <button class="sub-tab" @click="navigateTo('store')">Store</button>
      <button class="sub-tab" @click="navigateTo('history')">History</button>
    </div>

    <div class="toolbar">
      <span class="p-input-icon-left search-wrapper">
        <i class="pi pi-search" />
        <InputText v-model="searchFilter" placeholder="Search by player name..." class="search-input" />
      </span>
      <Button icon="pi pi-refresh" text severity="secondary" @click="fetchPoints" :loading="loading" />
    </div>

    <DataTable
      :value="economyStore.pointsList"
      :loading="loading"
      stripedRows
      :paginator="true"
      :rows="pageSize"
      :totalRecords="totalRecords"
      :lazy="true"
      :rowsPerPageOptions="[25, 50, 100]"
      @page="onPage"
      sortField="points"
      :sortOrder="-1"
      class="points-table"
    >
      <Column field="playerName" header="Player" sortable>
        <template #body="{ data }">
          <div class="player-name">
            <i class="pi pi-user" />
            <span>{{ data.playerName || data.id }}</span>
          </div>
        </template>
      </Column>

      <Column field="points" header="Points" sortable style="width: 150px">
        <template #body="{ data }">
          <Tag :value="data.points.toLocaleString()" severity="info" class="points-tag" />
        </template>
      </Column>

      <Column field="lastSignInAt" header="Last Sign-In" style="width: 200px">
        <template #body="{ data }">
          <span class="date-text">{{ formatDate(data.lastSignInAt) }}</span>
        </template>
      </Column>

      <Column v-if="canAdjustPoints" header="Actions" style="width: 100px">
        <template #body="{ data }">
          <div class="action-buttons">
            <Button
              icon="pi pi-plus-minus"
              text
              severity="info"
              size="small"
              @click="openAdjustDialog(data)"
              title="Adjust Points"
            />
          </div>
        </template>
      </Column>

      <template #empty>
        <div class="empty-state">
          <i class="pi pi-wallet" style="font-size: 2rem; color: var(--kc-text-secondary)" />
          <p>No points data yet</p>
          <span class="empty-hint">Players will appear here once they log in to the server.</span>
        </div>
      </template>
    </DataTable>

    <!-- Adjust Points Dialog -->
    <Dialog
      v-model:visible="showAdjustDialog"
      header="Adjust Points"
      :modal="true"
      :style="{ width: '400px' }"
    >
      <div class="adjust-form" v-if="adjustTarget">
        <p class="adjust-player">
          Player: <strong>{{ adjustTarget.playerName }}</strong>
          <br />
          Current: <Tag :value="adjustTarget.points.toLocaleString()" severity="info" />
        </p>

        <div class="form-field">
          <label>Amount (positive to add, negative to deduct)</label>
          <InputNumber v-model="adjustAmount" showButtons :min="-999999" :max="999999" />
        </div>

        <div class="form-field">
          <label>Reason (optional)</label>
          <Textarea v-model="adjustReason" rows="2" class="w-full" placeholder="e.g., Event reward, Admin correction" />
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="showAdjustDialog = false" />
        <Button
          label="Apply"
          severity="info"
          @click="submitAdjust"
          :loading="adjustLoading"
          :disabled="adjustAmount === 0"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.points-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 600;
}

.sub-tabs {
  display: flex;
  gap: 0.25rem;
  border-bottom: 1px solid var(--kc-border);
  padding-bottom: 0;
}

.sub-tab {
  padding: 0.5rem 1rem;
  border: none;
  background: none;
  color: var(--kc-text-secondary);
  cursor: pointer;
  border-bottom: 2px solid transparent;
  font-size: 0.9rem;
  transition: all 0.15s ease;
}

.sub-tab:hover {
  color: var(--kc-text-primary);
}

.sub-tab--active {
  color: var(--kc-cyan);
  border-bottom-color: var(--kc-cyan);
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.search-wrapper {
  flex: 1;
  max-width: 350px;
}

.search-input {
  width: 100%;
}

.player-name {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.points-tag {
  font-weight: 600;
}

.date-text {
  font-size: 0.85rem;
  color: var(--kc-text-secondary);
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

.empty-hint {
  font-size: 0.85rem;
  color: var(--kc-text-secondary);
}

.adjust-form {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.adjust-player {
  margin: 0;
  line-height: 1.6;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.form-field label {
  font-size: 0.85rem;
  color: var(--kc-text-secondary);
}

.w-full {
  width: 100%;
}

@media (max-width: 768px) {
  .toolbar { flex-wrap: wrap; width: 100%; }
  .search-wrapper { max-width: none; flex: 1 1 100%; }
}

@media (max-width: 640px) {
  .sub-tabs { overflow-x: auto; white-space: nowrap; }
}
</style>
