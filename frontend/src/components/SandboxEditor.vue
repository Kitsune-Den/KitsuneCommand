<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Message from 'primevue/message'
import {
  decodeSandboxCode,
  encodeSandboxCode,
  sandboxDefaults,
  sandboxDisabledBy,
  sandboxPresetMatching,
  sandboxSelectionFromPreset,
  type SandboxOptionDef,
  type SandboxSelection,
  type SandboxTable,
} from '@/lib/sandboxCode'

/**
 * The 7D2D 3.0+ Sandbox editor.
 *
 * 3.0 packs the world/gameplay ruleset into one opaque SandboxCode. Instead of
 * building it in-game and pasting it back, the operator edits the options
 * here and the code is generated live — the same code the game's own copy
 * button would give for the same settings.
 *
 * The option table comes from the running game (it differs between builds,
 * and KC reads it live, so it is always the right one). The code itself is the
 * source of truth: it's the SandboxCode property in serverconfig.xml, saved
 * with the rest of the config.
 */
const props = defineProps<{
  /** The SandboxCode being edited (serverconfig.xml, unsaved edits included). */
  code: string
  /** The SandboxCode as last saved, for "changed since save" markers. */
  savedCode: string
  table: SandboxTable
  /** The code the running world was started with, if known. */
  liveCode?: string | null
}>()

const emit = defineEmits<{ (e: 'update:code', value: string): void }>()

const { t } = useI18n()

const activeCategory = ref(props.table.categories[0]?.name ?? '')
const query = ref('')
const paste = ref('')
const pasteNote = ref('')
const pasteError = ref(false)
const copied = ref(false)

const hasCode = computed(() => props.code.trim() !== '')
const decoded = computed(() => (hasCode.value ? decodeSandboxCode(props.code, props.table) : null))
/** The code is set but this build can't read it — editing would replace it. */
const unreadable = computed(() => decoded.value !== null && !decoded.value.ok)

const selection = computed<SandboxSelection>(() =>
  decoded.value?.ok ? decoded.value.selection : sandboxDefaults(props.table),
)
const savedSelection = computed<SandboxSelection>(() => {
  const d = props.savedCode.trim() ? decodeSandboxCode(props.savedCode, props.table) : null
  return d?.ok ? d.selection : sandboxDefaults(props.table)
})

const generatedCode = computed(() => encodeSandboxCode(selection.value, props.table))
const disabledBy = computed(() => sandboxDisabledBy(selection.value, props.table))
const changedCount = computed(() => props.table.options.filter((o) => selection.value[o.name] !== o.defaultIndex).length)
const currentPreset = computed(() => (hasCode.value && !unreadable.value ? sandboxPresetMatching(selection.value, props.table) : undefined))

/** The saved code isn't what the running world was started with: it applies on restart. */
const pendingRestart = computed(() => {
  if (props.liveCode == null) return false
  return props.liveCode.trim().toUpperCase() !== props.savedCode.trim().toUpperCase()
})

const presetOptions = computed(() => {
  const groups: { label: string; items: { label: string; value: string }[] }[] = []
  for (const p of props.table.presets) {
    let group = groups.find((g) => g.label === (p.groupLabel || p.group))
    if (!group) groups.push((group = { label: p.groupLabel || p.group, items: [] }))
    group.items.push({ label: p.label || p.name, value: p.name })
  }
  return groups
})

const changedSinceSave = (o: SandboxOptionDef) => selection.value[o.name] !== savedSelection.value[o.name]

const categoryChanged = (name: string) =>
  props.table.options.some((o) => o.category === name && changedSinceSave(o))

const visibleOptions = computed(() => {
  const q = query.value.trim().toLowerCase()
  if (q === '') return props.table.options.filter((o) => o.category === activeCategory.value)
  return props.table.options.filter((o) =>
    [o.label, o.name, o.description].some((s) => (s ?? '').toLowerCase().includes(q)),
  )
})

/** Search results span categories, so group them under their category name. */
const visibleSections = computed(() =>
  props.table.categories
    .map((c) => ({ ...c, options: visibleOptions.value.filter((o) => o.category === c.name) }))
    .filter((c) => c.options.length > 0),
)

