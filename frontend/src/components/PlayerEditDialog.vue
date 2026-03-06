<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { usePlayersStore } from '@/stores/players'
import { updatePlayerMetadata, setAdminLevel } from '@/api/players'
import { useToast } from 'primevue/usetoast'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import ColorPicker from 'primevue/colorpicker'
import type { PlayerInfo } from '@/types'

const { t } = useI18n()
const toast = useToast()
const playersStore = usePlayersStore()

const props = defineProps<{
  player: PlayerInfo | null
}>()

const visible = defineModel<boolean>('visible', { default: false })
const emit = defineEmits<{ saved: [] }>()

const saving = ref(false)
const nameColor = ref<string | null>(null)
const customTag = ref('')
const adminLevel = ref(1000)
const notes = ref('')

const tagPresets = [
  { label: 'VIP', value: 'VIP' },
  { label: 'Supporter', value: 'Supporter' },
  { label: 'Moderator', value: 'Moderator' },
  { label: 'Builder', value: 'Builder' },
]

const adminLevelOptions = [
  { label: t('players.adminLevelAdmin'), value: 0 },
  { label: t('players.adminLevelModerator'), value: 1 },
  { label: t('players.adminLevelNormal'), value: 1000 },
]

// Load current metadata when dialog opens
watch(visible, (v) => {
  if (v && props.player) {
    const meta = playersStore.getMetadata(props.player.playerId)
    nameColor.value = meta?.nameColor ?? null
    customTag.value = meta?.customTag ?? ''
    adminLevel.value = props.player.adminLevel ?? 1000
    notes.value = meta?.notes ?? ''
  }
})

async function save() {
  if (!props.player) return
  saving.value = true

  try {
    // Save metadata
    await updatePlayerMetadata(props.player.playerId, {
      nameColor: nameColor.value || null,
      customTag: customTag.value || null,
      notes: notes.value || null,
    })

    // Change admin level if needed
    const currentIsAdmin = props.player.isAdmin
    const wantsAdmin = adminLevel.value < 1000
    if (currentIsAdmin !== wantsAdmin || (wantsAdmin && currentIsAdmin)) {
      try {
        await setAdminLevel(props.player.entityId, adminLevel.value)
        toast.add({ severity: 'success', summary: t('players.adminLevelChanged', { name: props.player.playerName }), life: 3000 })
      } catch {
        toast.add({ severity: 'error', summary: t('common.error'), detail: t('players.adminLevelFailed'), life: 3000 })
      }
    }

    toast.add({ severity: 'success', summary: t('players.metadataSaved'), life: 3000 })
    emit('saved')
    visible.value = false
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('players.metadataFailed'), life: 3000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    :header="t('players.editPlayer') + (player ? ` — ${player.playerName}` : '')"
    modal
    :style="{ width: '480px' }"
    :closable="true"
  >
    <div class="edit-form" v-if="player">
      <!-- Name Color -->
      <div class="field">
        <label>{{ t('players.nameColor') }}</label>
        <div class="color-row">
          <ColorPicker v-model="nameColor" />
          <span v-if="nameColor" class="color-preview" :style="{ color: `#${nameColor}` }">
            {{ player.playerName }}
          </span>
          <Button
            v-if="nameColor"
            icon="pi pi-times"
            text
            severity="secondary"
            size="small"
            @click="nameColor = null"
          />
        </div>
      </div>

      <!-- Custom Tag -->
      <div class="field">
        <label>{{ t('players.customTag') }}</label>
        <div class="tag-row">
          <InputText
            v-model="customTag"
            :placeholder="t('players.tagPlaceholder')"
            class="tag-input"
          />
          <Select
            :modelValue="null"
            :options="tagPresets"
            optionLabel="label"
            optionValue="value"
            placeholder="Presets"
            class="tag-presets"
            @update:modelValue="(v: string) => customTag = v"
          />
        </div>
      </div>

      <!-- Admin Level -->
      <div class="field">
        <label>{{ t('players.adminLevel') }}</label>
        <Select
          v-model="adminLevel"
          :options="adminLevelOptions"
          optionLabel="label"
          optionValue="value"
          class="full-width"
        />
      </div>

      <!-- Notes -->
      <div class="field">
        <label>{{ t('players.notes') }}</label>
        <Textarea
          v-model="notes"
          :placeholder="t('players.notesPlaceholder')"
          rows="3"
          class="full-width"
        />
      </div>
    </div>

    <template #footer>
      <Button :label="t('common.cancel')" text severity="secondary" @click="visible = false" />
      <Button :label="t('players.saveMetadata')" severity="info" @click="save" :loading="saving" />
    </template>
  </Dialog>
</template>

<style scoped>
.edit-form { display: flex; flex-direction: column; gap: 1rem; }
.field { display: flex; flex-direction: column; gap: 0.35rem; }
.field label { font-size: 0.85rem; font-weight: 600; color: var(--kc-text-secondary); }
.color-row { display: flex; align-items: center; gap: 0.75rem; }
.color-preview { font-weight: 600; font-size: 1rem; }
.tag-row { display: flex; gap: 0.5rem; }
.tag-input { flex: 1; }
.tag-presets { width: 130px; }
.full-width { width: 100%; }
</style>
