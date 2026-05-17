<script setup lang="ts">
/**
 * PackRelay publish UI. Two stacked panels:
 *
 *   1. Settings — paste API token + signing key + publicKeyId +
 *      publisherSlug once; encrypted at rest on the panel. Status
 *      chip + fingerprint shows the configured state without ever
 *      surfacing the plaintext.
 *
 *   2. Publish  — visible only when settings are configured AND a
 *      modpack exists. Big "Publish to PackRelay" button kicks off
 *      a job; we poll every 1s while it's running and render the
 *      orchestrator's phase + per-file progress live.
 *
 * Polling is intentional vs SSE/WebSocket — see
 * PackRelayPublishJobTracker for the design rationale. Cleared the
 * moment status flips out of "Running".
 */
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Message from 'primevue/message'
import ProgressBar from 'primevue/progressbar'
import Tag from 'primevue/tag'
import Card from 'primevue/card'
import {
  getPackRelaySettings,
  savePackRelaySettings,
  resetPackRelaySettings,
  startPublishToPackRelay,
  getPublishJob,
  type PackRelayStatus,
  type PublishJobSnapshot,
} from '@/api/packrelay'
import { getModpackState } from '@/api/modpack'
import type { ModpackState } from '@/types'

const { t } = useI18n()
const toast = useToast()

const status = ref<PackRelayStatus | null>(null)
const modpackState = ref<ModpackState | null>(null)
const loading = ref(true)

// Settings form state. All four fields are optional on save; pasting
// only one (e.g. rotating just the API token) is supported.
const apiTokenInput = ref('')
const signingKeyInput = ref('')
const publicKeyIdInput = ref('')
const publisherSlugInput = ref('')
const savingSettings = ref(false)

// Publish state.
const currentJob = ref<PublishJobSnapshot | null>(null)
const publishStarting = ref(false)
let pollHandle: number | null = null

const isConfigured = computed(
  () =>
    status.value?.hasApiToken &&
    status.value?.hasSigningKey &&
    !!status.value?.publisherSlug
)

const fingerprint = computed(() => {
  if (!status.value?.signingKeyPublic) return null
  // Show the leading 12 base64 chars of the public key — enough to
  // visually compare against what packrelay.cloud /account/keys
  // shows without dumping the whole 44-char string.
  return status.value.signingKeyPublic.slice(0, 12) + '…'
})

const isPublishing = computed(() => currentJob.value?.status === 'Running')

const publishPercent = computed(() => {
  const p = currentJob.value?.latestProgress
  if (!p || p.bytesTotal === 0) return 0
  return Math.min(100, Math.round((p.bytesDone / p.bytesTotal) * 100))
})

// ─── Lifecycle ────────────────────────────────────────────────────────

onMounted(async () => {
  await Promise.all([loadStatus(), loadModpack()])
  loading.value = false
})

onUnmounted(() => stopPolling())

async function loadStatus() {
  try {
    status.value = await getPackRelaySettings()
  } catch {
    toast.add({
      severity: 'error',
      summary: t('common.error'),
      detail: t('packrelay.errors.loadStatus'),
      life: 4000,
    })
  }
}

async function loadModpack() {
  try {
    modpackState.value = await getModpackState()
  } catch {
    // Non-fatal — the publish panel just stays hidden.
  }
}

// ─── Settings ────────────────────────────────────────────────────────

