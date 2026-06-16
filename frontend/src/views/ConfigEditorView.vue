<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { getConfig, getRawXml, saveConfig, saveRawXml, getWorlds, migrateConfigTo30, type ConfigFieldGroup } from '@/api/config'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import ToggleSwitch from 'primevue/toggleswitch'
import Textarea from 'primevue/textarea'
import Dialog from 'primevue/dialog'
import Message from 'primevue/message'
import DayNightCycleWidget from '@/components/DayNightCycleWidget.vue'

/**
 * camelCase / PascalCase → human-readable title case.
 * Used as a fallback when no i18n translation exists for a given field key.
 * Handles acronyms ("EAC" → "EAC") and common 7D2D prefixes like "AI".
 */
function formatFieldLabel(key: string): string {
  return key
    // Insert space between camelCase transitions (e.g. "dayNight" → "day Night")
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    // Preserve acronym boundaries (e.g. "XPMultiplier" → "XP Multiplier")
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    // Title-case each word, leaving all-caps acronyms alone
    .replace(/\b[a-z]/g, (c) => c.toUpperCase())
}

/** Keys handled by the DayNightCycleWidget — skip rendering them individually. */
const DAY_NIGHT_KEYS = ['DayNightLength', 'DayLightLength']

/** The 7D2D 3.0 Sandbox code field — shown only on 3.0, hidden on 2.x. */
const SANDBOX_CODE_KEY = 'SandboxCode'

const { t } = useI18n()
const toast = useToast()

const loading = ref(true)
const saving = ref(false)
const activeTab = ref<'form' | 'raw'>('form')
const properties = ref<Record<string, string>>({})
const originalProperties = ref<Record<string, string>>({})
const groups = ref<ConfigFieldGroup[]>([])
const configPath = ref('')
const rawXml = ref('')
const rawXmlOriginal = ref('')
const worlds = ref<string[]>([])
const confirmDialogVisible = ref(false)
const passwordVisible = ref<Record<string, boolean>>({})

const isDirty = computed(() => {
  if (activeTab.value === 'raw') {
    return rawXml.value !== rawXmlOriginal.value
  }
  return JSON.stringify(properties.value) !== JSON.stringify(originalProperties.value)
})

/**
 * True when the running game is 7D2D 3.0+ (supports the SandboxCode system) — set from the
 * backend's GameSupportsSandboxCode(). Controls whether the SandboxCode field is shown: it
 * must appear on ANY 3.0 server, even before a code has been pasted in (otherwise you could
 * never add one — chicken-and-egg).
 */
const is30 = ref(false)

/** True when a 3.0 server's serverconfig.xml still carries the old sandbox-governed
 *  properties — i.e. a one-click "Migrate to 3.0" would do something. */
const needsMigration = ref(false)
const migrating = ref(false)

/**
 * True when a non-empty SandboxCode is actually set. Only then does it override the
 * individual sandbox-governed settings, so only then are those hidden. With an empty/absent
 * code, 3.0 still reads the individual serverconfig properties (backward-compat), so they
 * stay visible and editable.
 */
const hasSandboxCode = computed(() => {
  const k = Object.keys(properties.value).find(k => k.toLowerCase() === SANDBOX_CODE_KEY.toLowerCase())
  return !!(k && properties.value[k] && properties.value[k].trim())
})

const coreGroup = computed(() => groups.value.find(g => g.key === 'core'))
// Hide groups that have no visible fields (e.g. Blood Moon is entirely sandbox-governed on 3.0).
const otherGroups = computed(() =>
  groups.value.filter(g => g.key !== 'core' && visibleFieldsFor(g).length > 0)
)

const groupLabels: Record<string, string> = {
  core: 'config.group.core',
  world: 'config.group.world',
  player: 'config.group.player',
  gameplay: 'config.group.gameplay',
  blockDamage: 'config.group.blockDamage',
  zombies: 'config.group.zombies',
  bloodMoon: 'config.group.bloodMoon',
  lootAndDrops: 'config.group.lootAndDrops',
  landClaims: 'config.group.landClaims',
  networkAndSlots: 'config.group.networkAndSlots',
  admin: 'config.group.admin',
  advanced: 'config.group.advanced',
}