function valueOptions(o: SandboxOptionDef) {
  return o.values.map((v, i) => ({
    label: i === o.defaultIndex ? `${v.label} ${t('config.sandbox.defaultMarker')}` : v.label,
    value: i,
  }))
}

function setOption(name: string, valueIndex: number) {
  emit('update:code', encodeSandboxCode({ ...selection.value, [name]: valueIndex }, props.table))
}

function applyPreset(name: string) {
  const preset = props.table.presets.find((p) => p.name === name)
  if (preset) emit('update:code', encodeSandboxCode(sandboxSelectionFromPreset(preset, props.table), props.table))
}

function resetAll() {
  emit('update:code', props.table.codeVersion)
}

function clearCode() {
  emit('update:code', '')
}

function loadPasted() {
  const result = decodeSandboxCode(paste.value, props.table)
  if (!result.ok) {
    pasteError.value = true
    pasteNote.value =
      result.error === 'wrong-version'
        ? t('config.sandbox.pasteWrongVersion', { got: result.codeVersion, want: props.table.codeVersion })
        : t('config.sandbox.pasteInvalid')
    return
  }
  // Keep what was pasted, as the game would store it: only the case changes.
  emit('update:code', paste.value.trim().toUpperCase())
  pasteError.value = false
  pasteNote.value = result.skipped.length
    ? t('config.sandbox.pasteSkipped', { count: result.skipped.length, version: props.table.gameVersion })
    : ''
  paste.value = ''
}

async function copyCode() {
  try {
    await navigator.clipboard.writeText(props.code.trim() || generatedCode.value)
    copied.value = true
    setTimeout(() => (copied.value = false), 1500)
  } catch {
    /* clipboard blocked (http, permissions); the code is selectable on screen */
  }
}

watch(
  () => props.table,
  (table) => {
    if (!table.categories.some((c) => c.name === activeCategory.value)) activeCategory.value = table.categories[0]?.name ?? ''
  },
)
</script>

