<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { sendChatMessage } from '@/api/chat'
import { useToast } from 'primevue/usetoast'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import Textarea from 'primevue/textarea'
import type { PlayerInfo } from '@/types'

const { t } = useI18n()
const toast = useToast()

const props = defineProps<{
  player: PlayerInfo | null
}>()

const visible = defineModel<boolean>('visible', { default: false })

const message = ref('')
const sending = ref(false)

watch(visible, (v) => {
  if (v) message.value = ''
})

async function send() {
  if (!props.player || !message.value.trim()) return
  sending.value = true
  try {
    await sendChatMessage(message.value.trim(), props.player.entityId.toString())
    toast.add({ severity: 'success', summary: t('players.pmSent', { name: props.player.playerName }), life: 3000 })
    visible.value = false
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('players.pmFailed'), life: 3000 })
  } finally {
    sending.value = false
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    :header="t('players.pmTitle', { name: player?.playerName ?? '' })"
    modal
    :style="{ width: '420px' }"
    :closable="true"
  >
    <Textarea
      v-model="message"
      :placeholder="t('players.pmPlaceholder')"
      rows="4"
      class="pm-input"
      @keydown.ctrl.enter="send"
    />
    <template #footer>
      <Button :label="t('common.cancel')" text severity="secondary" @click="visible = false" />
      <Button
        icon="pi pi-send"
        :label="t('players.sendPm')"
        severity="info"
        @click="send"
        :loading="sending"
        :disabled="!message.trim()"
      />
    </template>
  </Dialog>
</template>

<style scoped>
.pm-input { width: 100%; }
</style>
