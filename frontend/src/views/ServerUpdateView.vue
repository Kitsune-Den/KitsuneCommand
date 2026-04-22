<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import {
  getServerUpdateSettings,
  saveServerUpdateSettings,
  getServerConfigBak,
  saveServerConfigBak,
  type ServerUpdateSettings,
} from '@/api/serverUpdate'
import { restartServer } from '@/api/serverControl'
import Button from 'primevue/button'
import Card from 'primevue/card'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import InputNumber from 'primevue/inputnumber'
import ToggleSwitch from 'primevue/toggleswitch'
import Textarea from 'primevue/textarea'
import Dialog from 'primevue/dialog'

const { t } = useI18n()
const toast = useToast()

const loading = ref(true)
const saving = ref(false)
const savingConfigBak = ref(false)
const restarting = ref(false)
const restartDialogVisible = ref(false)

const settings = ref<ServerUpdateSettings>({
  autoUpdate: false,
  branch: 'public',
  branchPassword: '',
  logRetention: 20,
  steamAppId: 251570,
})

const configBak = ref<string>('')
const configBakLoaded = ref(false)

async function loadAll() {
  loading.value = true
  try {
    settings.value = await getServerUpdateSettings()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('serverUpdate.loadFailed'), life: 4000 })
  }

  try {
    configBak.value = await getServerConfigBak()
    configBakLoaded.value = true
  } catch {
    // 404 means the file doesn't exist yet - that's OK, user can create it
    configBakLoaded.value = false
  }

  loading.value = false
}

async function handleSaveSettings() {
  saving.value = true
  try {
    const msg = await saveServerUpdateSettings(settings.value)
    toast.add({ severity: 'success', summary: t('serverUpdate.saveSuccess'), detail: msg, life: 4000 })
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('serverUpdate.saveFailed'), life: 4000 })
  } finally {
    saving.value = false
  }
}

async function handleSaveConfigBak() {
  if (!configBak.value || !configBak.value.trim()) {
    toast.add({ severity: 'warn', summary: t('common.warning'), detail: t('serverUpdate.configBakEmpty'), life: 4000 })
    return
  }
  savingConfigBak.value = true
  try {
    const msg = await saveServerConfigBak(configBak.value)
    configBakLoaded.value = true
    toast.add({ severity: 'success', summary: t('serverUpdate.saveSuccess'), detail: msg, life: 6000 })
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('serverUpdate.configBakSaveFailed'), life: 4000 })
  } finally {
    savingConfigBak.value = false
  }
}

function showRestartDialog() {
  restartDialogVisible.value = true
}

async function handleRestart() {
  restartDialogVisible.value = false
  restarting.value = true
  try {
    const msg = await restartServer()
    toast.add({ severity: 'warn', summary: t('serverUpdate.updateTriggered'), detail: msg, life: 10000 })
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('serverUpdate.updateFailed'), life: 4000 })
  } finally {
    restarting.value = false
  }
}

onMounted(loadAll)
</script>

