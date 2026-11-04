<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { usePermissions } from '@/composables/usePermissions'
import { usePlayersStore } from '@/stores/players'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import {
  getStoreGoods,
  getStoreGoodsDetail,
  createStoreGoods,
  updateStoreGoods,
  deleteStoreGoods,
  buyStoreGoods,
  getItemDefinitions,
  getCommandDefinitions,
  createItemDefinition,
  updateItemDefinition,
  deleteItemDefinition,
  createCommandDefinition,
  updateCommandDefinition,
  deleteCommandDefinition,
} from '@/api/store'
import type { GoodsItem, GoodsDetail, ItemDefinition, CommandDefinition } from '@/types'
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
import Checkbox from 'primevue/checkbox'
import AutoComplete from 'primevue/autocomplete'
import { searchGameItems, getGameItemIconUrl } from '@/api/gameItems'
import type { GameItemInfo } from '@/types'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const confirmService = useConfirm()
const playersStore = usePlayersStore()
const { canManageStore, canBuyFromStore } = usePermissions()

// ─── State ──────────────────────────────────────────

const loading = ref(true)
const goodsList = ref<GoodsItem[]>([])
const totalGoods = ref(0)
const pageIndex = ref(0)
const pageSize = ref(50)
const mode = ref<'browse' | 'manage'>('browse')

// Definitions lists (for linking)
const itemDefs = ref<ItemDefinition[]>([])
const cmdDefs = ref<CommandDefinition[]>([])

// Goods CRUD dialog
const showGoodsDialog = ref(false)
const goodsDialogMode = ref<'create' | 'edit'>('create')
const goodsForm = ref({ name: '', price: 0, description: '', itemIds: [] as number[], commandIds: [] as number[] })
const editingGoodsId = ref<number | null>(null)
const goodsSaving = ref(false)

// Buy dialog
const showBuyDialog = ref(false)
const buyTarget = ref<GoodsDetail | null>(null)
const buyPlayerId = ref('')
const buyLoading = ref(false)

// Item Def dialog
const showItemDefDialog = ref(false)
const itemDefMode = ref<'create' | 'edit'>('create')
const itemDefForm = ref({ itemName: '', count: 1, quality: 1, durability: 100, description: '' })
const editingItemDefId = ref<number | null>(null)
const itemDefSaving = ref(false)

// Item name autocomplete
const itemSuggestions = ref<GameItemInfo[]>([])
async function onItemNameSearch(event: { query: string }) {
  try {
    itemSuggestions.value = await searchGameItems(event.query, 20)
  } catch {
    itemSuggestions.value = []
  }
}
function onItemNameSelect(event: { value: GameItemInfo }) {
  itemDefForm.value.itemName = event.value.itemName
}

// Command Def dialog
const showCmdDefDialog = ref(false)
const cmdDefMode = ref<'create' | 'edit'>('create')
const cmdDefForm = ref({ command: '', runInMainThread: false, description: '' })
const editingCmdDefId = ref<number | null>(null)
const cmdDefSaving = ref(false)

// ─── Fetch ──────────────────────────────────────────

async function fetchGoods() {
  loading.value = true
  try {
    const result = await getStoreGoods({ pageIndex: pageIndex.value, pageSize: pageSize.value })
    goodsList.value = result.items
    totalGoods.value = result.total
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('storeView.failedToLoad'), life: 3000 })
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
    // Non-critical
  }
}

// ─── Goods CRUD ─────────────────────────────────────

function openCreateGoods() {
  goodsDialogMode.value = 'create'
  goodsForm.value = { name: '', price: 0, description: '', itemIds: [], commandIds: [] }
  editingGoodsId.value = null
  showGoodsDialog.value = true
}

async function openEditGoods(goods: GoodsItem) {
  try {
    const detail = await getStoreGoodsDetail(goods.id)
    goodsDialogMode.value = 'edit'
    goodsForm.value = {
      name: detail.name,
      price: detail.price,
      description: detail.description || '',
      itemIds: detail.items.map(i => i.id),
      commandIds: detail.commands.map(c => c.id),
    }
    editingGoodsId.value = goods.id
    showGoodsDialog.value = true
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('storeView.failedToLoadDetail'), life: 3000 })
  }
}

