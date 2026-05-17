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
  <div class="packrelay-view">
    <header class="page-header">
      <h1 class="page-title">{{ t('packrelay.title') }}</h1>
      <p class="page-subtitle">{{ t('packrelay.subtitle') }}</p>
    </header>

    <div v-if="loading" class="empty-state">
      {{ t('common.loading') }}
    </div>

    <template v-else>
      <!-- Settings -->
      <Card class="settings-card packrelay-card">
        <template #title>{{ t('packrelay.settings.title') }}</template>
        <template #content>
          <div v-if="isConfigured" class="connection-summary">
            <Tag severity="success" :value="t('packrelay.settings.connected')" />
            <span class="connection-meta">
              <span class="meta-label">{{ t('packrelay.settings.publicKeyId') }}:</span>
              <code class="meta-value">{{ status?.publicKeyId }}</code>
            </span>
            <span class="connection-meta">
              <span class="meta-label">{{ t('packrelay.settings.fingerprint') }}:</span>
              <code class="meta-value">{{ fingerprint }}</code>
            </span>
          </div>

          <Message v-else severity="info" :closable="false" class="hint-message">
            {{ t('packrelay.settings.notConfiguredHint') }}
          </Message>

          <div class="form-group">
            <label class="form-label">
              {{ t('packrelay.settings.apiToken') }}
              <span v-if="status?.hasApiToken" class="field-already-set">
                ({{ t('packrelay.settings.alreadySet') }})
              </span>
            </label>
            <Password
              v-model="apiTokenInput"
              :feedback="false"
              toggleMask
              :placeholder="t('packrelay.settings.apiTokenPlaceholder')"
              class="form-input"
              fluid
            />
            <p class="field-hint">{{ t('packrelay.settings.apiTokenHint') }}</p>
          </div>

          <div class="form-group">
            <label class="form-label">
              {{ t('packrelay.settings.signingKey') }}
              <span v-if="status?.hasSigningKey" class="field-already-set">
                ({{ t('packrelay.settings.alreadySet') }})
              </span>
            </label>
            <Password
              v-model="signingKeyInput"
              :feedback="false"
              toggleMask
              :placeholder="t('packrelay.settings.signingKeyPlaceholder')"
              class="form-input"
              fluid
            />
            <p class="field-hint">{{ t('packrelay.settings.signingKeyHint') }}</p>
          </div>

          <div class="form-group">
            <label class="form-label">
              {{ t('packrelay.settings.publicKeyIdLabel') }}
            </label>
            <InputText
              v-model="publicKeyIdInput"
              :placeholder="status?.publicKeyId ?? 'kitsune-den/server-tools'"
              class="form-input"
              fluid
            />
          </div>

          <div class="form-group">
            <label class="form-label">
              {{ t('packrelay.settings.publisherSlugLabel') }}
            </label>
            <InputText
              v-model="publisherSlugInput"
              :placeholder="status?.publisherSlug ?? 'kitsune-den'"
              class="form-input"
              fluid
            />
            <p class="field-hint">{{ t('packrelay.settings.publisherSlugHint') }}</p>
          </div>

          <div class="form-actions">
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
              severity="info"
              @click="saveSettings"
            />
          </div>
        </template>
      </Card>

      <!-- Publish -->
      <Card v-if="modpackState?.modpack" class="settings-card packrelay-card">
        <template #title>{{ t('packrelay.publish.title') }}</template>
        <template #content>
          <div class="publish-meta">
            <div class="publish-meta-row">
              <span class="meta-label">{{ t('packrelay.publish.targetPack') }}:</span>
              <span class="publish-meta-value">{{ modpackState.modpack.name }}</span>
              <code class="meta-value">v{{ modpackState.modpack.version }}</code>
            </div>
            <div class="publish-meta-sub">
              {{ t('packrelay.publish.modCount', { n: modpackState.modList.length }) }}
            </div>
          </div>

          <Message
            v-if="!isConfigured"
            severity="warn"
            :closable="false"
            class="hint-message"
          >
            {{ t('packrelay.publish.notConfigured') }}
          </Message>

          <!-- Idle state -->
          <div v-else-if="!currentJob" class="publish-idle">
            <Button
              :label="t('packrelay.publish.button')"
              :loading="publishStarting"
              icon="pi pi-cloud-upload"
              severity="info"
              @click="publish"
            />
            <p class="field-hint publish-hint">{{ t('packrelay.publish.hint') }}</p>
          </div>

          <!-- Running -->
          <div v-else-if="isPublishing" class="publish-running">
            <div class="publish-phase-row">
              <span class="publish-phase-label">
                {{ t('packrelay.publish.phases.' + (currentJob.latestProgress?.phase ?? 'Walking')) }}
              </span>
              <span class="publish-counter">
                {{ currentJob.latestProgress?.filesDone ?? 0 }}
                /
                {{ currentJob.latestProgress?.filesTotal ?? '?' }}
              </span>
            </div>
            <ProgressBar :value="publishPercent" />
            <p
              v-if="currentJob.latestProgress?.bytesTotal"
              class="publish-bytes"
            >
              {{ formatBytes(currentJob.latestProgress.bytesDone) }} /
              {{ formatBytes(currentJob.latestProgress.bytesTotal) }}
            </p>
            <p
              v-if="currentJob.latestProgress?.currentFile"
              class="publish-current-file"
            >
              {{ currentJob.latestProgress.currentFile }}
            </p>
          </div>

          <!-- Done -->
          <div v-else-if="currentJob.status === 'Done'">
            <Message severity="success" :closable="false" class="hint-message">
              <div class="result-title">
                {{ currentJob.result?.alreadyPublished
                    ? t('packrelay.publish.successAlreadyPublished')
                    : t('packrelay.publish.success') }}
              </div>
              <div class="result-detail">
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
              class="cloud-link"
            >
              {{ t('packrelay.publish.viewOnCloud') }} →
            </a>
            <div class="form-actions">
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
            <Message severity="error" :closable="false" class="hint-message">
              <div class="result-title">{{ t('packrelay.publish.failed') }}</div>
              <div class="result-detail mono">{{ currentJob.errorMessage }}</div>
              <div v-if="currentJob.errorCode" class="result-detail-dim">
                {{ t('packrelay.publish.errorCode') }}: {{ currentJob.errorCode }}
              </div>
            </Message>
            <div class="form-actions">
              <Button
                :label="t('packrelay.publish.retry')"
                size="small"
                severity="info"
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