<template>
  <div class="server-update-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('serverUpdate.title') }}</h1>
    </div>

    <p class="page-subtitle">{{ t('serverUpdate.subtitle') }}</p>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem" />
      <p>{{ t('common.loading') }}</p>
    </div>

    <template v-else>
      <!-- Update Settings -->
      <Card class="settings-card">
        <template #title>{{ t('serverUpdate.updateSettings') }}</template>
        <template #subtitle>{{ t('serverUpdate.updateSettingsSubtitle') }}</template>
        <template #content>
          <div class="form-grid">
            <div class="toggle-row">
              <label class="form-label">{{ t('serverUpdate.autoUpdate') }}</label>
              <ToggleSwitch v-model="settings.autoUpdate" />
            </div>
            <small class="settings-hint">{{ t('serverUpdate.autoUpdateHint') }}</small>

            <div class="form-group">
              <label class="form-label">{{ t('serverUpdate.branch') }}</label>
              <InputText v-model="settings.branch" placeholder="public" />
              <small class="settings-hint">{{ t('serverUpdate.branchHint') }}</small>
            </div>

            <div class="form-group">
              <label class="form-label">{{ t('serverUpdate.branchPassword') }}</label>
              <Password v-model="settings.branchPassword" :feedback="false" toggle-mask input-class="w-full" />
              <small class="settings-hint">{{ t('serverUpdate.branchPasswordHint') }}</small>
            </div>

            <div class="form-group">
              <label class="form-label">{{ t('serverUpdate.logRetention') }}</label>
              <InputNumber v-model="settings.logRetention" :min="1" :max="500" />
              <small class="settings-hint">{{ t('serverUpdate.logRetentionHint') }}</small>
            </div>

            <div class="form-group">
              <label class="form-label">{{ t('serverUpdate.steamAppId') }}</label>
              <InputNumber v-model="settings.steamAppId" :min="1" :useGrouping="false" />
              <small class="settings-hint">{{ t('serverUpdate.steamAppIdHint') }}</small>
            </div>
          </div>

          <Button
            :label="t('serverUpdate.saveSettings')"
            icon="pi pi-save"
            severity="info"
            :loading="saving"
            class="save-btn"
            @click="handleSaveSettings"
          />
        </template>
      </Card>

      <!-- Sticky Server Config -->
      <Card class="settings-card">
        <template #title>{{ t('serverUpdate.stickyConfig') }}</template>
        <template #subtitle>{{ t('serverUpdate.stickyConfigSubtitle') }}</template>
        <template #content>
          <p class="settings-hint" style="margin-bottom: 0.5rem">
            {{ t('serverUpdate.stickyConfigHint') }}
          </p>
          <Textarea
            v-model="configBak"
            rows="24"
            class="config-bak-textarea"
            spellcheck="false"
            :placeholder="configBakLoaded ? '' : t('serverUpdate.configBakPlaceholder')"
          />
          <div class="action-row">
            <Button
              :label="t('serverUpdate.saveConfigBak')"
              icon="pi pi-save"
              severity="info"
              :loading="savingConfigBak"
              @click="handleSaveConfigBak"
            />
            <Button
              :label="t('common.reload')"
              icon="pi pi-refresh"
              severity="secondary"
              text
              @click="loadAll"
            />
          </div>
        </template>
      </Card>

      <!-- Update Now -->
      <Card class="settings-card action-card">
        <template #title>{{ t('serverUpdate.updateNow') }}</template>
        <template #subtitle>{{ t('serverUpdate.updateNowSubtitle') }}</template>
        <template #content>
          <Button
            :label="t('serverUpdate.updateNow')"
            icon="pi pi-sync"
            severity="warn"
            size="large"
            :loading="restarting"
            @click="showRestartDialog"
          />
          <p class="settings-hint" style="margin-top: 0.75rem">{{ t('serverUpdate.updateNowHint') }}</p>
        </template>
      </Card>
    </template>

    <!-- Restart confirmation dialog -->
    <Dialog v-model:visible="restartDialogVisible" :header="t('serverUpdate.updateConfirm')" modal :style="{ width: '500px' }">
      <div class="restart-dialog">
        <i class="pi pi-sync" style="font-size: 2.5rem; color: #f59e0b" />
        <p class="restart-warning">{{ t('serverUpdate.updateWarning') }}</p>
        <p style="font-size: 0.85rem; color: var(--kc-text-secondary); text-align: center;">{{ t('serverUpdate.updateDialogHint') }}</p>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" severity="secondary" text @click="restartDialogVisible = false" />
        <Button :label="t('serverUpdate.updateNow')" severity="warn" icon="pi pi-sync" @click="handleRestart" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.server-update-view {
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

.page-subtitle {
  color: var(--kc-text-secondary);
  font-size: 0.9rem;
  margin: -0.5rem 0 0 0;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  padding: 3rem;
  color: var(--kc-text-secondary);
}

.settings-card {
  background: var(--kc-bg-secondary);
  border: 1px solid var(--kc-border);
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 1rem;
  margin-bottom: 0.75rem;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.form-label {
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--kc-text);
}

.toggle-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.5rem 0;
}

.settings-hint {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  line-height: 1.4;
  display: block;
}

.save-btn {
  margin-top: 1rem;
}

.config-bak-textarea {
  width: 100%;
  font-family: ui-monospace, 'SF Mono', Menlo, Consolas, monospace;
  font-size: 0.8rem;
}

.action-row {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.75rem;
}

.action-card {
  border-color: #f59e0b;
}

.restart-dialog {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  text-align: center;
}

.restart-warning {
  color: #f59e0b;
  font-weight: 500;
}
</style>
