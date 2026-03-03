<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { usePermissions } from '@/composables/usePermissions'
import { usePlayersStore } from '@/stores/players'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import {
  getCdKeys,
  getCdKeyDetail,
  createCdKey,
  updateCdKey,
  deleteCdKey,
  redeemCdKey,
} from '@/api/cdkeys'
import { getItemDefinitions, getCommandDefinitions } from '@/api/store'
import type { CdKey, ItemDefinition, CommandDefinition } from '@/types'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Textarea from 'primevue/textarea'
import Dialog from 'primevue/dialog'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import MultiSelect from 'primevue/multiselect'
import DatePicker from 'primevue/datepicker'

const router = useRouter()
const toast = useToast()
const confirmService = useConfirm()
const playersStore = usePlayersStore()
const { canManageCdKeys, canRedeemCdKeys } = usePermissions()

// State
const loading = ref(true)
const cdKeys = ref<CdKey[]>([])
const totalKeys = ref(0)
const pageIndex = ref(0)
const pageSize = ref(50)
const searchFilter = ref('')
let searchTimeout: ReturnType<typeof setTimeout> | null = null

// Definitions for linking
const itemDefs = ref<ItemDefinition[]>([])
const cmdDefs = ref<CommandDefinition[]>([])

// Key CRUD dialog
const showKeyDialog = ref(false)
const keyDialogMode = ref<'create' | 'edit'>('create')
const keyForm = ref({
  key: '',
  maxRedeemCount: 1,
  expiryAt: null as Date | null,
  description: '',
  itemIds: [] as number[],
  commandIds: [] as number[],
})
const editingKeyId = ref<number | null>(null)
const keyDialogLoading = ref(false)

// Redeem dialog
const showRedeemDialog = ref(false)
const redeemKey = ref<CdKey | null>(null)
const redeemPlayerId = ref('')
const redeemLoading = ref(false)

// Redeem counts cache
const redeemCounts = ref<Record<number, number>>({})

async function fetchKeys() {
  loading.value = true
  try {
    const result = await getCdKeys({
      pageIndex: pageIndex.value,
      pageSize: pageSize.value,
      search: searchFilter.value || undefined,
    })
    cdKeys.value = result.items
    totalKeys.value = result.total

    // Fetch redeem counts for each key
    for (const key of result.items) {
      try {
        const detail = await getCdKeyDetail(key.id)
        redeemCounts.value[key.id] = detail.currentRedeemCount
      } catch {
        // ignore
      }
    }
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load CD keys', life: 3000 })
  } finally {
    loading.value = false
  }
}

async function fetchDefinitions() {
  try {
    const [items, cmds] = await Promise.all([getItemDefinitions(), getCommandDefinitions()])
    itemDefs.value = items
    cmdDefs.value = cmds
  } catch {
    // non-critical
  }
}

function onPage(event: { first: number; rows: number }) {
  pageIndex.value = Math.floor(event.first / event.rows)
  pageSize.value = event.rows
  fetchKeys()
}

function onSearch() {
  if (searchTimeout) clearTimeout(searchTimeout)
  searchTimeout = setTimeout(() => {
    pageIndex.value = 0
    fetchKeys()
  }, 400)
}

// ─── Key CRUD ───────────────────────────────────────

function openCreateDialog() {
  keyDialogMode.value = 'create'
  keyForm.value = { key: '', maxRedeemCount: 1, expiryAt: null, description: '', itemIds: [], commandIds: [] }
  editingKeyId.value = null
  showKeyDialog.value = true
}

async function openEditDialog(cdKey: CdKey) {
  keyDialogMode.value = 'edit'
  editingKeyId.value = cdKey.id

  try {
    const detail = await getCdKeyDetail(cdKey.id)
    keyForm.value = {
      key: detail.key,
      maxRedeemCount: detail.maxRedeemCount,
      expiryAt: detail.expiryAt ? new Date(detail.expiryAt + 'Z') : null,
      description: detail.description || '',
      itemIds: detail.items.map((i) => i.id),
      commandIds: detail.commands.map((c) => c.id),
    }
    showKeyDialog.value = true
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load key details', life: 3000 })
  }
}

