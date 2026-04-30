<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { changePassword } from '@/api/auth'
import { getUsers, createUser, updateUser, resetUserPassword, deleteUser } from '@/api/users'
import {
  getChatCommandSettings, updateChatCommandSettings,
  getPointsSettings, updatePointsSettings,
  getTeleportSettings, updateTeleportSettings,
  getStoreSettings, updateStoreSettings,
} from '@/api/settings'
import { getVoteSettings, updateVoteSettings } from '@/api/bloodmoonvote'
import { getVoteRewardsSettings, updateVoteRewardsSettings, getVoteGrants } from '@/api/voterewards'
import { getTicketSettings, updateTicketSettings } from '@/api/tickets'
import { getDiscordSettings, updateDiscordSettings, getDiscordStatus, testDiscordConnection } from '@/api/discord'
import { restartServer } from '@/api/serverControl'
import type { UserResponse, CreateUserRequest } from '@/api/users'
import type { ChatCommandSettings, PointsSettings, TeleportSettings, StoreSettings, BloodMoonVoteSettings, TicketSettings, DiscordSettings, DiscordStatus, VoteRewardsSettings, VoteGrant } from '@/types'
import { VOTE_REWARD_TYPE } from '@/types'
import { usePermissions } from '@/composables/usePermissions'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import Card from 'primevue/card'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import InputNumber from 'primevue/inputnumber'
import ToggleSwitch from 'primevue/toggleswitch'
import Select from 'primevue/select'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'

const { t } = useI18n()
const toast = useToast()
const confirmDialog = useConfirm()
const { isAdmin } = usePermissions()

// ---- Account Tab ----
const currentPassword = ref('')
const newPassword = ref('')
const confirmNewPassword = ref('')
const changingPassword = ref(false)