/** Fallback display name if no i18n translation exists for a group key. */
const fallbackGroupTitles: Record<string, string> = {
  world: 'World',
  player: 'Player',
  gameplay: 'Gameplay',
  blockDamage: 'Block Damage',
  zombies: 'Zombies',
  bloodMoon: 'Blood Moon',
  lootAndDrops: 'Loot & Drops',
  landClaims: 'Land Claims',
  networkAndSlots: 'Network & Slots',
  admin: 'Admin',
  advanced: 'Advanced',
}

async function loadConfig() {
  loading.value = true
  try {
    const [configData, worldList] = await Promise.all([getConfig(), getWorlds()])
    properties.value = { ...configData.properties }
    originalProperties.value = { ...configData.properties }
    groups.value = configData.groups
    configPath.value = configData.configPath
    worlds.value = worldList
    is30.value = configData.is30 ?? false
    needsMigration.value = configData.needsMigration ?? false
  } catch (err) {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('config.failedToLoad'), life: 4000 })
  } finally {
    loading.value = false
  }
}

/** One-click 3.0 migration: the server comments out the sandbox-governed props,
 *  adds SandboxCode, and backs up first. Reloads so the editor reflects the result. */
async function runMigration() {
  migrating.value = true
  try {
    const result = await migrateConfigTo30()
    toast.add({ severity: 'success', summary: t('common.success', 'Done'), detail: result.message, life: 6000 })
    await loadConfig()
  } catch (err) {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('config.migrate30Failed', 'Migration failed'), life: 4000 })
  } finally {
    migrating.value = false
  }
}

async function loadRawXml() {
  try {
    rawXml.value = await getRawXml()
    rawXmlOriginal.value = rawXml.value
  } catch (err) {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('config.failedToLoad'), life: 4000 })
  }
}

function switchTab(tab: 'form' | 'raw') {
  activeTab.value = tab
  if (tab === 'raw' && !rawXml.value) {
    loadRawXml()
  }
}

function showSaveDialog() {
  confirmDialogVisible.value = true
}

async function handleSave() {
  confirmDialogVisible.value = false
  saving.value = true
  try {
    if (activeTab.value === 'raw') {
      await saveRawXml(rawXml.value)
      rawXmlOriginal.value = rawXml.value
      loadConfig()
    } else {
      const changed: Record<string, string> = {}
      for (const key of Object.keys(properties.value)) {
        if (properties.value[key] !== originalProperties.value[key]) {
          changed[key] = properties.value[key]
        }
      }
      if (Object.keys(changed).length === 0) {
        toast.add({ severity: 'info', summary: t('config.noChanges'), life: 2000 })
        return
      }
      await saveConfig(changed)
      originalProperties.value = { ...properties.value }
    }
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('config.saved'), life: 4000 })
  } catch (err) {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('config.failedToSave'), life: 4000 })
  } finally {
    saving.value = false
  }
}

function getFieldValue(key: string): string {
  const lower = key.toLowerCase()
  const match = Object.keys(properties.value).find(k => k.toLowerCase() === lower)
  return match ? properties.value[match] : ''
}

function setFieldValue(key: string, value: string) {
  const lower = key.toLowerCase()
  const match = Object.keys(properties.value).find(k => k.toLowerCase() === lower)
  properties.value[match ?? key] = value
}

function getBoolValue(key: string): boolean {
  const val = getFieldValue(key).toLowerCase()
  return val === 'true' || val === '1'
}

function setBoolValue(key: string, value: boolean) {
  setFieldValue(key, value ? 'true' : 'false')
}

function getSelectOptions(field: { key: string; options?: string[]; labels?: string[] }) {
  if (field.key === 'GameWorld') {
    const opts = [...(field.options || []), ...worlds.value]
    return [...new Set(opts)].map(v => ({ label: v, value: v }))
  }
  return (field.options || []).map((v, i) => ({
    label: field.labels?.[i] ?? v,
    value: v
  }))
}

/**
 * Fields we want to render individually in a group's grid. For gameplay, skip
 * DayNightLength + DayLightLength because the DayNightCycleWidget covers them.
 */