async function saveSettings() {
  savingSettings.value = true
  try {
    const payload: Record<string, string> = {}
    if (apiTokenInput.value.trim()) payload.apiToken = apiTokenInput.value.trim()
    if (signingKeyInput.value.trim())
      payload.signingKeyBase64 = signingKeyInput.value.trim()
    if (publicKeyIdInput.value.trim())
      payload.publicKeyId = publicKeyIdInput.value.trim()
    if (publisherSlugInput.value.trim())
      payload.publisherSlug = publisherSlugInput.value.trim()
    if (Object.keys(payload).length === 0) {
      toast.add({
        severity: 'warn',
        summary: t('packrelay.settings.nothingToSave.summary'),
        detail: t('packrelay.settings.nothingToSave.detail'),
        life: 3500,
      })
      return
    }
    status.value = await savePackRelaySettings(payload)
    // Wipe the input fields so a screenshot of the page doesn't
    // contain the token. Status still shows the redacted form.
    apiTokenInput.value = ''
    signingKeyInput.value = ''
    toast.add({
      severity: 'success',
      summary: t('common.saved'),
      detail: t('packrelay.settings.saved'),
      life: 3000,
    })
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: t('common.error'),
      detail: extractError(err, t('packrelay.errors.saveStatus')),
      life: 5000,
    })
  } finally {
    savingSettings.value = false
  }
}

async function reset() {
  if (!confirm(t('packrelay.settings.resetConfirm'))) return
  try {
    await resetPackRelaySettings()
    await loadStatus()
    toast.add({
      severity: 'success',
      summary: t('common.done'),
      detail: t('packrelay.settings.resetDone'),
      life: 3000,
    })
  } catch {
    toast.add({
      severity: 'error',
      summary: t('common.error'),
      detail: t('packrelay.errors.reset'),
      life: 4000,
    })
  }
}

// ─── Publish ─────────────────────────────────────────────────────────

async function publish() {
  if (!modpackState.value?.modpack) return
  publishStarting.value = true
  try {
    const startResp = await startPublishToPackRelay(modpackState.value.modpack.id)
    // Seed currentJob with a Running placeholder so the UI flips
    // from "Publish" button to progress bar immediately, before the
    // first poll lands.
    currentJob.value = {
      jobId: startResp.jobId,
      modpackId: startResp.modpackId,
      status: 'Running',
      latestProgress: null,
      result: null,
      errorMessage: null,
      errorCode: null,
      startedAtUtc: new Date().toISOString(),
      updatedAtUtc: new Date().toISOString(),
    }
    startPolling(startResp.jobId)
  } catch (err: unknown) {
    toast.add({
      severity: 'error',
      summary: t('common.error'),
      detail: extractError(err, t('packrelay.errors.publishStart')),
      life: 5000,
    })
  } finally {
    publishStarting.value = false
  }
}

function startPolling(jobId: string) {
  stopPolling()
  // 1s cadence — the orchestrator emits an event per file uploaded,
  // so a 1s GET captures the latest snapshot without drowning the
  // panel in requests on a 100-file pack.
  pollHandle = window.setInterval(async () => {
    try {
      const snap = await getPublishJob(jobId)
      currentJob.value = snap
      if (snap.status !== 'Running') {
        stopPolling()
        if (snap.status === 'Done') {
          await loadStatus() // publisherSlug may have just been set
        }
      }
    } catch {
      // Single poll failure isn't fatal; keep polling. The job may
      // have just expired (30-min TTL); we'll see that via a fresh
      // call.
    }
  }, 1000)
}

function stopPolling() {
  if (pollHandle != null) {
    window.clearInterval(pollHandle)
    pollHandle = null
  }
}

function dismissJob() {
  currentJob.value = null
}

// ─── Helpers ─────────────────────────────────────────────────────────

function extractError(err: unknown, fallback: string): string {
  if (typeof err === 'object' && err && 'response' in err) {
    // axios error shape
    const resp = (err as { response?: { data?: { message?: string } } }).response
    if (resp?.data?.message) return resp.data.message
  }
  if (err instanceof Error) return err.message
  return fallback
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  if (n < 1024 * 1024 * 1024) return `${(n / (1024 * 1024)).toFixed(1)} MB`
  return `${(n / (1024 * 1024 * 1024)).toFixed(2)} GB`
}
</script>

