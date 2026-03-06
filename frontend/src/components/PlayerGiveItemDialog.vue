<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { giveItem } from '@/api/players'
import { searchGameItems, getGameItemIconUrl } from '@/api/gameItems'
import { useToast } from 'primevue/usetoast'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import AutoComplete from 'primevue/autocomplete'
import InputNumber from 'primevue/inputnumber'
import type { PlayerInfo, GameItemInfo } from '@/types'

const { t } = useI18n()
const toast = useToast()

const props = defineProps<{
  player: PlayerInfo | null
}>()

const visible = defineModel<boolean>('visible', { default: false })

const itemName = ref('')
const count = ref(1)
const quality = ref(1)
const giving = ref(false)

// Autocomplete
const itemSuggestions = ref<GameItemInfo[]>([])

async function onItemSearch(event: { query: string }) {
  try {
    itemSuggestions.value = await searchGameItems(event.query, 20)
  } catch {
    itemSuggestions.value = []
  }
}

function onItemSelect(event: { value: GameItemInfo }) {
  itemName.value = event.value.itemName
}

watch(visible, (v) => {
  if (v) {
    itemName.value = ''
    count.value = 1
    quality.value = 1
    itemSuggestions.value = []
  }
})

async function give() {
  const name = typeof itemName.value === 'string' ? itemName.value.trim() : (itemName.value as any)?.itemName ?? ''
  if (!props.player || !name) return
  giving.value = true
  try {
    await giveItem(props.player.entityId, name, count.value, quality.value)
    toast.add({ severity: 'success', summary: t('players.itemGiven', { name: props.player.playerName }), life: 3000 })
    visible.value = false
  } catch (err: any) {
    const detail = err?.response?.data?.message || t('players.itemGiveFailed')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 5000 })
  } finally {
    giving.value = false
  }
}

function resolvedItemName(): string {
  if (typeof itemName.value === 'string') return itemName.value.trim()
  return (itemName.value as any)?.itemName ?? ''
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    :header="t('players.giveItemTitle', { name: player?.playerName ?? '' })"
    modal
    :style="{ width: '450px' }"
    :closable="true"
  >
    <div class="give-form">
      <div class="field">
        <label>{{ t('players.itemName') }}</label>
        <AutoComplete
          v-model="itemName"
          :suggestions="itemSuggestions"
          @complete="onItemSearch"
          @item-select="onItemSelect"
          optionLabel="displayName"
          class="full-width"
          :placeholder="t('players.itemNamePlaceholder')"
        >
          <template #option="{ option }">
            <div class="ac-option">
              <img
                v-if="option.iconName"
                :src="getGameItemIconUrl(option.iconName, 24)"
                :alt="option.iconName"
                class="ac-icon"
                @error="($event.target as HTMLImageElement).style.display = 'none'"
              />
              <div class="ac-text">
                <span class="ac-display">{{ option.displayName }}</span>
                <span class="ac-internal">{{ option.itemName }}</span>
              </div>
            </div>
          </template>
        </AutoComplete>
      </div>
      <div class="field-row">
        <div class="field">
          <label>{{ t('players.quantity') }}</label>
          <InputNumber v-model="count" :min="1" :max="10000" showButtons />
        </div>
        <div class="field">
          <label>{{ t('players.quality') }}</label>
          <InputNumber v-model="quality" :min="1" :max="6" showButtons />
        </div>
      </div>
    </div>
    <template #footer>
      <Button :label="t('common.cancel')" text severity="secondary" @click="visible = false" />
      <Button
        icon="pi pi-box"
        :label="t('players.giveItem')"
        severity="info"
        @click="give"
        :loading="giving"
        :disabled="!resolvedItemName()"
      />
    </template>
  </Dialog>
</template>

<style scoped>
.give-form { display: flex; flex-direction: column; gap: 1rem; }
.field { display: flex; flex-direction: column; gap: 0.35rem; }
.field label { font-size: 0.85rem; font-weight: 600; color: var(--kc-text-secondary); }
.field-row { display: flex; gap: 1rem; }
.full-width { width: 100%; }
.ac-option { display: flex; align-items: center; gap: 0.5rem; }
.ac-icon { width: 24px; height: 24px; object-fit: contain; flex-shrink: 0; }
.ac-text { display: flex; flex-direction: column; }
.ac-display { font-size: 0.85rem; font-weight: 500; }
.ac-internal { font-size: 0.7rem; color: var(--kc-text-secondary); }
</style>