function visibleFieldsFor(group: ConfigFieldGroup) {
  let fields = group.fields
  // DayNight pair is rendered by the DayNightCycleWidget, not as individual fields.
  if (group.key === 'gameplay') {
    fields = fields.filter((f) => !DAY_NIGHT_KEYS.includes(f.key))
  }
  // The SandboxCode field only makes sense on a 3.0 server (show it even before one is set).
  if (!is30.value) {
    fields = fields.filter((f) => f.key !== SANDBOX_CODE_KEY)
  }
  // Hide the sandbox-governed settings only when a SandboxCode actually overrides them.
  // With no code set, 3.0 still reads these individual properties, so keep them editable.
  if (hasSandboxCode.value) {
    fields = fields.filter((f) => !f.sandboxGoverned)
  }
  return fields
}

function onDayNightUpdate(cfg: { DayNightLength: number; DayLightLength: number }) {
  setFieldValue('DayNightLength', String(cfg.DayNightLength))
  setFieldValue('DayLightLength', String(cfg.DayLightLength))
}

onMounted(loadConfig)
</script>

<template>
  <div class="config-editor-view">
    <div class="page-header">
      <div>
        <h1 class="page-title">{{ t('config.title') }}</h1>
        <p class="config-path" v-if="configPath">{{ configPath }}</p>
      </div>
      <div class="header-actions">
        <div class="tab-buttons">
          <Button
            :label="t('config.formView')"
            :severity="activeTab === 'form' ? 'info' : 'secondary'"
            size="small"
            @click="switchTab('form')"
          />
          <Button
            :label="t('config.rawView')"
            :severity="activeTab === 'raw' ? 'info' : 'secondary'"
            size="small"
            @click="switchTab('raw')"
          />
        </div>
        <Button
          :label="t('config.saveChanges')"
          icon="pi pi-save"
          severity="info"
          :disabled="!isDirty || saving"
          :loading="saving"
          @click="showSaveDialog"
        />
      </div>
    </div>

    <Message v-if="isDirty" severity="warn" :closable="false" class="dirty-banner">
      {{ t('config.unsavedChanges') }}
    </Message>

    <Message v-if="!loading && activeTab === 'form' && is30 && needsMigration" severity="warn" :closable="false" class="sandbox-banner">
      <div class="migrate-row">
        <span>{{ t('config.migrate30Notice', 'This server is on 7 Days to Die 3.0, but serverconfig.xml still has the old per-setting properties that 3.0 moved into the Sandbox. Migrating comments them out (preserved, not deleted), adds a Sandbox Code field, and leaves everything else alone. A backup is saved first.') }}</span>
        <Button :label="t('config.migrate30Button', 'Migrate to 3.0')" icon="pi pi-sync" severity="warn" size="small" :loading="migrating" @click="runMigration" />
      </div>
    </Message>

    <Message v-if="!loading && activeTab === 'form' && is30 && !hasSandboxCode" severity="info" :closable="false" class="sandbox-banner">
      {{ t('config.sandboxUpgradeNotice', 'This is a 7 Days to Die 3.0 server. Your individual settings below are still active — 3.0 reads them when no Sandbox code is set. To switch to the new Sandbox system and unlock 3.0-only options, generate a code in-game (New Game → Sandbox Options → copy code) and paste it into the Sandbox Code field. Saving writes it to both serverconfig.xml and the sticky .bak, so it survives restarts.') }}
    </Message>

    <Message v-if="!loading && activeTab === 'form' && hasSandboxCode" severity="info" :closable="false" class="sandbox-banner">
      {{ t('config.sandboxActiveNotice', 'A Sandbox code is set — it governs difficulty, XP, blood moon, loot, zombie behavior and other world settings, so those individual settings are hidden (the server reads them from the code, not serverconfig.xml). Clear the Sandbox Code field to return to individual settings.') }}
    </Message>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem" />
      <p>{{ t('common.loading') }}</p>
    </div>

    <!-- Form View -->
    <div v-else-if="activeTab === 'form'" class="form-view">
      <!-- Core Settings — prominent card with 2-col field grid -->
      <div v-if="coreGroup" class="core-card">
        <div class="core-header">
          <div class="core-accent" />
          <h3 class="group-title">{{ t(groupLabels[coreGroup.key] || coreGroup.key) }}</h3>
        </div>
        <div class="core-fields">
          <div v-for="field in coreGroup.fields" :key="field.key" class="field-item">
            <label class="field-label">{{ t('config.field.' + field.key, formatFieldLabel(field.key)) }}</label>
            <small v-if="field.description" class="field-description">{{ field.description }}</small>
            <InputText
              v-if="field.type === 'text'"
              :modelValue="getFieldValue(field.key)"
              @update:modelValue="setFieldValue(field.key, String($event ?? ''))"
              class="field-input"
            />
            <div v-else-if="field.type === 'password'" class="password-field">
              <InputText
                :modelValue="getFieldValue(field.key)"
                @update:modelValue="setFieldValue(field.key, String($event ?? ''))"
                :type="passwordVisible[field.key] ? 'text' : 'password'"
                class="field-input"
              />
              <button type="button" class="password-toggle" @click="passwordVisible[field.key] = !passwordVisible[field.key]">
                <i :class="passwordVisible[field.key] ? 'pi pi-eye-slash' : 'pi pi-eye'" />
              </button>
            </div>
            <Select
              v-else-if="field.type === 'select'"
              :modelValue="getFieldValue(field.key)"
              @update:modelValue="setFieldValue(field.key, $event)"
              :options="getSelectOptions(field)"
              optionLabel="label"
              optionValue="value"
              class="field-input"
            />
          </div>
        </div>
      </div>

      <!-- Other groups — 3-column card grid -->
      <div class="groups-grid">
        <div v-for="group in otherGroups" :key="group.key" class="group-card">
          <div class="group-card-header">
            {{ t(groupLabels[group.key] || group.key, fallbackGroupTitles[group.key] || group.key) }}
          </div>
          <div class="group-card-fields">
            <!-- Day/Night cycle widget lives in the gameplay group, above the grid -->
            <div
              v-if="group.key === 'gameplay' && !hasSandboxCode"
              class="field-item field-item-widget"
            >
              <DayNightCycleWidget
                :dayNightLength="Number(getFieldValue('DayNightLength')) || 60"
                :dayLightLength="Number(getFieldValue('DayLightLength')) || 18"
                @update="onDayNightUpdate"
              />
            </div>
            <div v-for="field in visibleFieldsFor(group)" :key="field.key" class="field-item">
              <label class="field-label">{{ t('config.field.' + field.key, formatFieldLabel(field.key)) }}</label>
              <small v-if="field.description" class="field-description">{{ field.description }}</small>

              <InputText
                v-if="field.type === 'text'"
                :modelValue="getFieldValue(field.key)"
                @update:modelValue="setFieldValue(field.key, String($event ?? ''))"
                class="field-input"
              />
              <div v-else-if="field.type === 'password'" class="password-field">
                <InputText
                  :modelValue="getFieldValue(field.key)"
                  @update:modelValue="setFieldValue(field.key, String($event ?? ''))"
                  :type="passwordVisible[field.key] ? 'text' : 'password'"
                  class="field-input"
                />
                <button type="button" class="password-toggle" @click="passwordVisible[field.key] = !passwordVisible[field.key]">
                  <i :class="passwordVisible[field.key] ? 'pi pi-eye-slash' : 'pi pi-eye'" />
                </button>
              </div>
              <InputNumber
                v-else-if="field.type === 'number'"
                :modelValue="Number(getFieldValue(field.key)) || 0"
                @update:modelValue="setFieldValue(field.key, String($event ?? 0))"
                :min="field.min ?? undefined"
                :max="field.max ?? undefined"
                class="field-input"
              />
              <div v-else-if="field.type === 'bool'" class="bool-field">
                <ToggleSwitch
                  :modelValue="getBoolValue(field.key)"
                  @update:modelValue="setBoolValue(field.key, $event)"
                />
                <span class="bool-label">{{ getBoolValue(field.key) ? t('common.enabled') : t('common.disabled') }}</span>
              </div>
              <Select
                v-else-if="field.type === 'select'"
                :modelValue="getFieldValue(field.key)"
                @update:modelValue="setFieldValue(field.key, $event)"
                :options="getSelectOptions(field)"
                optionLabel="label"
                optionValue="value"
                class="field-input"
              />
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Raw XML View -->
    <div v-else class="raw-view">
      <Textarea
        v-model="rawXml"
        class="raw-textarea"
        :autoResize="false"
        rows="30"
      />
    </div>

    <!-- Save confirmation dialog -->
    <Dialog
      v-model:visible="confirmDialogVisible"
      :header="t('config.confirmSave')"
      modal
      :style="{ width: '420px' }"
    >
      <p>{{ t('config.restartRequired') }}</p>
      <template #footer>
        <Button :label="t('common.cancel')" severity="secondary" text @click="confirmDialogVisible = false" />
        <Button :label="t('config.saveChanges')" severity="info" @click="handleSave" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.config-editor-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 600;
  margin: 0;
}

