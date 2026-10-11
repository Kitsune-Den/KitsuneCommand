import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import SandboxEditor from '@/components/SandboxEditor.vue'
import type { SandboxTable } from '@/lib/sandboxCode'

// A small table in the shape SandboxTableService serves. Indices are the real
// 3.3 ones for these options, so the codes are the game's codes.
const table: SandboxTable = {
  gameVersion: 'V 3.3.0 (b18)',
  codeVersion: 'A',
  categories: [
    { name: 'General', label: 'Player' },
    { name: 'World', label: 'World' },
  ],
  options: [
    { index: 0, name: 'RangedDamage', label: 'Ranged Damage', description: '', category: 'General', kind: 'float', defaultIndex: 2,
      values: [{ label: 'None', value: '0' }, { label: '50%', value: '0.5' }, { label: '100%', value: '1' }] },
    { index: 9, name: 'StaminaUsage', label: 'Stamina Usage', description: '', category: 'General', kind: 'float', defaultIndex: 1,
      values: [{ label: 'None', value: '0' }, { label: '100%', value: '1' }],
      disables: { options: ['StaminaRegen'], whenValueIndices: [0] } },
    { index: 10, name: 'StaminaRegen', label: 'Stamina Regen', description: '', category: 'General', kind: 'float', defaultIndex: 0,
      values: [{ label: '100%', value: '1' }, { label: '200%', value: '2' }] },
    { index: 40, name: 'BloodMoonFrequency', label: 'Blood Moon Frequency', description: 'How often', category: 'World', kind: 'int', defaultIndex: 1,
      values: [{ label: 'Disabled', value: '0' }, { label: '7 Days', value: '7' }] },
  ],
  presets: [
    { name: 'Hard', label: 'Hard', description: 'Hurts', group: 'Difficulty', groupLabel: 'Difficulty', rating: 6, isDefault: false, isUser: false, isModded: false, values: { RangedDamage: 1 } },
  ],
}

// Select stub that exposes its props and lets a test pick a value.
const SelectStub = defineComponent({
  name: 'SelectStub',
  props: ['modelValue', 'options', 'optionLabel', 'optionValue', 'optionGroupLabel', 'optionGroupChildren', 'disabled', 'placeholder'],
  emits: ['update:modelValue'],
  template: '<div class="select-stub" />',
})

function mountEditor(code: string, savedCode = code, liveCode: string | null = savedCode) {
  return mount(SandboxEditor, {
    props: { code, savedCode, table, liveCode },
    global: {
      stubs: {
        Select: SelectStub,
        Message: { template: '<div class="message"><slot /></div>' },
        Button: { template: '<button class="btn" :data-label="label" @click="$emit(\'click\')">{{ label }}</button>', props: ['label', 'icon', 'severity', 'size', 'text', 'disabled'] },
        InputText: { template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />', props: ['modelValue'] },
      },
    },
  })
}

/** The value Select for an option, by its label. */
function optionSelect(wrapper: ReturnType<typeof mountEditor>, label: string) {
  const item = wrapper.findAll('.field-item').find((el) => el.find('.field-label').text().startsWith(label))!
  return item.findComponent(SelectStub)
}

const lastCode = (wrapper: ReturnType<typeof mountEditor>) => {
  const emitted = wrapper.emitted('update:code')!
  return emitted[emitted.length - 1][0]
}

describe('SandboxEditor', () => {
  it('shows the decoded value and encodes a change as the game would', async () => {
    const wrapper = mountEditor('AAAB') // RangedDamage = 50%
    expect(optionSelect(wrapper, 'Ranged Damage').props('modelValue')).toBe(1)
    optionSelect(wrapper, 'Ranged Damage').vm.$emit('update:modelValue', 0)
    expect(lastCode(wrapper)).toBe('AAAA')
  })

  it('leaves options at their default out of the code', () => {
    const wrapper = mountEditor('AAAB')
    optionSelect(wrapper, 'Ranged Damage').vm.$emit('update:modelValue', 2)
    expect(lastCode(wrapper)).toBe('A')
  })

  it('applies a preset as a whole ruleset', () => {
    const wrapper = mountEditor('AAJA') // StaminaUsage off; Hard doesn't set it
    const presetSelect = wrapper.findAllComponents(SelectStub).find((s) => s.props('optionGroupChildren') === 'items')!
    presetSelect.vm.$emit('update:modelValue', 'Hard')
    expect(lastCode(wrapper)).toBe('AAAB')
  })

  it('recognises the preset a code matches', () => {
    const wrapper = mountEditor('AAAB')
    const presetSelect = wrapper.findAllComponents(SelectStub).find((s) => s.props('optionGroupChildren') === 'items')!
    expect(presetSelect.props('modelValue')).toBe('Hard')
    expect(wrapper.text()).toContain('Hurts')
  })

  it('disables an option another option switches off', () => {
    const wrapper = mountEditor('AAJA') // StaminaUsage = None
    expect(optionSelect(wrapper, 'Stamina Regen').props('disabled')).toBe(true)
    expect(wrapper.text()).toContain('config.sandbox.disabledBy')
  })

  it('loads a pasted code, keeping it as pasted', async () => {
    const wrapper = mountEditor('')
    const paste = wrapper.findAll('input').find((i) => i.attributes('placeholder') === 'config.sandbox.pastePlaceholder')!
    await paste.setValue(' abm ')
    await wrapper.findAll('button').find((b) => b.attributes('data-label') === 'config.sandbox.load')!.trigger('click')
    expect(lastCode(wrapper)).toBe('ABM')
  })

  it('refuses a code for another format', async () => {
    const wrapper = mountEditor('')
    const paste = wrapper.findAll('input').find((i) => i.attributes('placeholder') === 'config.sandbox.pastePlaceholder')!
    await paste.setValue('BAAA')
    await wrapper.findAll('button').find((b) => b.attributes('data-label') === 'config.sandbox.load')!.trigger('click')
    expect(wrapper.emitted('update:code')).toBeUndefined()
    expect(wrapper.text()).toContain('config.sandbox.pasteWrongVersion')
  })

  it('clears the code to go back to individual settings', async () => {
    const wrapper = mountEditor('AAAB')
    await wrapper.findAll('button').find((b) => b.attributes('data-label') === 'config.sandbox.stopUsing')!.trigger('click')
    expect(lastCode(wrapper)).toBe('')
  })

  it('says when the saved code is not what the world is running', () => {
    expect(mountEditor('AAAB', 'AAAB', 'A').text()).toContain('config.sandbox.pendingRestart')
    expect(mountEditor('AAAB', 'AAAB', 'aaab').text()).not.toContain('config.sandbox.pendingRestart')
  })

  it('switches category tabs', async () => {
    const wrapper = mountEditor('A')
    expect(wrapper.text()).not.toContain('Blood Moon Frequency')
    await wrapper.findAll('.sandbox-tab').find((t) => t.text().includes('World'))!.trigger('click')
    expect(wrapper.text()).toContain('Blood Moon Frequency')
  })

  it('searches across categories', async () => {
    const wrapper = mountEditor('A')
    const search = wrapper.findAll('input').find((i) => i.attributes('placeholder') === 'config.sandbox.search')!
    await search.setValue('how often')
    expect(wrapper.text()).toContain('Blood Moon Frequency')
    expect(wrapper.text()).not.toContain('Ranged Damage')
  })
})