async function saveGoods() {
  if (!goodsForm.value.name.trim()) return
  goodsSaving.value = true
  try {
    if (goodsDialogMode.value === 'create') {
      await createStoreGoods(goodsForm.value)
      toast.add({ severity: 'success', summary: t('common.success'), detail: t('storeView.storeItemCreated'), life: 3000 })
    } else {
      await updateStoreGoods(editingGoodsId.value!, goodsForm.value)
      toast.add({ severity: 'success', summary: t('common.success'), detail: t('storeView.storeItemUpdated'), life: 3000 })
    }
    showGoodsDialog.value = false
    fetchGoods()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('storeView.failedToSave'), life: 3000 })
  } finally {
    goodsSaving.value = false
  }
}

function confirmDeleteGoods(goods: GoodsItem) {
  confirmService.require({
    message: t('storeView.confirmDeleteMessage', { name: goods.name }),
    header: t('common.confirmDelete'),
    icon: 'pi pi-trash',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteStoreGoods(goods.id)
        toast.add({ severity: 'success', summary: t('storeView.deleted'), detail: t('storeView.storeItemDeleted'), life: 3000 })
        fetchGoods()
      } catch {
        toast.add({ severity: 'error', summary: t('common.error'), detail: t('storeView.failedToDelete'), life: 3000 })
      }
    },
  })
}

// ─── Buy ────────────────────────────────────────────

async function openBuyDialog(goods: GoodsItem) {
  try {
    const detail = await getStoreGoodsDetail(goods.id)
    buyTarget.value = detail
    buyPlayerId.value = ''
    showBuyDialog.value = true
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('storeView.failedToLoadGoods'), life: 3000 })
  }
}

async function executeBuy() {
  if (!buyTarget.value || !buyPlayerId.value) return
  const player = playersStore.playerList.find(p => p.playerId === buyPlayerId.value)
  if (!player) return

  buyLoading.value = true
  try {
    const result = await buyStoreGoods(buyTarget.value.id, player.playerId, player.playerName)
    toast.add({ severity: 'success', summary: t('storeView.purchased'), detail: result.message, life: 5000 })
    showBuyDialog.value = false
  } catch (err: any) {
    const detail = err.response?.data?.message || t('storeView.purchaseFailed')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 5000 })
  } finally {
    buyLoading.value = false
  }
}

// ─── Item Definitions CRUD ──────────────────────────

function openCreateItemDef() {
  itemDefMode.value = 'create'
  itemDefForm.value = { itemName: '', count: 1, quality: 1, durability: 100, description: '' }
  editingItemDefId.value = null
  showItemDefDialog.value = true
}

function openEditItemDef(item: ItemDefinition) {
  itemDefMode.value = 'edit'
  itemDefForm.value = { itemName: item.itemName, count: item.count, quality: item.quality, durability: item.durability, description: item.description || '' }
  editingItemDefId.value = item.id
  showItemDefDialog.value = true
}

async function saveItemDef() {
  if (!itemDefForm.value.itemName.trim()) return
  itemDefSaving.value = true
  try {
    if (itemDefMode.value === 'create') {
      await createItemDefinition(itemDefForm.value)
    } else {
      await updateItemDefinition(editingItemDefId.value!, itemDefForm.value)
    }
    showItemDefDialog.value = false
    fetchDefinitions()
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('storeView.itemDefSaved'), life: 3000 })
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('storeView.failedToSaveItemDef'), life: 3000 })
  } finally {
    itemDefSaving.value = false
  }
}

function confirmDeleteItemDef(item: ItemDefinition) {
  confirmService.require({
    message: t('storeView.confirmDeleteItemDef', { name: item.itemName }),
    header: t('common.confirmDelete'),
    icon: 'pi pi-trash',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteItemDefinition(item.id)
        fetchDefinitions()
        toast.add({ severity: 'success', summary: t('storeView.deleted'), life: 3000 })
      } catch {
        toast.add({ severity: 'error', summary: t('common.error'), detail: t('storeView.failedToDeleteDef'), life: 3000 })
      }
    },
  })
}

// ─── Command Definitions CRUD ───────────────────────

function openCreateCmdDef() {
  cmdDefMode.value = 'create'
  cmdDefForm.value = { command: '', runInMainThread: false, description: '' }
  editingCmdDefId.value = null
  showCmdDefDialog.value = true
}

function openEditCmdDef(cmd: CommandDefinition) {
  cmdDefMode.value = 'edit'
  cmdDefForm.value = { command: cmd.command, runInMainThread: cmd.runInMainThread, description: cmd.description || '' }
  editingCmdDefId.value = cmd.id
  showCmdDefDialog.value = true
}