.config-path {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  font-family: monospace;
  margin-top: 0.25rem;
  opacity: 0.7;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.tab-buttons {
  display: flex;
  gap: 0.25rem;
}

.dirty-banner {
  margin: 0;
}

.migrate-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.migrate-row > span {
  flex: 1;
  min-width: 240px;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 1rem;
  padding: 3rem;
  color: var(--kc-text-secondary);
}

.form-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

/* Core Settings — prominent card at top */
.core-card {
  background: linear-gradient(135deg, var(--kc-bg-secondary) 0%, var(--kc-bg-card) 100%);
  border: 1px solid var(--kc-border);
  border-radius: 12px;
  padding: 1.25rem;
}

.core-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.core-accent {
  width: 4px;
  height: 22px;
  border-radius: 4px;
  background: linear-gradient(to bottom, var(--kc-cyan), var(--kc-cyan-dark));
}

.group-title {
  font-size: 0.95rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--kc-text-primary);
  margin: 0;
}

.core-fields {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
  gap: 1rem;
}

/* Group cards — 3-column grid */
.groups-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1rem;
  align-items: start;
}

.group-card {
  background: rgba(26, 35, 50, 0.5);
  border: 1px solid var(--kc-border);
  border-radius: 12px;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.group-card-header {
  font-size: 0.7rem;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--kc-text-secondary);
  font-weight: 600;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid var(--kc-border);
}

