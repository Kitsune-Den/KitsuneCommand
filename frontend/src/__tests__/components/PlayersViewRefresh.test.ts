import { describe, it, expect, vi, beforeEach } from 'vitest'
import { shallowMount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import PlayersView from '@/views/PlayersView.vue'
import PlayerEditDialog from '@/components/PlayerEditDialog.vue'
import { usePlayersStore } from '@/stores/players'
import type { PlayerInfo } from '@/types'

vi.mock('vue-i18n', () => ({ useI18n: () => ({ t: (key: string) => key }) }))
vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRouter: () => ({ push: vi.fn() }),
}))
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: vi.fn() }) }))
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: vi.fn() }) }))
vi.mock('@/composables/usePermissions', () => ({
  usePermissions: () => ({
    isAdmin: { value: true },
    canKickPlayers: { value: true },
    canBanPlayers: { value: true },
    canGiveItems: { value: true },
    canSendChat: { value: true },
  }),
}))

const mockGetOnlinePlayers = vi.fn()
vi.mock('@/api/players', () => ({
  getOnlinePlayers: () => mockGetOnlinePlayers(),
  getKnownPlayers: vi.fn(),
  getAllPlayerMetadata: () => Promise.resolve({}),
  kickPlayer: vi.fn(),
  banPlayer: vi.fn(),
}))

function playerAt(adminLevel: number) {
  return { playerId: 'EOS_abc', playerName: 'Alice', entityId: 42, adminLevel, isAdmin: adminLevel === 0 } as PlayerInfo
}

describe('PlayersView — after the edit dialog saves', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    mockGetOnlinePlayers.mockReset()
  })

  it('reloads the players, so the dialog reopens on the new admin level', async () => {
    mockGetOnlinePlayers.mockResolvedValueOnce([playerAt(1000)]).mockResolvedValueOnce([playerAt(0)])
    const wrapper = shallowMount(PlayersView, {
      global: { directives: { tooltip: {} } },
    })
    await flushPromises()
    const store = usePlayersStore()
    expect(store.playerList[0].adminLevel).toBe(1000)

    wrapper.findComponent(PlayerEditDialog).vm.$emit('saved')
    await flushPromises()

    expect(mockGetOnlinePlayers).toHaveBeenCalledTimes(2)
    expect(store.playerList[0].adminLevel).toBe(0)
  })
})