async function saveKey() {
  if (!keyForm.value.key.trim()) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Key code is required', life: 3000 })
    return
  }

  keyDialogLoading.value = true
  try {
    const data = {
      key: keyForm.value.key.trim(),
      maxRedeemCount: keyForm.value.maxRedeemCount,
      expiryAt: keyForm.value.expiryAt ? keyForm.value.expiryAt.toISOString().replace('Z', '') : undefined,
      description: keyForm.value.description.trim() || undefined,
      itemIds: keyForm.value.itemIds,
      commandIds: keyForm.value.commandIds,
    }

    if (keyDialogMode.value === 'create') {
      await createCdKey(data)
      toast.add({ severity: 'success', summary: 'Created', detail: `CD key "${data.key}" created`, life: 3000 })
    } else {
      await updateCdKey(editingKeyId.value!, { ...data, maxRedeemCount: data.maxRedeemCount })
      toast.add({ severity: 'success', summary: 'Updated', detail: `CD key "${data.key}" updated`, life: 3000 })
    }

    showKeyDialog.value = false
    fetchKeys()
  } catch (err: any) {
    const msg = err?.response?.data?.message || 'Failed to save CD key'
    toast.add({ severity: 'error', summary: 'Error', detail: msg, life: 3000 })
  } finally {
    keyDialogLoading.value = false
  }
}

function confirmDeleteKey(cdKey: CdKey) {
  confirmService.require({
    message: `Delete CD key "${cdKey.key}"?`,
    header: 'Confirm Delete',
    icon: 'pi pi-trash',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteCdKey(cdKey.id)
        toast.add({ severity: 'success', summary: 'Deleted', detail: `CD key "${cdKey.key}" deleted`, life: 3000 })
        fetchKeys()
      } catch {
        toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to delete CD key', life: 3000 })
      }
    },
  })
}

// ─── Redemption ─────────────────────────────────────

function openRedeemDialog(cdKey: CdKey) {
  redeemKey.value = cdKey
  redeemPlayerId.value = ''
  showRedeemDialog.value = true
}

async function executeRedeem() {
  if (!redeemPlayerId.value || !redeemKey.value) return

  redeemLoading.value = true
  try {
    const player = playersStore.playerList.find((p) => p.playerId === redeemPlayerId.value)
    const result = await redeemCdKey(redeemKey.value.id, redeemPlayerId.value, player?.playerName)
    toast.add({ severity: 'success', summary: 'Redeemed', detail: result.message, life: 4000 })
    showRedeemDialog.value = false
    fetchKeys() // refresh counts
  } catch (err: any) {
    const msg = err?.response?.data?.message || 'Failed to redeem CD key'
    toast.add({ severity: 'error', summary: 'Error', detail: msg, life: 4000 })
  } finally {
    redeemLoading.value = false
  }
}

function navigateTo(tab: string) {
  if (tab === 'redemptions') router.push({ name: 'CdKeyRedemptions' })
}

function isExpired(expiryAt: string | null): boolean {
  if (!expiryAt) return false
  return new Date() > new Date(expiryAt + 'Z')
}

