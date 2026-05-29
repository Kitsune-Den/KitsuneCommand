import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import PlayerEditDialog from '@/components/PlayerEditDialog.vue'
import type { PlayerInfo, PlayerMetadata } from '@/types'

const mockUpdatePlayerMetadata = vi.fn()
const mockSetAdminLevel = vi.fn()
const mockSetPlayerTier = vi.fn()
const mockGetVipTiers = vi.fn()

vi.mock('@/api/players', () => ({
  updatePlayerMetadata: (...a: any[]) => mockUpdatePlayerMetadata(...a),
  setAdminLevel: (...a: any[]) => mockSetAdminLevel(...a),
  setPlayerTier: (...a: any[]) => mockSetPlayerTier(...a),
}))
vi.mock('@/api/vipperks', () => ({
  getVipTiers: () => mockGetVipTiers(),
}))

// Store returns no cached metadata by default → tier starts as '' (pleb).
const mockGetMetadata = vi.fn((): PlayerMetadata | undefined => undefined)
vi.mock('@/stores/players', () => ({
  usePlayersStore: () => ({ getMetadata: mockGetMetadata }),
}))

const player: PlayerInfo = {
  playerId: 'EOS_abc',
  playerName: 'Alice',
  entityId: 42,
  platformId: 'Steam_76561190000000000',
  positionX: 0,
  positionY: 0,
  positionZ: 0,
  level: 1,
  health: 100,
  stamina: 100,
  zombieKills: 0,
  playerKills: 0,
  deaths: 0,
  totalPlayTime: 0,
  ip: '127.0.0.1',
  isOnline: true,
  score: 0,
  lastLogin: 0,
  isAdmin: false,
  adminLevel: 1000,
}

function mountDialog() {
  return mount(PlayerEditDialog, {
    props: { player, visible: false },
    global: {
      plugins: [createPinia(), [PrimeVue, { theme: 'none' }]],
      stubs: {
        Dialog: { template: '<div><slot /><slot name="footer" /></div>', props: ['visible', 'header', 'modal'] },
        Button: { template: '<button @click="$emit(\'click\')">{{ label }}</button>', props: ['label', 'icon', 'loading', 'text', 'severity', 'size'] },
        InputText: { template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />', props: ['modelValue', 'placeholder'] },
        Textarea: { template: '<textarea />', props: ['modelValue', 'rows', 'placeholder'] },
        ColorPicker: { template: '<input />', props: ['modelValue'] },
        Select: {
          template: '<select :data-testid="$attrs[\'data-testid\']" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
          props: ['modelValue', 'options', 'optionLabel', 'optionValue'],
        },
      },
    },
  })
}

describe('PlayerEditDialog — VIP tier', () => {
  beforeEach(() => {
    mockUpdatePlayerMetadata.mockReset().mockResolvedValue({})
    mockSetAdminLevel.mockReset().mockResolvedValue(undefined)
    mockSetPlayerTier.mockReset().mockResolvedValue({})
    mockGetVipTiers.mockReset().mockResolvedValue(['VIP', 'VIP+'])
    mockGetMetadata.mockReset().mockReturnValue(undefined)
  })

  it('loads available tiers when opened', async () => {
    const wrapper = mountDialog()
    await wrapper.setProps({ visible: true })
    await flushPromises()
    expect(mockGetVipTiers).toHaveBeenCalled()
  })

  it('seeds the tier from cached metadata', async () => {
    mockGetMetadata.mockReturnValue({ playerId: 'EOS_abc', nameColor: null, customTag: null, notes: null, vipTier: 'VIP+', updatedAt: '' })
    const wrapper = mountDialog()
    await wrapper.setProps({ visible: true })
    await flushPromises()
    // Save and assert the seeded tier is sent back.
    await wrapper.find('button:last-of-type').trigger('click')
    await flushPromises()
    expect(mockSetPlayerTier).toHaveBeenCalledWith('EOS_abc', 'VIP+')
  })

  it('saves metadata and tier together, without touching admin level for a normal player', async () => {
    const wrapper = mountDialog()
    await wrapper.setProps({ visible: true })
    await flushPromises()

    await wrapper.find('button:last-of-type').trigger('click')
    await flushPromises()

    expect(mockUpdatePlayerMetadata).toHaveBeenCalledWith('EOS_abc', expect.any(Object))
    expect(mockSetPlayerTier).toHaveBeenCalledWith('EOS_abc', null) // '' → null
    expect(mockSetAdminLevel).not.toHaveBeenCalled()
  })

  it('falls back to no tiers when the lookup fails', async () => {
    mockGetVipTiers.mockRejectedValue(new Error('no backend'))
    const wrapper = mountDialog()
    await wrapper.setProps({ visible: true })
    await flushPromises()
    // Still saves cleanly with an empty tier.
    await wrapper.find('button:last-of-type').trigger('click')
    await flushPromises()
    expect(mockSetPlayerTier).toHaveBeenCalledWith('EOS_abc', null)
  })
})