<template>
  <div class="sandbox-card">
    <div class="sandbox-header">
      <div class="sandbox-title-row">
        <div class="sandbox-accent" />
        <h3 class="sandbox-title">{{ t('config.sandbox.title') }}</h3>
        <span class="sandbox-version">{{ table.gameVersion }}</span>
      </div>
      <span class="sandbox-sub">
        {{ hasCode ? t('config.sandbox.changedCount', { changed: changedCount, total: table.options.length }) : t('config.sandbox.notInUse') }}
      </span>
    </div>

    <Message v-if="!hasCode" severity="info" :closable="false">
      {{ t('config.sandbox.noCodeNotice') }}
    </Message>
    <Message v-if="unreadable" severity="warn" :closable="false">
      {{ decoded && !decoded.ok && decoded.error === 'wrong-version'
        ? t('config.sandbox.unreadableVersion', { got: decoded.codeVersion, want: table.codeVersion })
        : t('config.sandbox.unreadable') }}
    </Message>
    <Message v-if="pendingRestart" severity="secondary" :closable="false">
      {{ t('config.sandbox.pendingRestart') }}
    </Message>

    <!-- Preset + code -->
    <div class="sandbox-top">
      <div v-if="table.presets.length" class="sandbox-preset">
        <label class="field-label">{{ t('config.sandbox.preset') }}</label>
        <Select
          :modelValue="currentPreset?.name ?? null"
          @update:modelValue="applyPreset($event)"
          :options="presetOptions"
          optionLabel="label"
          optionValue="value"
          optionGroupLabel="label"
          optionGroupChildren="items"
          :placeholder="hasCode ? t('config.sandbox.custom') : t('config.sandbox.pickPreset')"
          class="field-input"
        />
        <small class="field-description">
          <template v-if="currentPreset">
            {{ currentPreset.description }}
            <span v-if="currentPreset.rating" class="sandbox-rating">{{ t('config.sandbox.rating', { rating: currentPreset.rating }) }}</span>
          </template>
          <template v-else-if="hasCode">{{ t('config.sandbox.customDescription') }}</template>
        </small>
      </div>

      <div class="sandbox-code">
        <label class="field-label">{{ t('config.sandbox.code') }}</label>
        <div class="sandbox-code-row">
          <code class="sandbox-code-text">{{ hasCode ? code.trim() : '—' }}</code>
          <Button
            :icon="copied ? 'pi pi-check' : 'pi pi-copy'"
            severity="secondary"
            text
            size="small"
            :disabled="!hasCode"
            :aria-label="t('config.sandbox.copy')"
            :title="t('config.sandbox.copy')"
            @click="copyCode"
          />
        </div>
        <small class="field-description">{{ t('config.sandbox.codeNote') }}</small>
      </div>
    </div>

    <!-- Tools -->
    <div class="sandbox-tools">
      <InputText v-model="query" :placeholder="t('config.sandbox.search')" class="sandbox-search" />
      <div class="sandbox-paste">
        <InputText
          v-model="paste"
          :placeholder="t('config.sandbox.pastePlaceholder')"
          class="sandbox-paste-input"
          spellcheck="false"
          autocomplete="off"
          @keydown.enter.prevent="loadPasted"
          @input="pasteNote = ''"
        />
        <Button :label="t('config.sandbox.load')" severity="secondary" size="small" :disabled="!paste.trim()" @click="loadPasted" />
      </div>
      <Button :label="t('config.sandbox.resetAll')" icon="pi pi-replay" severity="secondary" size="small" text @click="resetAll" />
      <Button
        v-if="hasCode"
        :label="t('config.sandbox.stopUsing')"
        icon="pi pi-times"
        severity="secondary"
        size="small"
        text
        :title="t('config.sandbox.stopUsingHint')"
        @click="clearCode"
      />
    </div>
    <small v-if="pasteNote" :class="['sandbox-paste-note', { error: pasteError }]">{{ pasteNote }}</small>

    <!-- Category tabs (hidden while searching: results span categories) -->
    <div v-if="!query.trim()" class="sandbox-tabs" role="tablist">
      <button
        v-for="c in table.categories"
        :key="c.name"
        type="button"
        role="tab"
        :aria-selected="activeCategory === c.name"
        :class="['sandbox-tab', { active: activeCategory === c.name }]"
        @click="activeCategory = c.name"
      >
        {{ c.label }}
        <span v-if="categoryChanged(c.name)" class="changed-dot" :aria-label="t('config.sandbox.unsavedInCategory')" />
      </button>
    </div>

    <p v-if="query.trim() && visibleSections.length === 0" class="sandbox-empty">
      {{ t('config.sandbox.noMatch', { query: query.trim() }) }}
    </p>

    <section v-for="section in visibleSections" :key="section.name" class="sandbox-section">
      <div v-if="query.trim()" class="sandbox-section-title">{{ section.label }}</div>
      <div class="sandbox-grid">
        <div
          v-for="o in section.options"
          :key="o.name"
          :class="['field-item', { disabled: !!disabledBy[o.name] }]"
        >
          <label class="field-label">
            {{ o.label || o.name }}
            <span v-if="selection[o.name] !== o.defaultIndex" class="changed-tag">{{ t('config.sandbox.changedTag') }}</span>
            <span v-if="changedSinceSave(o)" class="changed-dot" :aria-label="t('config.sandbox.unsaved')" />
          </label>
          <small v-if="o.description" class="field-description">{{ o.description }}</small>
          <Select
            :modelValue="selection[o.name]"
            @update:modelValue="setOption(o.name, $event)"
            :options="valueOptions(o)"
            optionLabel="label"
            optionValue="value"
            :disabled="!!disabledBy[o.name] || unreadable"
            class="field-input"
          />
          <small v-if="disabledBy[o.name]" class="field-disabled-by">
            {{ t('config.sandbox.disabledBy', {
              option: disabledBy[o.name].label || disabledBy[o.name].name,
              value: disabledBy[o.name].values[selection[disabledBy[o.name].name]]?.label,
            }) }}
          </small>
        </div>
      </div>
    </section>
  </div>
</template>