<style scoped>
/* Match the rest of the panel's spacing + typography. KC's global
 * stylesheet provides `.page-title`, `.settings-card`, `.form-group`,
 * `.form-label`, `.form-input`, and the `--kc-*` color tokens; this
 * scoped block adds the PackRelay-specific bits on top. */
.packrelay-view {
  padding: 1.5rem;
  max-width: 760px;
}

.page-header {
  margin-bottom: 1.5rem;
}

.page-subtitle {
  margin: 0.25rem 0 0;
  color: var(--kc-text-secondary);
  font-size: 0.875rem;
  line-height: 1.5;
}

/* Force visible separation between the two cards. Default Card has
 * no bottom margin and they end up flush against each other. */
.packrelay-card {
  max-width: none;
  margin-bottom: 1.5rem;
}
.packrelay-card:last-child {
  margin-bottom: 0;
}

/* Connection summary row at the top of the settings card */
.connection-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem 1rem;
  margin-bottom: 1.25rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--kc-border);
}

.connection-meta {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.8rem;
  color: var(--kc-text-primary);
}

.meta-label {
  color: var(--kc-text-secondary);
}

.meta-value {
  font-family: ui-monospace, "Cascadia Mono", "Source Code Pro", Menlo, monospace;
  font-size: 0.75rem;
  color: var(--kc-text-primary);
  background: var(--kc-bg-secondary);
  padding: 0.1rem 0.4rem;
  border-radius: 3px;
}

/* Stronger contrast on the field-already-set chip + hints. The
 * default Tailwind text-gray-500 ends up around #6B7280 which gets
 * lost on KC's #0f1419 page bg — too low contrast to read. Pulling
 * to --kc-text-secondary (#9aa0a6) brings it to ~5.4:1 contrast
 * ratio, comfortably above WCAG AA. */
.field-already-set {
  color: var(--kc-cyan);
  font-size: 0.75rem;
  font-weight: 400;
  margin-left: 0.4rem;
}

.field-hint {
  margin: 0.35rem 0 0;
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  line-height: 1.45;
}

.hint-message {
  margin-bottom: 1rem;
}

/* Reset + Save side-by-side at the bottom of the form */
.form-actions {
  display: flex;
  justify-content: space-between;
  gap: 0.75rem;
  margin-top: 1.25rem;
  padding-top: 1rem;
  border-top: 1px solid var(--kc-border);
}

/* Publish card */
.publish-meta {
  margin-bottom: 1.25rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--kc-border);
}

.publish-meta-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.9rem;
  color: var(--kc-text-primary);
}

.publish-meta-value {
  font-weight: 600;
}

.publish-meta-sub {
  margin-top: 0.35rem;
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
}

.publish-idle {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.5rem;
}

.publish-hint {
  margin-top: 0.25rem;
  max-width: 60ch;
}

.publish-running {
  /* Reserves the same vertical space whether we have the bytes/file
   * lines or not, so the success/error cards don't jump when the
   * job transitions out of Running. */
  min-height: 5.5rem;
}

.publish-phase-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 0.5rem;
}

.publish-phase-label {
  font-weight: 500;
  color: var(--kc-text-primary);
}

.publish-counter {
  font-family: ui-monospace, "Cascadia Mono", "Source Code Pro", Menlo, monospace;
  font-size: 0.85rem;
  color: var(--kc-text-secondary);
}

.publish-bytes {
  margin: 0.5rem 0 0;
  font-family: ui-monospace, "Cascadia Mono", "Source Code Pro", Menlo, monospace;
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
}

.publish-current-file {
  margin: 0.25rem 0 0;
  font-family: ui-monospace, "Cascadia Mono", "Source Code Pro", Menlo, monospace;
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* Done / Error result detail */
.result-title {
  font-weight: 600;
}

.result-detail {
  margin-top: 0.35rem;
  font-size: 0.85rem;
}

.result-detail.mono {
  font-family: ui-monospace, "Cascadia Mono", "Source Code Pro", Menlo, monospace;
}

.result-detail-dim {
  margin-top: 0.25rem;
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
}

.cloud-link {
  display: inline-block;
  margin-top: 0.5rem;
  color: var(--kc-cyan);
  text-decoration: none;
  font-size: 0.875rem;
}
.cloud-link:hover {
  text-decoration: underline;
}

.empty-state {
  padding: 3rem 1rem;
  text-align: center;
  color: var(--kc-text-secondary);
}

@media (max-width: 640px) {
  .packrelay-view { padding: 1rem; }
  .form-actions { flex-direction: column-reverse; align-items: stretch; }
  .connection-summary { gap: 0.5rem; }
}
</style>