async function saveCmdDef() {
  if (!cmdDefForm.value.command.trim()) return
  cmdDefSaving.value = true
  try {
    if (cmdDefMode.value === 'create') {
      await createCommandDefinition(cmdDefForm.value)
    } else {
      await updateCommandDefinition(editingCmdDefId.value!, cmdDefForm.value)
    }
    showCmdDefDialog.value = false
    fetchDefinitions()
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('storeView.cmdDefSaved'), life: 3000 })
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('storeView.failedToSaveCmdDef'), life: 3000 })
  } finally {
    cmdDefSaving.value = false
  }
}

function confirmDeleteCmdDef(cmd: CommandDefinition) {
  confirmService.require({
    message: t('storeView.confirmDeleteCmdDef', { name: cmd.command }),
    header: t('common.confirmDelete'),
    icon: 'pi pi-trash',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteCommandDefinition(cmd.id)
        fetchDefinitions()
        toast.add({ severity: 'success', summary: t('storeView.deleted'), life: 3000 })
      } catch {
        toast.add({ severity: 'error', summary: t('common.error'), detail: t('storeView.failedToDeleteDef'), life: 3000 })
      }
    },
  })
}

// ─── Navigation ─────────────────────────────────────

function navigateTo(tab: string) {
  if (tab === 'points') router.push({ name: 'Points' })
  else if (tab === 'history') router.push({ name: 'PurchaseHistory' })
}

function onPage(event: { first: number; rows: number }) {
  pageIndex.value = Math.floor(event.first / event.rows)
  pageSize.value = event.rows
  fetchGoods()
}

onMounted(() => {
  fetchGoods()
  fetchDefinitions()
})
</script>