async function handleChangePassword() {
  if (!newPassword.value || newPassword.value.length < 8) {
    toast.add({ severity: 'warn', summary: t('common.validation'), detail: t('settings.passwordMinLength'), life: 3000 })
    return
  }
  if (newPassword.value !== confirmNewPassword.value) {
    toast.add({ severity: 'warn', summary: t('common.validation'), detail: t('settings.passwordsDoNotMatch'), life: 3000 })
    return
  }

  changingPassword.value = true
  try {
    await changePassword(currentPassword.value, newPassword.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.passwordChanged'), life: 3000 })
    currentPassword.value = ''
    newPassword.value = ''
    confirmNewPassword.value = ''
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToChangePassword')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    changingPassword.value = false
  }
}

// ---- Users Tab (admin only) ----
const users = ref<UserResponse[]>([])
const loadingUsers = ref(false)

// Create user dialog
const showCreateDialog = ref(false)
const newUser = ref<CreateUserRequest>({ username: '', password: '', displayName: '', role: 'viewer' })
const creatingUser = ref(false)

// Edit user dialog
const showEditDialog = ref(false)
const editingUser = ref<UserResponse | null>(null)
const editForm = ref({ displayName: '', role: '', isActive: true })
const savingUser = ref(false)

// Reset password dialog
const showResetDialog = ref(false)
const resetTarget = ref<UserResponse | null>(null)
const resetNewPassword = ref('')
const resettingPassword = ref(false)

const roleOptions = computed(() => [
  { label: t('settings.admin'), value: 'admin' },
  { label: t('settings.moderator'), value: 'moderator' },
  { label: t('settings.viewer'), value: 'viewer' },
])

const activeStatusOptions = computed(() => [
  { label: t('common.active'), value: true },
  { label: t('common.inactive'), value: false },
])

function roleSeverity(role: string): string {
  switch (role) {
    case 'admin': return 'danger'
    case 'moderator': return 'warn'
    case 'viewer': return 'info'
    default: return 'secondary'
  }
}

async function fetchUsers() {
  loadingUsers.value = true
  try {
    users.value = await getUsers()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadUsers'), life: 3000 })
  } finally {
    loadingUsers.value = false
  }
}

function openCreateDialog() {
  newUser.value = { username: '', password: '', displayName: '', role: 'viewer' }
  showCreateDialog.value = true
}

async function handleCreateUser() {
  if (!newUser.value.username || !newUser.value.password) {
    toast.add({ severity: 'warn', summary: t('common.validation'), detail: t('settings.usernamePasswordRequired'), life: 3000 })
    return
  }
  if (newUser.value.password.length < 8) {
    toast.add({ severity: 'warn', summary: t('common.validation'), detail: t('settings.passwordMinChars'), life: 3000 })
    return
  }

  creatingUser.value = true
  try {
    await createUser(newUser.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.userCreated'), life: 3000 })
    showCreateDialog.value = false
    await fetchUsers()
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToCreateUser')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    creatingUser.value = false
  }
}

function openEditDialog(user: UserResponse) {
  editingUser.value = user
  editForm.value = {
    displayName: user.displayName,
    role: user.role,
    isActive: user.isActive,
  }
  showEditDialog.value = true
}

async function handleUpdateUser() {
  if (!editingUser.value) return
  savingUser.value = true
  try {
    await updateUser(editingUser.value.id, {
      displayName: editForm.value.displayName,
      role: editForm.value.role,
      isActive: editForm.value.isActive,
    })
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.userUpdated'), life: 3000 })
    showEditDialog.value = false
    await fetchUsers()
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToUpdateUser')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    savingUser.value = false
  }
}

function openResetDialog(user: UserResponse) {
  resetTarget.value = user
  resetNewPassword.value = ''
  showResetDialog.value = true
}

async function handleResetPassword() {
  if (!resetTarget.value) return
  if (!resetNewPassword.value || resetNewPassword.value.length < 8) {
    toast.add({ severity: 'warn', summary: t('common.validation'), detail: t('settings.passwordMinChars'), life: 3000 })
    return
  }

  resettingPassword.value = true
  try {
    await resetUserPassword(resetTarget.value.id, resetNewPassword.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.passwordReset', { username: resetTarget.value.username }), life: 3000 })
    showResetDialog.value = false
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToResetPassword')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    resettingPassword.value = false
  }
}

function confirmDeactivate(user: UserResponse) {
  confirmDialog.require({
    message: t('settings.deactivateMessage', { username: user.username }),
    header: t('settings.confirmDeactivation'),
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteUser(user.id)
        toast.add({ severity: 'success', summary: t('settings.deactivated'), detail: t('settings.deactivatedDetail', { username: user.username }), life: 3000 })
        await fetchUsers()
      } catch (err: any) {
        const detail = err.response?.data?.message || t('settings.failedToDeactivate')
        toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
      }
    },
  })
}

// ---- Chat Commands Tab (admin only) ----
const chatCmdSettings = ref<ChatCommandSettings>({
  enabled: true,
  prefix: '/',
  defaultCooldownSeconds: 5,
  homeEnabled: true,
  maxHomesPerPlayer: 3,
  homeCooldownSeconds: 30,
  teleportEnabled: true,
  teleportCooldownSeconds: 30,
  pointsEnabled: true,
  storeEnabled: true,
  vipEnabled: true,
  ticketEnabled: true,
  ticketCooldownSeconds: 60,
  voteEnabled: true,
  voteCooldownSeconds: 30,
})
const loadingChatCmd = ref(false)
const savingChatCmd = ref(false)

async function fetchChatCommandSettings() {
  loadingChatCmd.value = true
  try {
    chatCmdSettings.value = await getChatCommandSettings()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadChatSettings'), life: 3000 })
  } finally {
    loadingChatCmd.value = false
  }
}

async function handleSaveChatCommands() {
  savingChatCmd.value = true
  try {
    await updateChatCommandSettings(chatCmdSettings.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.chatCommandsUpdated'), life: 3000 })
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToSaveSettings')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    savingChatCmd.value = false
  }
}

// ---- Points Economy Tab (admin only) ----
const pointsSettings = ref<PointsSettings>({
  zombieKillPoints: 5,
  playerKillPoints: 10,
  signInBonus: 100,
  playtimePointsPerHour: 20,
  playtimeIntervalMinutes: 10,
})
const loadingPoints = ref(false)
const savingPoints = ref(false)

async function fetchPointsSettings() {
  loadingPoints.value = true
  try {
    pointsSettings.value = await getPointsSettings()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadPointsSettings'), life: 3000 })
  } finally {
    loadingPoints.value = false
  }
}

async function handleSavePoints() {
  savingPoints.value = true
  try {
    await updatePointsSettings(pointsSettings.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.pointsSettingsUpdated'), life: 3000 })
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToSaveSettings')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    savingPoints.value = false
  }
}

// ---- Teleport Settings Tab (admin only) ----
const teleportSettings = ref<TeleportSettings>({
  teleportDelaySeconds: 5,
  defaultPointsCost: 0,
  allowTeleportDuringBloodMoon: true,
})
const loadingTeleport = ref(false)
const savingTeleport = ref(false)

async function fetchTeleportSettings() {
  loadingTeleport.value = true
  try {
    teleportSettings.value = await getTeleportSettings()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadTeleportSettings'), life: 3000 })
  } finally {
    loadingTeleport.value = false
  }
}

async function handleSaveTeleport() {
  savingTeleport.value = true
  try {
    await updateTeleportSettings(teleportSettings.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.teleportSettingsUpdated'), life: 3000 })
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToSaveSettings')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    savingTeleport.value = false
  }
}

// ---- Store Settings Tab (admin only) ----
const storeSettings = ref<StoreSettings>({
  purchaseCooldownSeconds: 0,
  maxDailyPurchases: 0,
  priceMultiplier: 1.0,
})
const loadingStore = ref(false)
const savingStore = ref(false)

async function fetchStoreSettings() {
  loadingStore.value = true
  try {
    storeSettings.value = await getStoreSettings()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadStoreSettings'), life: 3000 })
  } finally {
    loadingStore.value = false
  }
}

async function handleSaveStore() {
  savingStore.value = true
  try {
    await updateStoreSettings(storeSettings.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.storeSettingsUpdated'), life: 3000 })
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToSaveSettings')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    savingStore.value = false
  }
}

// ---- Blood Moon Vote Tab (admin only) ----
const bmVoteSettings = ref<BloodMoonVoteSettings>({
  enabled: true,
  thresholdType: 'percentage',
  thresholdValue: 60,
  cooldownMinutes: 0,
  allowVoteHoursBefore: 2,
  allowVoteDuringBloodMoon: true,
  commandName: 'skipbm',
  voteRegisteredMessage: 'Vote registered to skip blood moon! ({current}/{required})',
  alreadyVotedMessage: 'You have already voted to skip this blood moon.',
  voteNotActiveMessage: 'No blood moon vote is active right now.',
  voteSuccessMessage: 'Vote passed! Skipping the blood moon...',
  featureDisabledMessage: 'Blood moon skip voting is disabled.',
  onCooldownMessage: 'Blood moon skip is on cooldown.',
})
const loadingBmVote = ref(false)
const savingBmVote = ref(false)

const thresholdTypeOptions = computed(() => [
  { label: t('settings.thresholdTypePercentage'), value: 'percentage' },
  { label: t('settings.thresholdTypeCount'), value: 'count' },
])

async function fetchBmVoteSettings() {
  loadingBmVote.value = true
  try {
    bmVoteSettings.value = await getVoteSettings()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadBmVoteSettings'), life: 3000 })
  } finally {
    loadingBmVote.value = false
  }
}

async function handleSaveBmVote() {
  savingBmVote.value = true
  try {
    await updateVoteSettings(bmVoteSettings.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.bloodMoonVoteUpdated'), life: 3000 })
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToSaveSettings')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    savingBmVote.value = false
  }
}

// ---- Vote Rewards Tab ----
//
// Backed by VoteRewardsController (REST) + VoteRewardsFeature (sweep + dispatch).
// The shape is "master toggle + N providers + audit log". The provider list is
// seeded by the server with one entry per registered adapter; this UI lets the
// admin enable/configure each, but doesn't add or remove rows — that's a
// compile-time concern on the backend.
const voteRewardsSettings = ref<VoteRewardsSettings>({
  enabled: false,
  providers: [],
})
const voteGrants = ref<VoteGrant[]>([])
const loadingVoteRewards = ref(false)
const savingVoteRewards = ref(false)
const loadingVoteGrants = ref(false)

const voteRewardTypeOptions = computed(() => [
  { label: t('settings.voteRewardsRewardPoints'), value: VOTE_REWARD_TYPE.POINTS },
  { label: t('settings.voteRewardsRewardVipGift'), value: VOTE_REWARD_TYPE.VIP_GIFT },
  { label: t('settings.voteRewardsRewardCdKey'), value: VOTE_REWARD_TYPE.CD_KEY },
])

// The literal placeholder text we want shown in the broadcast template input.
// Hardcoded as a const because Vue/PrimeVue doesn't have a clean attribute-level
// v-pre, and embedding `{player}` directly in the template's :placeholder string
// is fine but cleaner pulled out to a single source of truth.
const broadcastPlaceholder = "{player} voted! Thanks — here's {reward}."

/** Pretty label for the "Provider" header — falls back to the raw key. */
function voteProviderDisplayName(key: string): string {
  switch (key) {
    case '7daystodie-servers': return '7daystodie-servers.com'
    default: return key
  }
}

async function fetchVoteRewardsSettings() {
  loadingVoteRewards.value = true
  try {
    voteRewardsSettings.value = await getVoteRewardsSettings()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadVoteRewards'), life: 3000 })
  } finally {
    loadingVoteRewards.value = false
  }
}

async function fetchVoteGrants() {
  loadingVoteGrants.value = true
  try {
    voteGrants.value = await getVoteGrants(50)
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadVoteGrants'), life: 3000 })
  } finally {
    loadingVoteGrants.value = false
  }
}

async function handleSaveVoteRewards() {
  savingVoteRewards.value = true
  try {
    await updateVoteRewardsSettings(voteRewardsSettings.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.voteRewardsSaved'), life: 3000 })
    // Reload — the server may have backfilled default provider rows for any
    // adapter that wasn't already in the persisted blob (e.g. after a mod
    // update that adds a new provider).
    await fetchVoteRewardsSettings()
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToSaveSettings')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    savingVoteRewards.value = false
  }
}

// ---- Tickets Tab ----
const ticketSettings = ref<TicketSettings>({
  enabled: true,
  maxOpenTicketsPerPlayer: 3,
  cooldownSeconds: 60,
  discordWebhookUrl: '',
  discordNotifyOnCreate: true,
  discordNotifyOnReply: true,
  discordNotifyOnClose: true,
})
const loadingTicketSettings = ref(false)
const savingTicketSettings = ref(false)

async function fetchTicketSettings() {
  loadingTicketSettings.value = true
  try {
    ticketSettings.value = await getTicketSettings()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadSettings'), life: 3000 })
  } finally {
    loadingTicketSettings.value = false
  }
}

async function handleSaveTicketSettings() {
  savingTicketSettings.value = true
  try {
    await updateTicketSettings(ticketSettings.value)
    toast.add({ severity: 'success', summary: t('common.success'), detail: t('settings.ticketSettingsUpdated'), life: 3000 })
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToSaveSettings')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    savingTicketSettings.value = false
  }
}

// ---- Discord Bot Tab ----
const discordSettings = ref<DiscordSettings>({
  enabled: false,
  botToken: '',
  chatBridgeEnabled: true,
  chatBridgeChannelId: '',
  eventNotificationsEnabled: true,
  eventChannelId: '',
  notifyPlayerJoin: true,
  notifyPlayerLeave: true,
  notifyServerStart: true,
  notifyServerStop: true,
  notifyBloodMoon: true,
  slashCommandsEnabled: true,
  serverName: '7 Days to Die Server',
  showPlayerCountInStatus: true,
})
const discordStatus = ref<DiscordStatus>({ isConnected: false, botUsername: '', latencyMs: -1 })
const loadingDiscordSettings = ref(false)
const savingDiscordSettings = ref(false)
const testingDiscord = ref(false)
// Flips true after a successful save so the UI can surface "restart to apply".
// Bot connects at server boot, not on config update — so config changes don't
// reach the running process until a restart. Banner auto-clears when the status
// endpoint confirms the bot is connected with fresh latency.
const discordRestartPending = ref(false)
const restartingForDiscord = ref(false)

async function fetchDiscordSettings() {
  loadingDiscordSettings.value = true
  try {
    discordSettings.value = await getDiscordSettings()
    discordStatus.value = await getDiscordStatus()
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('settings.failedToLoadSettings'), life: 3000 })
  } finally {
    loadingDiscordSettings.value = false
  }
}

async function handleSaveDiscordSettings() {
  savingDiscordSettings.value = true
  try {
    await updateDiscordSettings(discordSettings.value)
    toast.add({
      severity: 'success',
      summary: 'Saved',
      detail: 'Discord settings stored. Restart the server for the bot to pick them up.',
      life: 5000,
    })
    discordRestartPending.value = true
    // Refresh status after saving — not to clear the pending flag (that needs a
    // real restart), just to keep the Connected/Disconnected badge fresh.
    setTimeout(refreshDiscordStatus, 3000)
  } catch (err: any) {
    const detail = err.response?.data?.message || t('settings.failedToSaveSettings')
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    savingDiscordSettings.value = false
  }
}

async function handleRestartForDiscord() {
  restartingForDiscord.value = true
  try {
    await restartServer()
    toast.add({
      severity: 'info',
      summary: 'Restarting',
      detail: 'Server is restarting. Bot should be back in under a minute.',
      life: 5000,
    })
    // Clear the pending flag optimistically — the poll below will rehydrate
    // whether the bot actually came back.
    discordRestartPending.value = false
    setTimeout(refreshDiscordStatus, 45000)
  } catch (err: any) {
    const detail = err.response?.data?.message || 'Restart request failed.'
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 4000 })
  } finally {
    restartingForDiscord.value = false
  }
}

async function handleTestDiscord() {
  testingDiscord.value = true
  try {
    const msg = await testDiscordConnection()
    toast.add({ severity: 'success', summary: t('common.success'), detail: msg, life: 3000 })
  } catch (err: any) {
    const detail = err.response?.data?.message || 'Test failed.'
    toast.add({ severity: 'error', summary: t('common.error'), detail, life: 3000 })
  } finally {
    testingDiscord.value = false
  }
}

async function refreshDiscordStatus() {
  try {
    discordStatus.value = await getDiscordStatus()
  } catch { /* ignore */ }
}

onMounted(() => {
  if (isAdmin.value) {
    fetchUsers()
    fetchChatCommandSettings()
    fetchPointsSettings()
    fetchTeleportSettings()
    fetchStoreSettings()
    fetchBmVoteSettings()
    fetchTicketSettings()
    fetchDiscordSettings()
    fetchVoteRewardsSettings()
    fetchVoteGrants()
  }
})
</script>

<template>
  <div class="settings-view">
    <h1 class="page-title">{{ t('settings.title') }}</h1>

    <Tabs value="0">
      <TabList>
        <Tab value="0">{{ t('settings.account') }}</Tab>
        <Tab v-if="isAdmin" value="1">{{ t('settings.users') }}</Tab>
        <Tab v-if="isAdmin" value="2">{{ t('settings.chatCommands') }}</Tab>
        <Tab v-if="isAdmin" value="3">{{ t('settings.pointsEconomy') }}</Tab>
        <Tab v-if="isAdmin" value="4">{{ t('settings.teleport') }}</Tab>
        <Tab v-if="isAdmin" value="5">{{ t('settings.store') }}</Tab>
        <Tab v-if="isAdmin" value="6">{{ t('settings.bloodMoonVote') }}</Tab>
        <Tab v-if="isAdmin" value="7">{{ t('settings.tickets') }}</Tab>
        <Tab v-if="isAdmin" value="8">Discord</Tab>
        <Tab v-if="isAdmin" value="9">{{ t('settings.voteRewards') }}</Tab>
      </TabList>
      <TabPanels>
        <!-- Account Tab -->
        <TabPanel value="0">
          <Card class="settings-card">
            <template #title>{{ t('settings.changePassword') }}</template>
            <template #content>
              <div class="form-group">
                <label class="form-label">{{ t('settings.currentPassword') }}</label>
                <InputText v-model="currentPassword" type="password" class="form-input" />
              </div>
              <div class="form-group">
                <label class="form-label">{{ t('settings.newPassword') }}</label>
                <InputText v-model="newPassword" type="password" class="form-input" :placeholder="t('settings.newPasswordPlaceholder')" />
              </div>
              <div class="form-group">
                <label class="form-label">{{ t('settings.confirmPassword') }}</label>
                <InputText v-model="confirmNewPassword" type="password" class="form-input" @keydown.enter="handleChangePassword" />
              </div>
              <Button
                :label="t('settings.changePasswordButton')"
                icon="pi pi-lock"
                @click="handleChangePassword"
                :loading="changingPassword"
                :disabled="!currentPassword || !newPassword || !confirmNewPassword"
                severity="info"
              />
            </template>
          </Card>
        </TabPanel>

        <!-- Users Tab (admin only) -->
        <TabPanel v-if="isAdmin" value="1">
          <div class="users-toolbar">
            <Button :label="t('settings.createUser')" icon="pi pi-plus" severity="info" @click="openCreateDialog" />
            <Button icon="pi pi-refresh" text severity="secondary" @click="fetchUsers" :loading="loadingUsers" />
          </div>

          <DataTable :value="users" :loading="loadingUsers" stripedRows class="users-table">
            <Column field="username" :header="t('settings.usernameCol')" sortable />
            <Column field="displayName" :header="t('settings.displayNameCol')" sortable />
            <Column field="role" :header="t('settings.roleCol')" sortable style="width: 120px">
              <template #body="{ data }">
                <Tag :value="data.role" :severity="roleSeverity(data.role) as any" />
              </template>
            </Column>
            <Column :header="t('settings.statusCol')" style="width: 100px">
              <template #body="{ data }">
                <Tag :value="data.isActive ? t('common.active') : t('common.inactive')" :severity="data.isActive ? 'success' : 'secondary'" />
              </template>
            </Column>
            <Column field="lastLoginAt" :header="t('settings.lastLoginCol')" style="width: 160px">
              <template #body="{ data }">
                <span class="text-secondary">{{ data.lastLoginAt || t('common.never') }}</span>
              </template>
            </Column>
            <Column :header="t('settings.actionsCol')" style="width: 180px">
              <template #body="{ data }">
                <div class="action-buttons">
                  <Button icon="pi pi-pencil" text severity="info" size="small" @click="openEditDialog(data)" :title="t('common.edit')" />
                  <Button icon="pi pi-key" text severity="warn" size="small" @click="openResetDialog(data)" :title="t('settings.resetPassword')" />
                  <Button
                    v-if="data.isActive"
                    icon="pi pi-ban"
                    text
                    severity="danger"
                    size="small"
                    @click="confirmDeactivate(data)"
                    :title="t('settings.deactivated')"
                  />
                </div>
              </template>
            </Column>
            <template #empty>
              <div class="empty-state">
                <p>{{ t('settings.noUsersFound') }}</p>
              </div>
            </template>
          </DataTable>
        </TabPanel>

        <!-- Chat Commands Tab (admin only) -->
        <TabPanel v-if="isAdmin" value="2">
          <div v-if="loadingChatCmd" class="loading-state">{{ t('settings.loadingSettings') }}</div>
          <div v-else class="chat-cmd-settings">
            <!-- Master Toggle -->
            <Card class="settings-card">
              <template #title>{{ t('settings.general') }}</template>
              <template #content>
                <div class="toggle-row">
                  <label>{{ t('settings.enableChatCommands') }}</label>
                  <ToggleSwitch v-model="chatCmdSettings.enabled" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.commandPrefix') }}</label>
                  <InputText v-model="chatCmdSettings.prefix" class="form-input prefix-input" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.defaultCooldown') }}</label>
                  <InputNumber v-model="chatCmdSettings.defaultCooldownSeconds" :min="0" :max="3600" class="form-input" />
                </div>
              </template>
            </Card>

            <!-- Home Commands -->
            <Card class="settings-card">
              <template #title>{{ t('settings.homeCommands') }}</template>
              <template #subtitle>{{ t('settings.homeCommandsSubtitle') }}</template>
              <template #content>
                <div class="toggle-row">
                  <label>{{ t('common.enabled') }}</label>
                  <ToggleSwitch v-model="chatCmdSettings.homeEnabled" />
                </div>
                <div class="form-row">
                  <div class="form-group">
                    <label class="form-label">{{ t('settings.maxHomesPerPlayer') }}</label>
                    <InputNumber v-model="chatCmdSettings.maxHomesPerPlayer" :min="1" :max="50" class="form-input" />
                  </div>
                  <div class="form-group">
                    <label class="form-label">{{ t('settings.cooldownSeconds') }}</label>
                    <InputNumber v-model="chatCmdSettings.homeCooldownSeconds" :min="0" :max="3600" class="form-input" />
                  </div>
                </div>
              </template>
            </Card>

            <!-- Teleport Commands -->
            <Card class="settings-card">
              <template #title>{{ t('settings.teleportCommands') }}</template>
              <template #subtitle>{{ t('settings.teleportCommandsSubtitle') }}</template>
              <template #content>
                <div class="toggle-row">
                  <label>{{ t('common.enabled') }}</label>
                  <ToggleSwitch v-model="chatCmdSettings.teleportEnabled" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.cooldownSeconds') }}</label>
                  <InputNumber v-model="chatCmdSettings.teleportCooldownSeconds" :min="0" :max="3600" class="form-input" />
                </div>
              </template>
            </Card>

            <!-- Points Commands -->
            <Card class="settings-card">
              <template #title>{{ t('settings.pointsCommands') }}</template>
              <template #subtitle>{{ t('settings.pointsCommandsSubtitle') }}</template>
              <template #content>
                <div class="toggle-row">
                  <label>{{ t('common.enabled') }}</label>
                  <ToggleSwitch v-model="chatCmdSettings.pointsEnabled" />
                </div>
              </template>
            </Card>

            <!-- Store Commands -->
            <Card class="settings-card">
              <template #title>{{ t('settings.storeCommands') }}</template>
              <template #subtitle>{{ t('settings.storeCommandsSubtitle') }}</template>
              <template #content>
                <div class="toggle-row">
                  <label>{{ t('common.enabled') }}</label>
                  <ToggleSwitch v-model="chatCmdSettings.storeEnabled" />
                </div>
              </template>
            </Card>

            <!-- VIP Commands -->
            <Card class="settings-card">
              <template #title>{{ t('settings.vipCommands') }}</template>
              <template #subtitle>{{ t('settings.vipCommandsSubtitle') }}</template>
              <template #content>
                <div class="toggle-row">
                  <label>{{ t('common.enabled') }}</label>
                  <ToggleSwitch v-model="chatCmdSettings.vipEnabled" />
                </div>
              </template>
            </Card>

            <Button
              :label="t('settings.saveSettings')"
              icon="pi pi-save"
              @click="handleSaveChatCommands"
              :loading="savingChatCmd"
              severity="info"
              class="save-btn"
            />
          </div>
        </TabPanel>

        <!-- Points Economy Tab (admin only) -->
        <TabPanel v-if="isAdmin" value="3">
          <div v-if="loadingPoints" class="loading-state">{{ t('settings.loadingSettings') }}</div>
          <div v-else class="chat-cmd-settings">
            <!-- Kill Points -->
            <Card class="settings-card">
              <template #title>{{ t('settings.killRewards') }}</template>
              <template #subtitle>{{ t('settings.killRewardsSubtitle') }}</template>
              <template #content>
                <div class="form-row">
                  <div class="form-group">
                    <label class="form-label">{{ t('settings.zombieKillPoints') }}</label>
                    <InputNumber v-model="pointsSettings.zombieKillPoints" :min="0" :max="10000" class="form-input" />
                  </div>
                  <div class="form-group">
                    <label class="form-label">{{ t('settings.playerKillPointsPvP') }}</label>
                    <InputNumber v-model="pointsSettings.playerKillPoints" :min="0" :max="10000" class="form-input" />
                  </div>
                </div>
              </template>
            </Card>

            <!-- Sign-In Bonus -->
            <Card class="settings-card">
              <template #title>{{ t('settings.dailySignInBonus') }}</template>
              <template #subtitle>{{ t('settings.signInBonusSubtitle') }}</template>
              <template #content>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.signInBonusPoints') }}</label>
                  <InputNumber v-model="pointsSettings.signInBonus" :min="0" :max="100000" class="form-input" />
                </div>
                <small class="settings-hint">{{ t('settings.signInBonusHint') }}</small>
              </template>
            </Card>

            <!-- Playtime Rewards -->
            <Card class="settings-card">
              <template #title>{{ t('settings.playtimeRewards') }}</template>
              <template #subtitle>{{ t('settings.playtimeRewardsSubtitle') }}</template>
              <template #content>
                <div class="form-row">
                  <div class="form-group">
                    <label class="form-label">{{ t('settings.pointsPerHour') }}</label>
                    <InputNumber v-model="pointsSettings.playtimePointsPerHour" :min="0" :max="10000" class="form-input" />
                  </div>
                  <div class="form-group">
                    <label class="form-label">{{ t('settings.checkInterval') }}</label>
                    <InputNumber v-model="pointsSettings.playtimeIntervalMinutes" :min="1" :max="1440" class="form-input" />
                  </div>
                </div>
                <small class="settings-hint">{{ t('settings.playtimeHint') }}</small>
              </template>
            </Card>

            <Button
              :label="t('settings.saveSettings')"
              icon="pi pi-save"
              @click="handleSavePoints"
              :loading="savingPoints"
              severity="info"
              class="save-btn"
            />
          </div>
        </TabPanel>

        <!-- Teleport Settings Tab (admin only) -->
        <TabPanel v-if="isAdmin" value="4">
          <div v-if="loadingTeleport" class="loading-state">{{ t('settings.loadingSettings') }}</div>
          <div v-else class="chat-cmd-settings">
            <!-- Teleport Behavior -->
            <Card class="settings-card">
              <template #title>{{ t('settings.teleportBehavior') }}</template>
              <template #subtitle>{{ t('settings.teleportBehaviorSubtitle') }}</template>
              <template #content>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.teleportDelayLabel') }}</label>
                  <InputNumber v-model="teleportSettings.teleportDelaySeconds" :min="0" :max="60" class="form-input" />
                </div>
                <small class="settings-hint">{{ t('settings.teleportDelayHint') }}</small>
              </template>
            </Card>

            <!-- City Waypoints -->
            <Card class="settings-card">
              <template #title>{{ t('settings.cityWaypoints') }}</template>
              <template #subtitle>{{ t('settings.cityWaypointsSubtitle') }}</template>
              <template #content>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.defaultPointsCost') }}</label>
                  <InputNumber v-model="teleportSettings.defaultPointsCost" :min="0" :max="100000" class="form-input" />
                </div>
                <small class="settings-hint">{{ t('settings.defaultPointsCostHint') }}</small>
              </template>
            </Card>

            <!-- Blood Moon -->
            <Card class="settings-card">
              <template #title>{{ t('settings.bloodMoonRestrictions') }}</template>
              <template #subtitle>{{ t('settings.bloodMoonRestrictionsSubtitle') }}</template>
              <template #content>
                <div class="toggle-row">
                  <label>{{ t('settings.allowTeleportDuringBloodMoon') }}</label>
                  <ToggleSwitch v-model="teleportSettings.allowTeleportDuringBloodMoon" />
                </div>
                <small class="settings-hint">{{ t('settings.bloodMoonHint') }}</small>
              </template>
            </Card>

            <Button
              :label="t('settings.saveSettings')"
              icon="pi pi-save"
              @click="handleSaveTeleport"
              :loading="savingTeleport"
              severity="info"
              class="save-btn"
            />
          </div>
        </TabPanel>

        <!-- Store Settings Tab (admin only) -->
        <TabPanel v-if="isAdmin" value="5">
          <div v-if="loadingStore" class="loading-state">{{ t('settings.loadingSettings') }}</div>
          <div v-else class="chat-cmd-settings">
            <!-- Purchase Limits -->
            <Card class="settings-card">
              <template #title>{{ t('settings.purchaseLimits') }}</template>
              <template #subtitle>{{ t('settings.purchaseLimitsSubtitle') }}</template>
              <template #content>
                <div class="form-row">
                  <div class="form-group">
                    <label class="form-label">{{ t('settings.purchaseCooldown') }}</label>
                    <InputNumber v-model="storeSettings.purchaseCooldownSeconds" :min="0" :max="86400" class="form-input" />
                  </div>
                  <div class="form-group">
                    <label class="form-label">{{ t('settings.maxDailyPurchases') }}</label>
                    <InputNumber v-model="storeSettings.maxDailyPurchases" :min="0" :max="1000" class="form-input" />
                  </div>
                </div>
                <small class="settings-hint">{{ t('settings.purchaseLimitsHint') }}</small>
              </template>
            </Card>

            <!-- Pricing -->
            <Card class="settings-card">
              <template #title>{{ t('settings.pricing') }}</template>
              <template #subtitle>{{ t('settings.pricingSubtitle') }}</template>
              <template #content>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.priceMultiplier') }}</label>
                  <InputNumber v-model="storeSettings.priceMultiplier" :min="0.1" :max="10" :minFractionDigits="1" :maxFractionDigits="2" :step="0.1" class="form-input" />
                </div>
                <small class="settings-hint">{{ t('settings.priceMultiplierHint') }}</small>
              </template>
            </Card>

            <Button
              :label="t('settings.saveSettings')"
              icon="pi pi-save"
              @click="handleSaveStore"
              :loading="savingStore"
              severity="info"
              class="save-btn"
            />
          </div>
        </TabPanel>

        <!-- Blood Moon Vote Tab (admin only) -->
        <TabPanel v-if="isAdmin" value="6">
          <div v-if="loadingBmVote" class="loading-state">{{ t('settings.loadingSettings') }}</div>
          <div v-else class="chat-cmd-settings">
            <!-- General -->
            <Card class="settings-card">
              <template #title>{{ t('settings.general') }}</template>
              <template #subtitle>{{ t('settings.bloodMoonVoteSubtitle') }}</template>
              <template #content>
                <div class="toggle-row">
                  <label>{{ t('settings.enableBloodMoonVote') }}</label>
                  <ToggleSwitch v-model="bmVoteSettings.enabled" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.commandNameLabel') }}</label>
                  <InputText v-model="bmVoteSettings.commandName" class="form-input prefix-input" />
                </div>
              </template>
            </Card>

            <!-- Threshold -->
            <Card class="settings-card">
              <template #title>{{ t('settings.thresholdType') }}</template>
              <template #content>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.thresholdType') }}</label>
                  <Select v-model="bmVoteSettings.thresholdType" :options="thresholdTypeOptions" optionLabel="label" optionValue="value" class="form-input" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.thresholdValue') }}</label>
                  <InputNumber v-model="bmVoteSettings.thresholdValue" :min="1" :max="bmVoteSettings.thresholdType === 'percentage' ? 100 : 999" class="form-input" />
                </div>
                <small class="settings-hint">{{ t('settings.thresholdValueHint') }}</small>
              </template>
            </Card>

            <!-- Timing -->
            <Card class="settings-card">
              <template #title>{{ t('settings.voteTimingTitle') }}</template>
              <template #subtitle>{{ t('settings.voteTimingSubtitle') }}</template>
              <template #content>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.allowVoteHoursBefore') }}</label>
                  <InputNumber v-model="bmVoteSettings.allowVoteHoursBefore" :min="0" :max="22" class="form-input" />
                </div>
                <small class="settings-hint">{{ t('settings.allowVoteHoursBeforeHint') }}</small>
                <div class="toggle-row" style="margin-top: 1rem">
                  <label>{{ t('settings.allowVoteDuringBloodMoon') }}</label>
                  <ToggleSwitch v-model="bmVoteSettings.allowVoteDuringBloodMoon" />
                </div>
                <small class="settings-hint">{{ t('settings.allowVoteDuringBloodMoonHint') }}</small>
                <div class="form-group" style="margin-top: 1rem">
                  <label class="form-label">{{ t('settings.voteCooldownMinutes') }}</label>
                  <InputNumber v-model="bmVoteSettings.cooldownMinutes" :min="0" :max="1440" class="form-input" />
                </div>
                <small class="settings-hint">{{ t('settings.voteCooldownHint') }}</small>
              </template>
            </Card>

            <!-- Chat Messages -->
            <Card class="settings-card" style="max-width: 600px">
              <template #title>{{ t('settings.chatMessagesTitle') }}</template>
              <template #subtitle>{{ t('settings.chatMessagesSubtitle') }}</template>
              <template #content>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.voteRegisteredMsg') }}</label>
                  <InputText v-model="bmVoteSettings.voteRegisteredMessage" class="form-input" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.alreadyVotedMsg') }}</label>
                  <InputText v-model="bmVoteSettings.alreadyVotedMessage" class="form-input" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.voteNotActiveMsg') }}</label>
                  <InputText v-model="bmVoteSettings.voteNotActiveMessage" class="form-input" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.voteSuccessMsg') }}</label>
                  <InputText v-model="bmVoteSettings.voteSuccessMessage" class="form-input" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.featureDisabledMsg') }}</label>
                  <InputText v-model="bmVoteSettings.featureDisabledMessage" class="form-input" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.onCooldownMsg') }}</label>
                  <InputText v-model="bmVoteSettings.onCooldownMessage" class="form-input" />
                </div>
              </template>
            </Card>

            <Button
              :label="t('settings.saveSettings')"
              icon="pi pi-save"
              @click="handleSaveBmVote"
              :loading="savingBmVote"
              severity="info"
              class="save-btn"
            />
          </div>
        </TabPanel>

        <!-- Tickets Tab -->
        <TabPanel v-if="isAdmin" value="7">
          <div class="settings-section" v-if="!loadingTicketSettings">
            <Card class="settings-card">
              <template #title>{{ t('settings.ticketGeneral') }}</template>
              <template #subtitle>{{ t('settings.ticketGeneralSubtitle') }}</template>
              <template #content>
                <div class="form-row">
                  <label class="form-label">{{ t('settings.enabled') }}</label>
                  <ToggleSwitch v-model="ticketSettings.enabled" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.maxOpenTickets') }}</label>
                  <InputNumber v-model="ticketSettings.maxOpenTicketsPerPlayer" :min="1" :max="20" class="form-input" />
                </div>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.ticketCooldown') }}</label>
                  <InputNumber v-model="ticketSettings.cooldownSeconds" :min="0" :max="3600" class="form-input" suffix=" sec" />
                </div>
              </template>
            </Card>

            <Card class="settings-card">
              <template #title>{{ t('settings.discordIntegration') }}</template>
              <template #subtitle>{{ t('settings.discordIntegrationSubtitle') }}</template>
              <template #content>
                <div class="form-group">
                  <label class="form-label">{{ t('settings.discordWebhookUrl') }}</label>
                  <InputText v-model="ticketSettings.discordWebhookUrl" type="password" class="form-input" :placeholder="t('settings.discordWebhookPlaceholder')" />
                </div>
                <div class="form-row">
                  <label class="form-label">{{ t('settings.notifyOnCreate') }}</label>
                  <ToggleSwitch v-model="ticketSettings.discordNotifyOnCreate" />
                </div>
                <div class="form-row">
                  <label class="form-label">{{ t('settings.notifyOnReply') }}</label>
                  <ToggleSwitch v-model="ticketSettings.discordNotifyOnReply" />
                </div>
                <div class="form-row">
                  <label class="form-label">{{ t('settings.notifyOnClose') }}</label>
                  <ToggleSwitch v-model="ticketSettings.discordNotifyOnClose" />
                </div>
              </template>
            </Card>

            <Button
              :label="t('settings.saveSettings')"
              icon="pi pi-save"
              @click="handleSaveTicketSettings"
              :loading="savingTicketSettings"
              severity="info"
              class="save-btn"
            />
          </div>
        </TabPanel>
        <!-- Discord Bot Tab -->
        <TabPanel v-if="isAdmin" value="8">
          <div class="settings-section" v-if="!loadingDiscordSettings">
            <Message
              v-if="discordRestartPending"
              severity="warn"
              :closable="true"
              @close="discordRestartPending = false"
              class="restart-banner"
            >
              <div class="restart-banner-content">
                <div class="restart-banner-text">
                  <strong>Restart required.</strong>
                  The bot reads these settings at server boot — your changes
                  are saved but won't take effect until the 7D2D service
                  restarts.
                </div>
                <Button
                  label="Restart now"
                  icon="pi pi-refresh"
                  severity="warn"
                  size="small"
                  :loading="restartingForDiscord"
                  @click="handleRestartForDiscord"
                />
              </div>
            </Message>
            <div class="discord-grid">
              <!-- Left column: Connection + Display -->
              <div class="discord-col">
                <Card class="settings-card discord-card">
                  <template #title>Connection</template>
                  <template #subtitle>Configure the Discord bot connection</template>
                  <template #content>
                    <div class="toggle-row">
                      <label>Enabled</label>
                      <ToggleSwitch v-model="discordSettings.enabled" />
                    </div>
                    <div class="form-group">
                      <label class="form-label">Bot Token</label>
                      <InputText v-model="discordSettings.botToken" type="password" class="form-input" placeholder="Paste your Discord bot token" />
                    </div>
                    <div class="discord-status-row">
                      <Tag :severity="discordStatus.isConnected ? 'success' : 'danger'" :value="discordStatus.isConnected ? 'Connected' : 'Disconnected'" />
                      <span v-if="discordStatus.isConnected" class="discord-status-info">
                        {{ discordStatus.botUsername }} &middot; {{ discordStatus.latencyMs }}ms
                      </span>
                      <Button label="Refresh" icon="pi pi-refresh" severity="secondary" size="small" text @click="refreshDiscordStatus" />
                      <Button label="Test" icon="pi pi-bolt" severity="info" size="small" text :loading="testingDiscord" @click="handleTestDiscord" />
                    </div>
                  </template>
                </Card>

                <Card class="settings-card discord-card">
                  <template #title>Display</template>
                  <template #subtitle>Bot presence and embed settings</template>
                  <template #content>
                    <div class="form-group">
                      <label class="form-label">Server Name</label>
                      <InputText v-model="discordSettings.serverName" class="form-input" placeholder="Shown in embed footers" />
                    </div>
                    <div class="toggle-row">
                      <label>Show Player Count in Status</label>
                      <ToggleSwitch v-model="discordSettings.showPlayerCountInStatus" />
                    </div>
                    <div class="toggle-row">
                      <label>Slash Commands</label>
                      <ToggleSwitch v-model="discordSettings.slashCommandsEnabled" />
                    </div>
                    <small class="settings-hint">/status, /players, /time</small>
                  </template>
                </Card>
              </div>

              <!-- Right column: Chat Bridge + Events -->
              <div class="discord-col">
                <Card class="settings-card discord-card">
                  <template #title>Chat Bridge</template>
                  <template #subtitle>Bridge in-game chat with a Discord channel</template>
                  <template #content>
                    <div class="toggle-row">
                      <label>Enable Chat Bridge</label>
                      <ToggleSwitch v-model="discordSettings.chatBridgeEnabled" />
                    </div>
                    <div class="form-group">
                      <label class="form-label">Channel ID</label>
                      <InputText v-model="discordSettings.chatBridgeChannelId" class="form-input" placeholder="Right-click channel > Copy Channel ID" />
                    </div>
                  </template>
                </Card>

                <Card class="settings-card discord-card">
                  <template #title>Event Notifications</template>
                  <template #subtitle>Push server events to a Discord channel</template>
                  <template #content>
                    <div class="toggle-row">
                      <label>Enable Notifications</label>
                      <ToggleSwitch v-model="discordSettings.eventNotificationsEnabled" />
                    </div>
                    <div class="form-group">
                      <label class="form-label">Channel ID</label>
                      <InputText v-model="discordSettings.eventChannelId" class="form-input" placeholder="Right-click channel > Copy Channel ID" />
                    </div>
                    <div class="discord-events-grid">
                      <div class="toggle-row compact">
                        <label>Player Join</label>
                        <ToggleSwitch v-model="discordSettings.notifyPlayerJoin" />
                      </div>
                      <div class="toggle-row compact">
                        <label>Player Leave</label>
                        <ToggleSwitch v-model="discordSettings.notifyPlayerLeave" />
                      </div>
                      <div class="toggle-row compact">
                        <label>Server Start</label>
                        <ToggleSwitch v-model="discordSettings.notifyServerStart" />
                      </div>
                      <div class="toggle-row compact">
                        <label>Server Stop</label>
                        <ToggleSwitch v-model="discordSettings.notifyServerStop" />
                      </div>
                      <div class="toggle-row compact">
                        <label>Blood Moon</label>
                        <ToggleSwitch v-model="discordSettings.notifyBloodMoon" />
                      </div>
                    </div>
                  </template>
                </Card>
              </div>
            </div>

            <Button
              label="Save Discord Settings"
              icon="pi pi-save"
              @click="handleSaveDiscordSettings"
              :loading="savingDiscordSettings"
              severity="info"
              class="save-btn"
            />
          </div>
        </TabPanel>

        <!-- Vote Rewards Tab -->
        <TabPanel v-if="isAdmin" value="9">
          <div v-if="loadingVoteRewards" class="loading-state">{{ t('settings.loadingSettings') }}</div>
          <div v-else class="settings-section">
            <Card class="settings-card">
              <template #title>{{ t('settings.voteRewardsTitle') }}</template>
              <template #subtitle>{{ t('settings.voteRewardsSubtitle') }}</template>
              <template #content>
                <div class="toggle-row">
                  <label>{{ t('settings.voteRewardsMasterToggle') }}</label>
                  <ToggleSwitch v-model="voteRewardsSettings.enabled" />
                </div>
                <small class="settings-hint">{{ t('settings.voteRewardsMasterToggleHint') }}</small>
              </template>
            </Card>

            <Card class="settings-card" v-if="voteRewardsSettings.providers && voteRewardsSettings.providers.length > 0">
              <template #title>{{ t('settings.voteRewardsProviderHeader') }}</template>
              <template #content>
                <div v-for="(provider, idx) in voteRewardsSettings.providers" :key="provider.key" class="provider-block vote-provider">
                  <h3 class="provider-title">{{ voteProviderDisplayName(provider.key) }}</h3>

                  <div class="vote-provider-grid">
                    <div class="toggle-row vote-provider-full">
                      <label>{{ t('settings.voteRewardsProviderEnable') }}</label>
                      <ToggleSwitch v-model="voteRewardsSettings.providers[idx].enabled" />
                    </div>

                    <div class="form-group vote-provider-full">
                      <label class="form-label">{{ t('settings.voteRewardsApiKey') }}</label>
                      <InputText v-model="voteRewardsSettings.providers[idx].apiKey" type="password" class="form-input" />
                      <small class="settings-hint">{{ t('settings.voteRewardsApiKeyHint') }}</small>
                    </div>

                    <div class="form-group">
                      <label class="form-label">{{ t('settings.voteRewardsServerId') }}</label>
                      <InputText v-model="voteRewardsSettings.providers[idx].serverId" class="form-input" />
                      <small class="settings-hint">{{ t('settings.voteRewardsServerIdHint') }}</small>
                    </div>

                    <div class="form-group">
                      <label class="form-label">{{ t('settings.voteRewardsPollInterval') }}</label>
                      <InputNumber v-model="voteRewardsSettings.providers[idx].pollIntervalMinutes" :min="1" :max="1440" class="form-input" />
                      <small class="settings-hint">{{ t('settings.voteRewardsPollIntervalHint') }}</small>
                    </div>

                    <div class="form-group">
                      <label class="form-label">{{ t('settings.voteRewardsRewardType') }}</label>
                      <Select v-model="voteRewardsSettings.providers[idx].rewardType" :options="voteRewardTypeOptions" optionLabel="label" optionValue="value" class="form-input" />
                    </div>

                    <div class="form-group" v-if="provider.rewardType === 'points'">
                      <label class="form-label">{{ t('settings.voteRewardsPointsAmount') }}</label>
                      <InputNumber v-model="voteRewardsSettings.providers[idx].pointsAmount" :min="0" :max="1000000" class="form-input" />
                    </div>

                    <div class="form-group" v-if="provider.rewardType === 'vip_gift'">
                      <label class="form-label">{{ t('settings.voteRewardsVipGiftTemplate') }}</label>
                      <InputText v-model="voteRewardsSettings.providers[idx].vipGiftTemplateName" class="form-input" />
                      <small class="settings-hint">{{ t('settings.voteRewardsVipGiftTemplateHint') }}</small>
                    </div>

                    <div class="form-group" v-if="provider.rewardType === 'cd_key'">
                      <label class="form-label">{{ t('settings.voteRewardsCdKeyTemplate') }}</label>
                      <InputNumber v-model="voteRewardsSettings.providers[idx].cdKeyTemplateId" :min="0" class="form-input" disabled />
                      <small class="settings-hint">{{ t('settings.voteRewardsRewardCdKey') }}</small>
                    </div>

                    <div class="form-group vote-provider-full">
                      <label class="form-label">{{ t('settings.voteRewardsBroadcastTemplate') }}</label>
                      <!-- v-pre on the placeholder string isn't usable on attributes; pass via :placeholder bound to a literal -->
                      <InputText v-model="voteRewardsSettings.providers[idx].broadcastTemplate" class="form-input" :placeholder="broadcastPlaceholder" />
                      <!-- Token names are passed as i18n params so vue-i18n doesn't try to interpolate {player}/{reward} as named slots and substitute them with empty strings. -->
                      <small class="settings-hint">{{ t('settings.voteRewardsBroadcastHint', { playerToken: '{player}', rewardToken: '{reward}' }) }}</small>
                    </div>
                  </div>
                </div>
              </template>
            </Card>

            <Button
              :label="t('settings.saveSettings')"
              icon="pi pi-save"
              @click="handleSaveVoteRewards"
              :loading="savingVoteRewards"
              severity="info"
              class="save-btn"
            />

            <Card class="settings-card">
              <template #title>{{ t('settings.voteRewardsAuditTitle') }}</template>
              <template #subtitle>{{ t('settings.voteRewardsAuditSubtitle') }}</template>
              <template #content>
                <DataTable :value="voteGrants" :loading="loadingVoteGrants" stripedRows :emptyMessage="t('settings.voteRewardsAuditEmpty')">
                  <Column field="grantedAt" :header="t('settings.voteRewardsColTime')" style="width: 180px" />
                  <Column field="provider" :header="t('settings.voteRewardsColProvider')" style="width: 180px" />
                  <Column field="playerName" :header="t('settings.voteRewardsColPlayer')" />
                  <Column field="steamId" :header="t('settings.voteRewardsColSteamId')" style="width: 200px" />
                  <Column :header="t('settings.voteRewardsColReward')">
                    <template #body="slotProps">
                      {{ slotProps.data.rewardType }}: {{ slotProps.data.rewardValue }}
                    </template>
                  </Column>
                </DataTable>
              </template>
            </Card>
          </div>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- Create User Dialog -->
    <Dialog v-model:visible="showCreateDialog" :header="t('settings.createUser')" :modal="true" :style="{ width: '400px' }">
      <div class="dialog-form">
        <div class="form-group">
          <label class="form-label">{{ t('settings.username') }}</label>
          <InputText v-model="newUser.username" class="form-input" :placeholder="t('settings.lowercaseNoSpaces')" />
        </div>
        <div class="form-group">
          <label class="form-label">{{ t('settings.password') }}</label>
          <InputText v-model="newUser.password" type="password" class="form-input" :placeholder="t('settings.newPasswordPlaceholder')" />
        </div>
        <div class="form-group">
          <label class="form-label">{{ t('settings.displayName') }}</label>
          <InputText v-model="newUser.displayName" class="form-input" :placeholder="t('settings.optional')" />
        </div>
        <div class="form-group">
          <label class="form-label">{{ t('settings.role') }}</label>
          <Select v-model="newUser.role" :options="roleOptions" optionLabel="label" optionValue="value" class="form-input" />
        </div>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" text severity="secondary" @click="showCreateDialog = false" />
        <Button :label="t('common.create')" icon="pi pi-check" @click="handleCreateUser" :loading="creatingUser" severity="info" />
      </template>
    </Dialog>

    <!-- Edit User Dialog -->
    <Dialog v-model:visible="showEditDialog" :header="t('settings.editUser')" :modal="true" :style="{ width: '400px' }">
      <div class="dialog-form" v-if="editingUser">
        <div class="form-group">
          <label class="form-label">{{ t('settings.username') }}</label>
          <InputText :modelValue="editingUser.username" disabled class="form-input" />
        </div>
        <div class="form-group">
          <label class="form-label">{{ t('settings.displayName') }}</label>
          <InputText v-model="editForm.displayName" class="form-input" />
        </div>
        <div class="form-group">
          <label class="form-label">{{ t('settings.role') }}</label>
          <Select v-model="editForm.role" :options="roleOptions" optionLabel="label" optionValue="value" class="form-input" />
        </div>
        <div class="form-group">
          <label class="form-label">{{ t('common.status') }}</label>
          <Select
            v-model="editForm.isActive"
            :options="activeStatusOptions"
            optionLabel="label"
            optionValue="value"
            class="form-input"
          />
        </div>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" text severity="secondary" @click="showEditDialog = false" />
        <Button :label="t('common.save')" icon="pi pi-check" @click="handleUpdateUser" :loading="savingUser" severity="info" />
      </template>
    </Dialog>

    <!-- Reset Password Dialog -->
    <Dialog v-model:visible="showResetDialog" :header="t('settings.resetPassword')" :modal="true" :style="{ width: '400px' }">
      <div class="dialog-form" v-if="resetTarget">
        <p class="reset-info">{{ t('settings.resettingPasswordFor') }} <strong>{{ resetTarget.username }}</strong></p>
        <div class="form-group">
          <label class="form-label">{{ t('settings.newPassword') }}</label>
          <InputText v-model="resetNewPassword" type="password" class="form-input" :placeholder="t('settings.newPasswordPlaceholder')" @keydown.enter="handleResetPassword" />
        </div>
      </div>
      <template #footer>
        <Button :label="t('common.cancel')" text severity="secondary" @click="showResetDialog = false" />
        <Button :label="t('settings.resetPasswordButton')" icon="pi pi-key" @click="handleResetPassword" :loading="resettingPassword" severity="warn" />
      </template>
    </Dialog>
  </div>
</template>

<style scoped>
.settings-view {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.page-title {
  font-size: 1.5rem;
  font-weight: 600;
  margin-bottom: 0.5rem;
}

.settings-card {
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
  max-width: 500px;
}

.form-group {
  margin-bottom: 1rem;
}

.form-label {
  display: block;
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 0.35rem;
}

.form-input {
  width: 100%;
}

.users-toolbar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.users-table {
  margin-top: 0.5rem;
}

.action-buttons {
  display: flex;
  gap: 0.25rem;
}

.text-secondary {
  color: var(--kc-text-secondary);
  font-size: 0.85rem;
}

.empty-state {
  text-align: center;
  padding: 2rem;
  color: var(--kc-text-secondary);
}

.dialog-form {
  padding: 0.5rem 0;
}

.reset-info {
  margin-bottom: 1rem;
  color: var(--kc-text-secondary);
  font-size: 0.9rem;
}

.chat-cmd-settings {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  max-width: 600px;
}

.toggle-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 1rem;
}

.toggle-row label {
  font-weight: 500;
}

.form-row {
  display: flex;
  gap: 1rem;
}

.form-row .form-group {
  flex: 1;
}

.prefix-input {
  max-width: 80px;
}

.save-btn {
  align-self: flex-start;
}

.settings-hint {
  display: block;
  font-size: 0.8rem;
  color: var(--kc-text-secondary);
  margin-top: 0.25rem;
}

.loading-state {
  padding: 2rem;
  text-align: center;
  color: var(--kc-text-secondary);
}

.discord-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
  align-items: start;
}

.discord-col {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.restart-banner {
  margin-bottom: 1rem;
}

.restart-banner-content {
  display: flex;
  gap: 1rem;
  align-items: center;
  justify-content: space-between;
}

.restart-banner-text {
  line-height: 1.45;
  flex: 1;
}

.restart-banner-text strong {
  color: inherit;
  margin-right: 0.25rem;
}

.discord-card {
  max-width: none;
}

.discord-status-row {
  display: flex;
  gap: 0.75rem;
  align-items: center;
  flex-wrap: wrap;
}

.discord-status-info {
  color: var(--kc-text-secondary);
  font-size: 0.85rem;
}

.discord-events-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.25rem 1.5rem;
}

.toggle-row.compact {
  margin-bottom: 0.25rem;
}

.toggle-row.compact label {
  font-size: 0.85rem;
}

/* Vote Rewards: dense 2-col layout for provider config blocks. The toggle,
   API key, and broadcast template span the full row; everything else fits
   in pairs (Server ID + Poll Interval, Reward Type + reward-value field). */
.vote-provider {
  margin-bottom: 1.5rem;
  padding-bottom: 1.5rem;
  border-bottom: 1px solid var(--kc-border, rgba(255, 255, 255, 0.08));
}

.vote-provider:last-child {
  margin-bottom: 0;
  padding-bottom: 0;
  border-bottom: none;
}

.vote-provider-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.5rem 1.25rem;
  align-items: start;
}

.vote-provider-grid > .vote-provider-full {
  grid-column: 1 / -1;
}

@media (max-width: 768px) {
  .settings-card { max-width: none; }
  .chat-cmd-settings { max-width: none; }
  .form-row { flex-direction: column; gap: 0; }
  .discord-grid { grid-template-columns: 1fr; }
  .discord-events-grid { grid-template-columns: 1fr; }
  .vote-provider-grid { grid-template-columns: 1fr; }
}
</style>