.group-card-fields {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
}

/* Widget field items fill their row, no label stacking */
.field-item-widget {
  width: 100%;
}

/* Fields */
.field-item {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.field-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--kc-text-primary);
  letter-spacing: 0.01em;
}

.field-description {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  opacity: 0.82;
  line-height: 1.45;
  margin-bottom: 0.15rem;
}

.field-input {
  width: 100%;
}

/* Form input dark theme */
.form-view :deep(.p-inputtext),
.form-view :deep(.p-inputnumber-input),
.form-view :deep(.p-select) {
  background: var(--kc-bg-primary);
  color: var(--kc-text-primary);
  border: 1px solid var(--kc-border);
  border-radius: 8px;
  font-size: 0.85rem;
  transition: border-color 0.15s;
}

.form-view :deep(.p-inputtext:focus),
.form-view :deep(.p-inputnumber-input:focus),
.form-view :deep(.p-select.p-focus) {
  border-color: var(--kc-cyan-dark);
  box-shadow: 0 0 0 1px rgba(0, 212, 255, 0.15);
}

.form-view :deep(.p-select-label) {
  color: var(--kc-text-primary);
}

.form-view :deep(.p-select-dropdown) {
  color: var(--kc-text-secondary);
}

.password-field {
  position: relative;
  display: flex;
  align-items: center;
}

.password-toggle {
  position: absolute;
  right: 0.5rem;
  background: none;
  border: none;
  color: var(--kc-text-secondary);
  cursor: pointer;
  padding: 0.25rem;
  font-size: 0.9rem;
  opacity: 0.5;
  transition: opacity 0.15s;
}

.password-toggle:hover {
  opacity: 1;
  color: var(--kc-cyan);
}

.bool-field {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding-top: 0.15rem;
}

.bool-label {
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
}

/* Raw XML */
.raw-view {
  flex: 1;
}

.raw-textarea {
  width: 100%;
  font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
  font-size: 0.85rem;
  background: var(--kc-bg-primary);
  color: #e6edf3;
  border: 1px solid var(--kc-border);
  border-radius: 10px;
  padding: 1rem;
  resize: vertical;
  min-height: 400px;
  line-height: 1.6;
}

.raw-textarea:focus {
  border-color: var(--kc-cyan-dark);
  outline: none;
  box-shadow: 0 0 0 1px rgba(0, 212, 255, 0.15);
}

@media (max-width: 1200px) {
  .groups-grid { grid-template-columns: repeat(2, 1fr); }
}

@media (max-width: 768px) {
  .page-header { flex-direction: column; }
  .header-actions { flex-wrap: wrap; }
  .core-fields { grid-template-columns: 1fr; }
  .groups-grid { grid-template-columns: 1fr; }
}
</style>