<template>
  <div class="store-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('economy.title') }}</h1>
    </div>

    <!-- Sub-tab navigation -->
    <div class="sub-tabs">
      <button class="sub-tab" @click="navigateTo('points')">{{ t('economy.points') }}</button>
      <button class="sub-tab sub-tab--active">{{ t('economy.store') }}</button>
      <button class="sub-tab" @click="navigateTo('history')">{{ t('economy.history') }}</button>
    </div>

    <!-- Mode toggle (admin only) -->
    <div class="toolbar">
      <div class="mode-toggle" v-if="canManageStore">
        <Button
          :label="mode === 'browse' ? t('storeView.browseMode') : t('storeView.manageMode')"
          :icon="mode === 'browse' ? 'pi pi-shopping-cart' : 'pi pi-cog'"
          :severity="mode === 'browse' ? 'info' : 'warn'"
          text
          @click="mode = mode === 'browse' ? 'manage' : 'browse'"
        />
      </div>
      <Button icon="pi pi-refresh" text severity="secondary" @click="fetchGoods" :loading="loading" />
    </div>

    <!-- ─── Browse Mode ──────────────────────────────── -->
    <template v-if="mode === 'browse'">
      <div class="goods-grid" v-if="goodsList.length > 0">
        <div class="goods-card" v-for="goods in goodsList" :key="goods.id">
          <div class="goods-card-header">
            <span class="goods-name">{{ goods.name }}</span>
            <Tag :value="`${goods.price} pts`" severity="info" />
          </div>
          <p class="goods-description" v-if="goods.description">{{ goods.description }}</p>
          <p class="goods-description" v-else style="opacity: 0.5">{{ t('storeView.noDescription') }}</p>
          <div class="goods-card-footer" v-if="canBuyFromStore">
            <Button :label="t('storeView.buyForPlayer')" icon="pi pi-shopping-cart" size="small" severity="success" @click="openBuyDialog(goods)" />
          </div>
        </div>
      </div>
      <div class="empty-state" v-else-if="!loading">
        <i class="pi pi-shopping-cart" style="font-size: 2rem; color: var(--kc-text-secondary)" />
        <p>{{ t('storeView.noItemsYet') }}</p>
      </div>
    </template>

    <!-- ─── Manage Mode (Admin) ──────────────────────── -->
    <template v-if="mode === 'manage' && canManageStore">
      <!-- Goods Table -->
      <div class="section-header">
        <h2 class="section-title">{{ t('storeView.storeItems') }}</h2>
        <Button :label="t('storeView.addItem')" icon="pi pi-plus" size="small" @click="openCreateGoods" />
      </div>

      <DataTable :value="goodsList" :loading="loading" stripedRows :paginator="true" :rows="pageSize" :totalRecords="totalGoods" :lazy="true" @page="onPage">
        <Column field="name" :header="t('storeView.name')" sortable />
        <Column field="price" :header="t('storeView.price')" sortable style="width: 120px">
          <template #body="{ data }">
            <Tag :value="`${data.price} pts`" severity="info" />
          </template>
        </Column>
        <Column field="description" :header="t('common.description')" />
        <Column :header="t('common.actions')" style="width: 140px">
          <template #body="{ data }">
            <div class="action-buttons">
              <Button icon="pi pi-pencil" text severity="info" size="small" @click="openEditGoods(data)" />
              <Button icon="pi pi-trash" text severity="danger" size="small" @click="confirmDeleteGoods(data)" />
            </div>
          </template>
        </Column>
      </DataTable>

      <!-- Item Definitions -->
      <div class="section-header">
        <h2 class="section-title">{{ t('storeView.itemDefinitions') }}</h2>
        <Button :label="t('storeView.addDef')" icon="pi pi-plus" size="small" @click="openCreateItemDef" />
      </div>

      <DataTable :value="itemDefs" stripedRows>
        <Column field="itemName" :header="t('storeView.itemName')" />
        <Column field="count" :header="t('storeView.count')" style="width: 80px" />
        <Column field="quality" :header="t('storeView.qualityCol')" style="width: 80px" />
        <Column field="durability" :header="t('storeView.durability')" style="width: 100px" />
        <Column field="description" :header="t('common.description')" />
        <Column :header="t('common.actions')" style="width: 120px">
          <template #body="{ data }">
            <div class="action-buttons">
              <Button icon="pi pi-pencil" text severity="info" size="small" @click="openEditItemDef(data)" />
              <Button icon="pi pi-trash" text severity="danger" size="small" @click="confirmDeleteItemDef(data)" />
            </div>
          </template>
        </Column>
      </DataTable>

      <!-- Command Definitions -->
      <div class="section-header">
        <h2 class="section-title">{{ t('storeView.commandDefinitions') }}</h2>
        <Button :label="t('storeView.addDef')" icon="pi pi-plus" size="small" @click="openCreateCmdDef" />
      </div>

      <DataTable :value="cmdDefs" stripedRows>
        <Column field="command" :header="t('storeView.command')">
          <template #body="{ data }">
            <code class="cmd-text">{{ data.command }}</code>
          </template>
        </Column>
        <Column field="runInMainThread" :header="t('storeView.mainThread')" style="width: 120px">
          <template #body="{ data }">
            <Tag :value="data.runInMainThread ? t('common.yes') : t('common.no')" :severity="data.runInMainThread ? 'warn' : 'secondary'" />
          </template>
        </Column>
        <Column field="description" :header="t('common.description')" />
        <Column :header="t('common.actions')" style="width: 120px">
          <template #body="{ data }">
            <div class="action-buttons">
              <Button icon="pi pi-pencil" text severity="info" size="small" @click="openEditCmdDef(data)" />
              <Button icon="pi pi-trash" text severity="danger" size="small" @click="confirmDeleteCmdDef(data)" />
            </div>
          </template>
        </Column>
      </DataTable>
    </template>

    <!-- ─── Goods Dialog ─────────────────────────────── -->
    <Dialog v-model:visible="showGoodsDialog" :header="goodsDialogMode === 'create' ? t('storeView.newStoreItem') : t('storeView.editStoreItem')" :modal="true" :style="{ width: '500px' }">
      <div class="dialog-form">
        <div class="form-field">
          <label>{{ t('storeView.nameLabel') }}</label>
          <InputText v-model="goodsForm.name" class="w-full" />
        </div>
        <div class="form-field">
          <label>{{ t('storeView.priceLabel') }}</label>
          <InputNumber v-model="goodsForm.price" :min="0" class="w-full" />
        </div>
        <div class="form-field">
          <label>{{ t('common.description') }}</label>
          <Textarea v-model="goodsForm.description" rows="2" class="w-full" />
        </div>
        <div class="form-field">
          <label>{{ t('storeView.linkedItems') }}</label>
          <MultiSelect v-model="goodsForm.itemIds" :options="itemDefs" optionLabel="itemName" optionValue="id" :placeholder="t('storeView.selectItems')" class="w-full" />
        </div>
        <div class="form-field">
          <label>{{ t('storeView.linkedCommands') }}</label>
          <MultiSelect v-model="goodsForm.commandIds" :options="cmdDefs" optionLabel="command" optionValue="id" :placeholder="t('storeView.selectCommands')" class="w-full" />
        </div>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" text severity="secondary" @click="showGoodsDialog = false" />
        <Button :label="goodsDialogMode === 'create' ? t('common.create') : t('common.save')" @click="saveGoods" :loading="goodsSaving" :disabled="!goodsForm.name.trim()" />
      </template>
    </Dialog>

    <!-- ─── Buy Dialog ───────────────────────────────── -->
    <Dialog v-model:visible="showBuyDialog" :header="t('storeView.purchaseItem')" :modal="true" :style="{ width: '450px' }">
      <div class="dialog-form" v-if="buyTarget">
        <p><strong>{{ buyTarget.name }}</strong> — {{ buyTarget.effectivePrice ?? buyTarget.price }} points</p>
        <p class="buy-detail" v-if="buyTarget.description">{{ buyTarget.description }}</p>

        <div v-if="buyTarget.items.length > 0" class="buy-section">
          <strong>{{ t('storeView.items') }}</strong>
          <ul>
            <li v-for="item in buyTarget.items" :key="item.id">{{ item.itemName }} x{{ item.count }} (Q{{ item.quality }})</li>
          </ul>
        </div>
        <div v-if="buyTarget.commands.length > 0" class="buy-section">
          <strong>{{ t('storeView.commands') }}</strong>
          <ul>
            <li v-for="cmd in buyTarget.commands" :key="cmd.id"><code>{{ cmd.command }}</code></li>
          </ul>
        </div>

        <div class="form-field">
          <label>{{ t('storeView.selectPlayerOnline') }}</label>
          <Select
            v-model="buyPlayerId"
            :options="playersStore.playerList"
            optionLabel="playerName"
            optionValue="playerId"
            :placeholder="t('storeView.selectPlayer')"
            class="w-full"
          />
        </div>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" text severity="secondary" @click="showBuyDialog = false" />
        <Button :label="t('storeView.confirmPurchase')" severity="success" icon="pi pi-check" @click="executeBuy" :loading="buyLoading" :disabled="!buyPlayerId" />
      </template>
    </Dialog>

    <!-- ─── Item Definition Dialog ───────────────────── -->
    <Dialog v-model:visible="showItemDefDialog" :header="itemDefMode === 'create' ? t('storeView.newItemDefinition') : t('storeView.editItemDefinition')" :modal="true" :style="{ width: '450px' }">
      <div class="dialog-form">
        <div class="form-field">
          <label>{{ t('storeView.itemNameLabel') }}</label>
          <AutoComplete
            v-model="itemDefForm.itemName"
            :suggestions="itemSuggestions"
            @complete="onItemNameSearch"
            @item-select="onItemNameSelect"
            optionLabel="displayName"
            class="w-full"
            :placeholder="t('storeView.itemNamePlaceholder')"
          >
            <template #option="{ option }">
              <div class="autocomplete-option">
                <img
                  v-if="option.iconName"
                  :src="getGameItemIconUrl(option.iconName, 24)"
                  :alt="option.iconName"
                  class="autocomplete-icon"
                  @error="($event.target as HTMLImageElement).style.display = 'none'"
                />
                <div class="autocomplete-text">
                  <span class="autocomplete-display-name">{{ option.displayName }}</span>
                  <span class="autocomplete-internal-name">{{ option.itemName }}</span>
                </div>
              </div>
            </template>
          </AutoComplete>
        </div>
        <div class="form-row">
          <div class="form-field">
            <label>{{ t('storeView.countLabel') }}</label>
            <InputNumber v-model="itemDefForm.count" :min="1" />
          </div>
          <div class="form-field">
            <label>{{ t('storeView.qualityLabel') }}</label>
            <InputNumber v-model="itemDefForm.quality" :min="1" :max="6" />
          </div>
          <div class="form-field">
            <label>{{ t('storeView.durabilityLabel') }}</label>
            <InputNumber v-model="itemDefForm.durability" :min="1" />
          </div>
        </div>
        <div class="form-field">
          <label>{{ t('common.description') }}</label>
          <Textarea v-model="itemDefForm.description" rows="2" class="w-full" />
        </div>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" text severity="secondary" @click="showItemDefDialog = false" />
        <Button :label="itemDefMode === 'create' ? t('common.create') : t('common.save')" @click="saveItemDef" :loading="itemDefSaving" :disabled="!itemDefForm.itemName.trim()" />
      </template>
    </Dialog>

    <!-- ─── Command Definition Dialog ────────────────── -->
    <Dialog v-model:visible="showCmdDefDialog" :header="cmdDefMode === 'create' ? t('storeView.newCommandDefinition') : t('storeView.editCommandDefinition')" :modal="true" :style="{ width: '450px' }">
      <div class="dialog-form">
        <div class="form-field">
          <label>{{ t('storeView.commandLabel') }}</label>
          <InputText v-model="cmdDefForm.command" class="w-full" :placeholder="t('storeView.commandPlaceholder')" />
        </div>
        <div class="form-field checkbox-field">
          <Checkbox v-model="cmdDefForm.runInMainThread" :binary="true" inputId="mainThread" />
          <label for="mainThread">{{ t('storeView.runOnMainThread') }}</label>
        </div>
        <div class="form-field">
          <label>{{ t('common.description') }}</label>
          <Textarea v-model="cmdDefForm.description" rows="2" class="w-full" />
        </div>
        <p class="hint-text">
          {{ t('storeView.availablePlaceholders') }}
        </p>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" text severity="secondary" @click="showCmdDefDialog = false" />
        <Button :label="cmdDefMode === 'create' ? t('common.create') : t('common.save')" @click="saveCmdDef" :loading="cmdDefSaving" :disabled="!cmdDefForm.command.trim()" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.store-view { display: flex; flex-direction: column; gap: 1rem; }
