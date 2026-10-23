import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import en from '@/i18n/locales/en'
import SuspicionLogView from '@/views/SuspicionLogView.vue'
import type { SuspicionEvent, SuspicionSettings } from '@/api/suspicion'

// t echoes the key plus any params so assertions can see both; te answers from
// the real English messages so the view's "known rule?" fallback is exercised.
function lookup(path: string): unknown {
  return path.split('.').reduce<any>((o, k) => (o == null ? undefined : o[k]), en)
}
vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    t: (key: string, params?: Record<string, unknown>) => (params ? `${key} ${JSON.stringify(params)}` : key),
    te: (key: string) => typeof lookup(key) === 'string',
    locale: { value: 'en' },
  }),
}))

const mockGetEvents = vi.fn()
const mockReview = vi.fn()
const mockGetSettings = vi.fn()
const mockUpdateSettings = vi.fn()

vi.mock('@/api/suspicion', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api/suspicion')>()
  return {
    ...actual,
    getSuspicionEvents: (...args: any[]) => mockGetEvents(...args),
    reviewSuspicionEvents: (...args: any[]) => mockReview(...args),
    getSuspicionSettings: () => mockGetSettings(),
    updateSuspicionSettings: (...args: any[]) => mockUpdateSettings(...args),
  }
})

const mockToastAdd = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: mockToastAdd }) }))