<template>
  <div class="p-4 max-w-4xl mx-auto">
    <header class="mb-6">
      <h1 class="text-2xl font-semibold mb-1">{{ t('packrelay.title') }}</h1>
      <p class="text-sm text-gray-500 dark:text-gray-400">
        {{ t('packrelay.subtitle') }}
      </p>
    </header>

    <div v-if="loading" class="text-center py-12 text-gray-500">
      {{ t('common.loading') }}
    </div>

    <template v-else>
      <!-- Settings -->
      <Card class="mb-6">
        <template #title>{{ t('packrelay.settings.title') }}</template>
        <template #content>
          <div v-if="isConfigured" class="mb-4 flex items-center gap-3">
            <Tag severity="success" :value="t('packrelay.settings.connected')" />
            <span class="text-sm text-gray-600 dark:text-gray-400">
              {{ t('packrelay.settings.publicKeyId') }}:
              <code class="font-mono text-xs">{{ status?.publicKeyId }}</code>
            </span>
            <span class="text-sm text-gray-600 dark:text-gray-400">
              {{ t('packrelay.settings.fingerprint') }}:
              <code class="font-mono text-xs">{{ fingerprint }}</code>
            </span>
          </div>

          <Message v-else severity="info" :closable="false" class="mb-4">
            {{ t('packrelay.settings.notConfiguredHint') }}
          </Message>

          <div class="grid grid-cols-1 gap-4">
            <div>
              <label class="block text-sm font-medium mb-1">
                {{ t('packrelay.settings.apiToken') }}
                <span v-if="status?.hasApiToken" class="text-xs text-green-600">
                  ({{ t('packrelay.settings.alreadySet') }})
                </span>
              </label>
              <Password
                v-model="apiTokenInput"
                :feedback="false"
                toggleMask
                :placeholder="t('packrelay.settings.apiTokenPlaceholder')"
                fluid
              />
              <p class="text-xs text-gray-500 mt-1">
                {{ t('packrelay.settings.apiTokenHint') }}
              </p>
            </div>

            <div>
              <label class="block text-sm font-medium mb-1">
                {{ t('packrelay.settings.signingKey') }}
                <span v-if="status?.hasSigningKey" class="text-xs text-green-600">
                  ({{ t('packrelay.settings.alreadySet') }})
                </span>
              </label>
              <Password
                v-model="signingKeyInput"
                :feedback="false"
                toggleMask
                :placeholder="t('packrelay.settings.signingKeyPlaceholder')"
                fluid
              />
              <p class="text-xs text-gray-500 mt-1">
                {{ t('packrelay.settings.signingKeyHint') }}
              </p>
            </div>

            <div>
              <label class="block text-sm font-medium mb-1">
                {{ t('packrelay.settings.publicKeyIdLabel') }}
              </label>
              <InputText
                v-model="publicKeyIdInput"
                :placeholder="status?.publicKeyId ?? 'kitsune-den/server-tools'"
                fluid
              />
            </div>

            <div>
              <label class="block text-sm font-medium mb-1">
                {{ t('packrelay.settings.publisherSlugLabel') }}
              </label>
              <InputText
                v-model="publisherSlugInput"
                :placeholder="status?.publisherSlug ?? 'kitsune-den'"
                fluid
              />
              <p class="text-xs text-gray-500 mt-1">
                {{ t('packrelay.settings.publisherSlugHint') }}
              </p>
            </div>
          </div>

          <div class="mt-4 flex justify-between">
            <Button
              :label="t('packrelay.settings.reset')"
              severity="secondary"
              outlined
              size="small"
              @click="reset"
            />
            <Button
              :label="t('common.save')"
              :loading="savingSettings"
              @click="saveSettings"
            />
          </div>
        </template>
      </Card>

      <!-- Publish -->
      <Card v-if="modpackState?.modpack" class="mb-6">
        <template #title>{{ t('packrelay.publish.title') }}</template>
        <template #content>
          <div class="mb-4 text-sm">
            <div>
              <strong>{{ t('packrelay.publish.targetPack') }}:</strong>
              {{ modpackState.modpack.name }}
              <code class="font-mono text-xs ml-1">v{{ modpackState.modpack.version }}</code>
            </div>
            <div class="text-gray-500 dark:text-gray-400 mt-1">
              {{ t('packrelay.publish.modCount', { n: modpackState.modList.length }) }}
            </div>
          </div>

          <Message
            v-if="!isConfigured"
            severity="warn"
            :closable="false"
          >
            {{ t('packrelay.publish.notConfigured') }}
          </Message>

          <!-- Idle state: big Publish button -->
          <div v-else-if="!currentJob">
            <Button
              :label="t('packrelay.publish.button')"
              :loading="publishStarting"
              size="large"
              @click="publish"
            />
            <p class="text-xs text-gray-500 mt-2">
              {{ t('packrelay.publish.hint') }}
            </p>
          </div>

          <!-- Running -->
          <div v-else-if="isPublishing">
            <div class="mb-2 flex items-center justify-between">
              <span class="text-sm font-medium">
                {{ t('packrelay.publish.phases.' + (currentJob.latestProgress?.phase ?? 'Walking')) }}
              </span>
              <span class="text-sm text-gray-500 font-mono">
                {{ currentJob.latestProgress?.filesDone ?? 0 }}
                /
                {{ currentJob.latestProgress?.filesTotal ?? '?' }}
              </span>
            </div>
            <ProgressBar :value="publishPercent" />
            <p
              v-if="currentJob.latestProgress?.bytesTotal"
              class="text-xs text-gray-500 mt-2 font-mono"
            >
              {{ formatBytes(currentJob.latestProgress.bytesDone) }} /
              {{ formatBytes(currentJob.latestProgress.bytesTotal) }}
            </p>
            <p
              v-if="currentJob.latestProgress?.currentFile"
              class="text-xs text-gray-500 mt-1 font-mono truncate"
            >
              {{ currentJob.latestProgress.currentFile }}
            </p>
          </div>

          <!-- Done -->
          <div v-else-if="currentJob.status === 'Done'">
            <Message severity="success" :closable="false" class="mb-3">
              <div class="font-medium">
                {{ currentJob.result?.alreadyPublished
                    ? t('packrelay.publish.successAlreadyPublished')
                    : t('packrelay.publish.success') }}
              </div>
              <div class="text-xs mt-1">
                {{ currentJob.result?.slug }} v{{ currentJob.result?.version }}
                · {{ currentJob.result?.fileCount }} {{ t('packrelay.publish.files') }}
                · {{ formatBytes(currentJob.result?.totalSize ?? 0) }}
              </div>
            </Message>
            <a
              v-if="currentJob.result?.slug"
              :href="`https://packrelay.cloud/packs/${currentJob.result.slug}`"
              target="_blank"
              rel="noopener noreferrer"
              class="text-sm text-blue-600 dark:text-blue-400 hover:underline"
            >
              {{ t('packrelay.publish.viewOnCloud') }} →
            </a>
            <div class="mt-4">
              <Button
                :label="t('common.dismiss')"
                severity="secondary"
                outlined
                size="small"
                @click="dismissJob"
              />
            </div>
          </div>

          <!-- Error -->
          <div v-else-if="currentJob.status === 'Error'">
            <Message severity="error" :closable="false" class="mb-3">
              <div class="font-medium">{{ t('packrelay.publish.failed') }}</div>
              <div class="text-xs mt-1 font-mono">
                {{ currentJob.errorMessage }}
              </div>
              <div v-if="currentJob.errorCode" class="text-xs mt-1 text-gray-500">
                {{ t('packrelay.publish.errorCode') }}: {{ currentJob.errorCode }}
              </div>
            </Message>
            <div class="flex gap-2">
              <Button
                :label="t('packrelay.publish.retry')"
                size="small"
                @click="() => { dismissJob(); publish(); }"
              />
              <Button
                :label="t('common.dismiss')"
                severity="secondary"
                outlined
                size="small"
                @click="dismissJob"
              />
            </div>
          </div>
        </template>
      </Card>

      <Message v-else severity="info" :closable="false">
        {{ t('packrelay.publish.noModpack') }}
      </Message>
    </template>
  </div>
</template>