.page-header { display: flex; align-items: center; gap: 1rem; }
.page-title { font-size: 1.5rem; font-weight: 600; }

.sub-tabs { display: flex; gap: 0.25rem; border-bottom: 1px solid var(--kc-border); }
.sub-tab { padding: 0.5rem 1rem; border: none; background: none; color: var(--kc-text-secondary); cursor: pointer; border-bottom: 2px solid transparent; font-size: 0.9rem; transition: all 0.15s ease; }
.sub-tab:hover { color: var(--kc-text-primary); }
.sub-tab--active { color: var(--kc-cyan); border-bottom-color: var(--kc-cyan); }

.toolbar { display: flex; align-items: center; gap: 0.5rem; }
.mode-toggle { flex: 1; }

.goods-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 1rem; }
.goods-card {
  background: var(--kc-bg-secondary); border: 1px solid var(--kc-border);
  border-radius: 8px; padding: 1rem; display: flex; flex-direction: column; gap: 0.5rem;
}
.goods-card-header { display: flex; justify-content: space-between; align-items: center; }
.goods-name { font-weight: 600; font-size: 1rem; }
.goods-description { font-size: 0.85rem; color: var(--kc-text-secondary); margin: 0; flex: 1; }
.goods-card-footer { margin-top: 0.5rem; }