// The table renders one .dt-row per row and each Column renders its #body slot
// for that row, so the real cell templates run.
vi.mock('primevue/datatable', async () => {
  const { defineComponent, h, provide } = await import('vue')
  const Row = defineComponent({
    props: ['row'],
    setup(props, { slots }) {
      provide('dtRow', props.row)
      return () => h('div', { class: 'dt-row' }, slots.default?.())
    },
  })
  return {
    default: defineComponent({
      name: 'DataTable',
      props: ['value', 'loading', 'paginator', 'rows', 'totalRecords', 'lazy', 'dataKey', 'rowsPerPageOptions', 'stripedRows'],
      setup(props, { slots }) {
        return () => h('div', [
          ...(props.value ?? []).map((row: any) => h(Row, { row, key: row.id }, () => slots.default?.())),
          (props.value ?? []).length === 0 ? slots.empty?.() : null,
        ])
      },
    }),
  }
})
vi.mock('primevue/column', async () => {
  const { defineComponent, h, inject } = await import('vue')
  return {
    default: defineComponent({
      name: 'Column',
      props: ['field', 'header'],
      setup(_, { slots }) {
        const row = inject<any>('dtRow', null)
        return () => (row ? h('div', { class: 'dt-cell' }, slots.body?.({ data: row })) : null)
      },
    }),
  }
})
vi.mock('primevue/button', () => ({
  default: {
    name: 'Button',
    props: ['label', 'icon', 'loading', 'disabled', 'severity', 'text', 'rounded', 'size'],
    emits: ['click'],
    template: '<button :disabled="disabled" @click="$emit(\'click\')">{{ label }}</button>',
  },
}))
vi.mock('primevue/tag', () => ({ default: { name: 'Tag', props: ['value', 'severity'], template: '<span class="tag">{{ value }}</span>' } }))
vi.mock('primevue/dialog', () => ({
  default: { name: 'Dialog', props: ['visible', 'header', 'modal'], template: '<div v-if="visible" class="dialog"><slot /><slot name="footer" /></div>' },
}))
vi.mock('primevue/select', () => ({ default: { name: 'Select', props: ['modelValue', 'options'], emits: ['update:modelValue'], template: '<select />' } }))
vi.mock('primevue/inputtext', () => ({
  default: { name: 'InputText', props: ['modelValue'], emits: ['update:modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
}))
vi.mock('primevue/textarea', () => ({
  default: { name: 'Textarea', props: ['modelValue'], emits: ['update:modelValue'], template: '<textarea :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />' },
}))
vi.mock('primevue/inputnumber', () => ({ default: { name: 'InputNumber', props: ['modelValue'], template: '<input type="number" />' } }))
vi.mock('primevue/toggleswitch', () => ({ default: { name: 'ToggleSwitch', props: ['modelValue', 'disabled'], template: '<input type="checkbox" />' } }))

function evt(overrides: Partial<SuspicionEvent> = {}): SuspicionEvent {
  return {
    id: 1,
    createdAt: '2026-10-23 12:00:00',
    playerId: 'EOS_ada',
    playerName: 'Ada',
    entityId: 171,
    detector: 'inventory',
    rule: 'over_stack',
    severity: 'high',
    summary: 'A stack of 9000 resourceWood; the limit is 6000',
    details: '{"item":"resourceWood","count":9000,"stackLimit":6000}',
    posX: 10.4,
    posY: 61,
    posZ: -20.6,
    reviewed: false,
    reviewedBy: null,
    reviewedAt: null,
    reviewNote: null,
    ...overrides,
  }
}

function settingsFixture(): SuspicionSettings {
  return {
    enabled: true,
    ignoreAdmins: true,
    exemptPlayerIds: ['EOS_trusted'],
    repeatCooldownMinutes: 10,
    retentionDays: 90,
    discordWebhookUrl: '',
    discordMinSeverity: 'medium',
    discordCooldownMinutes: 30,
    inventory: {
      enabled: true, scanIntervalSeconds: 60, flagDevItems: true, flagHiddenItems: false, flagOverQuality: true,
      maxQuality: 0, flagOverStack: true, flagCountJumps: true, countJumpThreshold: 2000, allowList: ['decoCookingPot'],
    },
    movement: {
      enabled: true, maxHorizontalSpeed: 15, speedSamplesRequired: 2, jumpDistance: 80, flagFlying: true,
      flyHeight: 12, flySamplesRequired: 4, graceSeconds: 10,
    },
    progression: { enabled: true, windowMinutes: 10, maxLevelGain: 10, maxUnexplainedSkillPoints: 5, warmupSeconds: 60 },
    killRate: { enabled: true, maxZombieKillsPerMinute: 60 },
  }
}

async function mountView(items: SuspicionEvent[] = [evt()], unreviewed = 1) {
  mockGetEvents.mockResolvedValue({ items, total: items.length, unreviewed })
  const wrapper = mount(SuspicionLogView)
  await flushPromises()
  return wrapper
}

describe('SuspicionLogView', () => {
  beforeEach(() => {
    mockGetEvents.mockReset()
    mockReview.mockReset().mockResolvedValue(1)
    mockGetSettings.mockReset()
    mockUpdateSettings.mockReset()
    mockToastAdd.mockReset()
  })

  it('loads the unreviewed entries first', async () => {
    await mountView()
    expect(mockGetEvents).toHaveBeenCalledWith({
      pageIndex: 0, pageSize: 50, detector: undefined, severity: undefined, reviewed: false, search: undefined,
    })
  })

  it('renders the player, a localized description built from the details, and the position', async () => {
    const wrapper = await mountView()
    const row = wrapper.find('.dt-row')

    expect(row.text()).toContain('Ada')
    expect(row.text()).toContain('EOS_ada')
    expect(row.text()).toContain('suspicion.rules.over_stack')
    expect(row.find('[data-test="describe"]').text()).toBe(
      'suspicion.describe.over_stack {"item":"resourceWood","count":"9000","stackLimit":"6000"}',
    )
    expect(row.text()).toContain('10, 61, -21')
  })

  it('falls back to the server summary for a rule it has no string for', async () => {
    const wrapper = await mountView([evt({ rule: 'future_rule', summary: 'Something new' })])
    expect(wrapper.find('[data-test="describe"]').text()).toBe('Something new')
    expect(wrapper.text()).toContain('future_rule')
  })

  it('shows the unreviewed count', async () => {
    const wrapper = await mountView([evt()], 7)
    expect(wrapper.find('.unreviewed-tag').text()).toBe('suspicion.unreviewedCount {"count":7}')
  })

  it('shows the empty state when nothing is flagged', async () => {
    const wrapper = await mountView([], 0)
    expect(wrapper.find('[data-test="empty"]').exists()).toBe(true)
    expect(wrapper.find('.unreviewed-tag').exists()).toBe(false)
    expect(wrapper.find('[data-test="mark-page"]').exists()).toBe(false)
  })

  it('marks one entry reviewed and reloads', async () => {
    const wrapper = await mountView()
    await wrapper.find('[data-test="review"]').trigger('click')
    await flushPromises()

    expect(mockReview).toHaveBeenCalledWith([1], true, undefined)
    expect(mockGetEvents).toHaveBeenCalledTimes(2)
  })

  it('reopens a reviewed entry', async () => {
    const wrapper = await mountView([evt({ id: 5, reviewed: true, reviewedBy: 'mod1' })])
    expect(wrapper.find('[data-test="reviewed"]').text()).toContain('mod1')

    await wrapper.find('[data-test="reopen"]').trigger('click')
    await flushPromises()
    expect(mockReview).toHaveBeenCalledWith([5], false, undefined)
  })

  it('marks only the open entries on the page reviewed', async () => {
    const wrapper = await mountView([evt({ id: 1 }), evt({ id: 2, reviewed: true }), evt({ id: 3 })])
    await wrapper.find('[data-test="mark-page"]').trigger('click')
    await flushPromises()
    expect(mockReview).toHaveBeenCalledWith([1, 3], true, undefined)
  })

  it('reviews from the details dialog with a note', async () => {
    const wrapper = await mountView()
    await wrapper.find('[data-test="details"]').trigger('click')

    const dialog = wrapper.find('[data-test="details-dialog"]')
    expect(dialog.exists()).toBe(true)
    expect(dialog.text()).toContain('stackLimit')
    expect(dialog.text()).toContain('6000')

    await dialog.find('textarea').setValue('  traded from a friend  ')
    await wrapper.find('[data-test="dialog-review"]').trigger('click')
    await flushPromises()

    expect(mockReview).toHaveBeenCalledWith([1], true, 'traded from a friend')
    expect(wrapper.find('[data-test="details-dialog"]').exists()).toBe(false)
  })

  it('reports a failed load', async () => {
    mockGetEvents.mockRejectedValue(new Error('boom'))
    mount(SuspicionLogView)
    await flushPromises()
    expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error', detail: 'suspicion.failedToLoad' }))
  })

  it('loads detector settings when the tab opens and saves the edited lists', async () => {
    const stored = settingsFixture()
    mockGetSettings.mockResolvedValue(stored)
    mockUpdateSettings.mockImplementation(async (s: SuspicionSettings) => s)
    const wrapper = await mountView()

    expect(mockGetSettings).not.toHaveBeenCalled()
    await wrapper.find('[data-test="tab-settings"]').trigger('click')
    await flushPromises()
    expect(mockGetSettings).toHaveBeenCalledTimes(1)

    const settings = wrapper.find('[data-test="settings"]')
    for (const section of ['inventory', 'movement', 'progression', 'killRate']) {
      expect(settings.find(`[data-test="section-${section}"]`).exists()).toBe(true)
    }

    const [exempt, allow] = settings.findAll('textarea')
    expect((exempt.element as HTMLTextAreaElement).value).toBe('EOS_trusted')
    await exempt.setValue('EOS_trusted\n\n  Steam_123  \n')
    await allow.setValue('decoCookingPot\nquestReward*')
    await wrapper.find('[data-test="save-settings"]').trigger('click')
    await flushPromises()

    const sent = mockUpdateSettings.mock.calls[0][0] as SuspicionSettings
    expect(sent.exemptPlayerIds).toEqual(['EOS_trusted', 'Steam_123'])
    expect(sent.inventory.allowList).toEqual(['decoCookingPot', 'questReward*'])
    expect(sent.movement.maxHorizontalSpeed).toBe(15)
    expect(mockToastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }))
  })

  it('every settings label and hint the form uses exists in English', () => {
    const sections: Record<string, string[]> = {
      inventory: ['title', 'subtitle', 'scanIntervalSeconds', 'flagDevItems', 'flagHiddenItems', 'flagOverQuality',
        'flagOverStack', 'flagCountJumps', 'maxQuality', 'countJumpThreshold', 'allowList', 'allowListHint'],
      movement: ['title', 'subtitle', 'flagFlying', 'maxHorizontalSpeed', 'speedSamplesRequired', 'jumpDistance',
        'flyHeight', 'flySamplesRequired', 'graceSeconds'],
      progression: ['title', 'subtitle', 'windowMinutes', 'maxLevelGain', 'maxUnexplainedSkillPoints', 'warmupSeconds'],
      killRate: ['title', 'subtitle', 'maxZombieKillsPerMinute'],
    }
    const missing = Object.entries(sections)
      .flatMap(([s, keys]) => keys.map((k) => `suspicion.settings.${s}.${k}`))
      .filter((path) => typeof lookup(path) !== 'string')
    expect(missing).toEqual([])
  })
})