<style scoped>
.sandbox-card {
  background: linear-gradient(135deg, var(--kc-bg-secondary) 0%, var(--kc-bg-card) 100%);
  border: 1px solid var(--kc-border);
  border-radius: 12px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.sandbox-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 1rem;
  flex-wrap: wrap;
}

.sandbox-title-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.sandbox-accent {
  width: 4px;
  height: 22px;
  border-radius: 4px;
  background: linear-gradient(to bottom, var(--kc-orange), var(--kc-orange-dark));
}

.sandbox-title {
  font-size: 0.95rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--kc-text-primary);
  margin: 0;
}

.sandbox-version,
.sandbox-sub {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
}

.sandbox-top {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 1rem;
}

.sandbox-preset,
.sandbox-code {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  min-width: 0;
}

.sandbox-code-row {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  background: var(--kc-bg-primary);
  border: 1px solid var(--kc-border);
  border-radius: 8px;
  padding: 0.3rem 0.3rem 0.3rem 0.75rem;
  min-width: 0;
}

.sandbox-code-text {
  flex: 1;
  min-width: 0;
  font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
  font-size: 0.8rem;
  color: var(--kc-cyan);
  overflow-wrap: anywhere;
  user-select: all;
}

.sandbox-rating {
  margin-left: 0.4rem;
  color: var(--kc-orange);
}

.sandbox-tools {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.sandbox-search {
  flex: 1 1 220px;
}

.sandbox-paste {
  display: flex;
  gap: 0.35rem;
  flex: 1 1 280px;
}

.sandbox-paste-input {
  flex: 1;
  font-family: 'Cascadia Code', 'Fira Code', 'Consolas', monospace;
}

.sandbox-paste-note {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
}
.sandbox-paste-note.error {
  color: #f87171;
}

.sandbox-tabs {
  display: flex;
  gap: 0.25rem;
  flex-wrap: wrap;
  border-bottom: 1px solid var(--kc-border);
  padding-bottom: 0.5rem;
}

.sandbox-tab {
  position: relative;
  background: none;
  border: 1px solid transparent;
  border-radius: 8px;
  color: var(--kc-text-secondary);
  font-size: 0.8rem;
  font-weight: 600;
  padding: 0.4rem 0.8rem;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
}
.sandbox-tab:hover {
  color: var(--kc-text-primary);
}
.sandbox-tab.active {
  color: var(--kc-cyan);
  border-color: var(--kc-border);
  background: var(--kc-bg-primary);
}

.sandbox-section {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.sandbox-section-title {
  font-size: 0.7rem;
  text-transform: uppercase;
  letter-spacing: 0.1em;
  color: var(--kc-text-secondary);
  font-weight: 600;
}

.sandbox-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
  gap: 1rem;
}

.sandbox-empty {
  color: var(--kc-text-secondary);
  font-size: 0.85rem;
  margin: 0;
}

.field-item {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}
.field-item.disabled {
  opacity: 0.55;
}

.field-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--kc-text-primary);
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.field-description {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  opacity: 0.82;
  line-height: 1.45;
  /* The game's descriptions use line breaks ("None = No penalty.\nXP Only = ..."). */
  white-space: pre-line;
}

.field-disabled-by {
  font-size: 0.72rem;
  color: var(--kc-orange);
}

.field-input {
  width: 100%;
}

/* Line the dropdowns up across a row whatever the description length. */
.sandbox-grid .field-input {
  margin-top: auto;
}

.changed-tag {
  font-size: 0.65rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--kc-orange);
  border: 1px solid var(--kc-orange-dark);
  border-radius: 4px;
  padding: 0 0.3rem;
}

.changed-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--kc-cyan);
  display: inline-block;
}

.sandbox-card :deep(.p-inputtext),
.sandbox-card :deep(.p-select) {
  background: var(--kc-bg-primary);
  color: var(--kc-text-primary);
  border: 1px solid var(--kc-border);
  border-radius: 8px;
  font-size: 0.85rem;
}

.sandbox-card :deep(.p-select-label) {
  color: var(--kc-text-primary);
}

@media (max-width: 768px) {
  .sandbox-top {
    grid-template-columns: 1fr;
  }
}
</style>