.section-header { display: flex; align-items: center; justify-content: space-between; margin-top: 1rem; }
.section-title { font-size: 1.1rem; font-weight: 600; }

.dialog-form { display: flex; flex-direction: column; gap: 1rem; }
.form-field { display: flex; flex-direction: column; gap: 0.25rem; }
.form-field label { font-size: 0.85rem; color: var(--kc-text-secondary); }
.form-row { display: flex; gap: 1rem; }
.checkbox-field { flex-direction: row; align-items: center; gap: 0.5rem; }
.w-full { width: 100%; }

.action-buttons { display: flex; gap: 0.25rem; }
.cmd-text { font-size: 0.85rem; background: rgba(0,0,0,0.2); padding: 0.15rem 0.4rem; border-radius: 4px; }

.buy-detail { font-size: 0.85rem; color: var(--kc-text-secondary); margin: 0; }
.buy-section { font-size: 0.85rem; }
.buy-section ul { margin: 0.25rem 0; padding-left: 1.5rem; }
.hint-text { font-size: 0.8rem; color: var(--kc-text-secondary); margin: 0; }
.hint-text code { background: rgba(0,0,0,0.2); padding: 0.1rem 0.3rem; border-radius: 3px; }

.autocomplete-option { display: flex; align-items: center; gap: 0.5rem; }
.autocomplete-icon { width: 24px; height: 24px; object-fit: contain; flex-shrink: 0; }
.autocomplete-text { display: flex; flex-direction: column; }
.autocomplete-display-name { font-size: 0.85rem; font-weight: 500; }
.autocomplete-internal-name { font-size: 0.7rem; color: var(--kc-text-secondary); }

.empty-state { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 2rem; color: var(--kc-text-secondary); }

@media (max-width: 768px) {
  .toolbar { flex-wrap: wrap; width: 100%; }
  .form-row { flex-direction: column; }
}

@media (max-width: 640px) {
  .sub-tabs { overflow-x: auto; white-space: nowrap; }
  .goods-grid { grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); }
}
</style>