function formatDate(dateStr: string | null): string {
  if (!dateStr) return 'No Expiry'
  const d = new Date(dateStr + 'Z')
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

const onlinePlayers = () => playersStore.playerList.filter((p) => p.isOnline)

onMounted(() => {
  fetchKeys()
  fetchDefinitions()
})
</script>

<template>
  <div class="cdkeys-view">
    <div class="page-header">
      <h1 class="page-title">CD Keys</h1>
    </div>

    <!-- Sub-tab navigation -->
    <div class="sub-tabs">
      <button class="sub-tab sub-tab--active">Keys</button>
      <button class="sub-tab" @click="navigateTo('redemptions')">Redemptions</button>
    </div>

    <div class="toolbar">
      <span class="p-input-icon-left search-wrapper">
        <i class="pi pi-search" />
        <InputText v-model="searchFilter" placeholder="Search by key or description..." class="search-input" @input="onSearch" />
      </span>
      <Button icon="pi pi-refresh" text severity="secondary" @click="fetchKeys" :loading="loading" />
      <div class="toolbar-spacer" />
      <Button
        v-if="canManageCdKeys"
        label="Add CD Key"
        icon="pi pi-plus"
        severity="info"
        size="small"
        @click="openCreateDialog"
      />
    </div>

    <DataTable
      :value="cdKeys"
      :loading="loading"
      stripedRows
      :paginator="true"
      :rows="pageSize"
      :totalRecords="totalKeys"
      :lazy="true"
      :rowsPerPageOptions="[25, 50, 100]"
      @page="onPage"
    >
      <Column field="key" header="Key Code">
        <template #body="{ data }">
          <code class="key-text">{{ data.key }}</code>
        </template>
      </Column>

      <Column header="Redeemed" style="width: 130px">
        <template #body="{ data }">
          <span>{{ redeemCounts[data.id] ?? '...' }} / {{ data.maxRedeemCount }}</span>
        </template>
      </Column>

      <Column field="expiryAt" header="Expiry" style="width: 200px">
        <template #body="{ data }">
          <Tag v-if="isExpired(data.expiryAt)" value="Expired" severity="danger" />
          <span v-else-if="data.expiryAt" class="date-text">{{ formatDate(data.expiryAt) }}</span>
          <span v-else class="empty-text">No Expiry</span>
        </template>
      </Column>

      <Column field="description" header="Description">
        <template #body="{ data }">
          <span class="desc-text">{{ data.description || '—' }}</span>
        </template>
      </Column>

      <Column header="Actions" style="width: 160px">
        <template #body="{ data }">
          <div class="action-buttons">
            <Button
              v-if="canRedeemCdKeys"
              icon="pi pi-gift"
              text
              severity="info"
              size="small"
              @click="openRedeemDialog(data)"
              title="Redeem for Player"
            />
            <Button
              v-if="canManageCdKeys"
              icon="pi pi-pencil"
              text
              severity="secondary"
              size="small"
              @click="openEditDialog(data)"
              title="Edit"
            />
            <Button
              v-if="canManageCdKeys"
              icon="pi pi-trash"
              text
              severity="danger"
              size="small"
              @click="confirmDeleteKey(data)"
              title="Delete"
            />
          </div>
        </template>
      </Column>

      <template #empty>
        <div class="empty-state">
          <i class="pi pi-key" style="font-size: 2rem; color: var(--kc-text-secondary)" />
          <p>No CD keys yet</p>
          <span class="empty-hint">Create redeemable codes that deliver items and run commands for players.</span>
        </div>
      </template>
    </DataTable>

    <!-- Key CRUD Dialog -->
    <Dialog
      v-model:visible="showKeyDialog"
      :header="keyDialogMode === 'create' ? 'Create CD Key' : 'Edit CD Key'"
      :modal="true"
      :style="{ width: '550px' }"
    >
      <div class="form-grid">
        <div class="form-field">
          <label>Key Code *</label>
          <InputText v-model="keyForm.key" placeholder="e.g., WELCOME2025" class="w-full" />
        </div>

        <div class="form-row">
          <div class="form-field">
            <label>Max Redemptions</label>
            <InputNumber v-model="keyForm.maxRedeemCount" :min="1" :max="999999" showButtons class="w-full" />
          </div>
          <div class="form-field">
            <label>Expiry Date <small>(optional)</small></label>
            <DatePicker v-model="keyForm.expiryAt" showTime hourFormat="24" showIcon class="w-full" placeholder="No expiry" />
          </div>
        </div>

        <div class="form-field">
          <label>Description <small>(optional)</small></label>
          <Textarea v-model="keyForm.description" rows="2" class="w-full" placeholder="e.g., Welcome pack for new players" />
        </div>

        <div class="form-field">
          <label>Linked Items</label>
          <MultiSelect
            v-model="keyForm.itemIds"
            :options="itemDefs"
            optionLabel="itemName"
            optionValue="id"
            placeholder="Select items..."
            class="w-full"
            filter
            display="chip"
          />
        </div>

        <div class="form-field">
          <label>Linked Commands</label>
          <MultiSelect
            v-model="keyForm.commandIds"
            :options="cmdDefs"
            optionLabel="command"
            optionValue="id"
            placeholder="Select commands..."
            class="w-full"
            filter
            display="chip"
          />
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="showKeyDialog = false" />
        <Button
          :label="keyDialogMode === 'create' ? 'Create' : 'Save'"
          severity="info"
          @click="saveKey"
          :loading="keyDialogLoading"
        />
      </template>
    </Dialog>

    <!-- Redeem Dialog -->
    <Dialog
      v-model:visible="showRedeemDialog"
      header="Redeem CD Key"
      :modal="true"
      :style="{ width: '400px' }"
    >
      <div class="redeem-form" v-if="redeemKey">
        <p class="redeem-info">
          Key: <code class="key-text">{{ redeemKey.key }}</code>
          <br />
          Redeemed: {{ redeemCounts[redeemKey.id] ?? '...' }} / {{ redeemKey.maxRedeemCount }}
          <br />
          <span v-if="redeemKey.expiryAt">
            Expiry:
            <Tag v-if="isExpired(redeemKey.expiryAt)" value="Expired" severity="danger" />
            <span v-else>{{ formatDate(redeemKey.expiryAt) }}</span>
          </span>
          <span v-else>Expiry: <span class="empty-text">None</span></span>
        </p>

        <div class="form-field">
          <label>Select Player (online)</label>
          <Select
            v-model="redeemPlayerId"
            :options="onlinePlayers()"
            optionLabel="playerName"
            optionValue="playerId"
            placeholder="Choose a player..."
            class="w-full"
            filter
          />
        </div>
      </div>

      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="showRedeemDialog = false" />
        <Button
          label="Redeem"
          icon="pi pi-gift"
          severity="info"
          @click="executeRedeem"
          :loading="redeemLoading"
          :disabled="!redeemPlayerId"
        />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.cdkeys-view { display: flex; flex-direction: column; gap: 1rem; }
.page-header { display: flex; align-items: center; gap: 1rem; }
.page-title { font-size: 1.5rem; font-weight: 600; }

.sub-tabs { display: flex; gap: 0.25rem; border-bottom: 1px solid var(--kc-border); }
.sub-tab { padding: 0.5rem 1rem; border: none; background: none; color: var(--kc-text-secondary); cursor: pointer; border-bottom: 2px solid transparent; font-size: 0.9rem; transition: all 0.15s ease; }
.sub-tab:hover { color: var(--kc-text-primary); }
.sub-tab--active { color: var(--kc-cyan); border-bottom-color: var(--kc-cyan); }

.toolbar { display: flex; align-items: center; gap: 0.5rem; }
.toolbar-spacer { flex: 1; }
.search-wrapper { flex: 0 1 350px; }
.search-input { width: 100%; }

.action-buttons { display: flex; gap: 0.25rem; }
.key-text { font-size: 0.9rem; color: var(--kc-cyan); background: rgba(0, 212, 255, 0.08); padding: 0.15rem 0.5rem; border-radius: 4px; font-family: monospace; }
.date-text { font-size: 0.85rem; color: var(--kc-text-secondary); }
.desc-text { font-size: 0.85rem; color: var(--kc-text-secondary); }
.empty-text { color: var(--kc-text-secondary); }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 2rem; color: var(--kc-text-secondary); }
.empty-hint { font-size: 0.85rem; color: var(--kc-text-secondary); }

.form-grid { display: flex; flex-direction: column; gap: 1rem; }
.form-row { display: flex; gap: 1rem; }
.form-row .form-field { flex: 1; }
.form-field { display: flex; flex-direction: column; gap: 0.25rem; }
.form-field label { font-size: 0.85rem; color: var(--kc-text-secondary); }
.form-field small { opacity: 0.7; }
.w-full { width: 100%; }

.redeem-form { display: flex; flex-direction: column; gap: 1rem; }
.redeem-info { margin: 0; line-height: 1.8; }

@media (max-width: 768px) {
  .toolbar { flex-wrap: wrap; width: 100%; }
  .search-wrapper { flex: 1 1 100%; }
  .form-row { flex-direction: column; }
}

@media (max-width: 640px) {
  .sub-tabs { overflow-x: auto; white-space: nowrap; }
}
</style>
