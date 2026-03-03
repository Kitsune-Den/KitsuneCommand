import { computed } from 'vue'
import { useAuthStore } from '@/stores/auth'

export function usePermissions() {
  const auth = useAuthStore()

  const isAdmin = computed(() => auth.role === 'admin')
  const isModerator = computed(() => auth.role === 'moderator')
  const isViewer = computed(() => auth.role === 'viewer')

  const canKickPlayers = computed(() => isAdmin.value || isModerator.value)
  const canBanPlayers = computed(() => isAdmin.value)
  const canExecuteCommands = computed(() => isAdmin.value)
  const canSendChat = computed(() => isAdmin.value || isModerator.value)
  const canManageUsers = computed(() => isAdmin.value)
  const canGiveItems = computed(() => isAdmin.value)
  const canTeleport = computed(() => isAdmin.value)
  const canAdjustPoints = computed(() => isAdmin.value)
  const canManageStore = computed(() => isAdmin.value)
  const canBuyFromStore = computed(() => isAdmin.value || isModerator.value)
  const canManageTeleport = computed(() => isAdmin.value)
  const canExecuteTeleport = computed(() => isAdmin.value || isModerator.value)
  const canManageCdKeys = computed(() => isAdmin.value)
  const canRedeemCdKeys = computed(() => isAdmin.value || isModerator.value)

  return {
    isAdmin,
    isModerator,
    isViewer,
    canKickPlayers,
    canBanPlayers,
    canExecuteCommands,
    canSendChat,
    canManageUsers,
    canGiveItems,
    canTeleport,
    canAdjustPoints,
    canManageStore,
    canBuyFromStore,
    canManageTeleport,
    canExecuteTeleport,
    canManageCdKeys,
    canRedeemCdKeys,
  }
}
