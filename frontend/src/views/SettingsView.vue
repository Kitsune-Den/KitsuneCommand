<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { changePassword } from '@/api/auth'
import { getUsers, createUser, updateUser, resetUserPassword, deleteUser } from '@/api/users'
import { getChatCommandSettings, updateChatCommandSettings } from '@/api/settings'
import type { UserResponse, CreateUserRequest } from '@/api/users'
import type { ChatCommandSettings } from '@/types'
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
import InputNumber from 'primevue/inputnumber'
import ToggleSwitch from 'primevue/toggleswitch'
import Select from 'primevue/select'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'

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
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'New password must be at least 8 characters', life: 3000 })
    return
  }
  if (newPassword.value !== confirmNewPassword.value) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Passwords do not match', life: 3000 })
    return
  }

  changingPassword.value = true
  try {
    await changePassword(currentPassword.value, newPassword.value)
    toast.add({ severity: 'success', summary: 'Success', detail: 'Password changed successfully', life: 3000 })
    currentPassword.value = ''
    newPassword.value = ''
    confirmNewPassword.value = ''
  } catch (err: any) {
    const detail = err.response?.data?.message || 'Failed to change password'
    toast.add({ severity: 'error', summary: 'Error', detail, life: 3000 })
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

const roleOptions = [
  { label: 'Admin', value: 'admin' },
  { label: 'Moderator', value: 'moderator' },
  { label: 'Viewer', value: 'viewer' },
]

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
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load users', life: 3000 })
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
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Username and password are required', life: 3000 })
    return
  }
  if (newUser.value.password.length < 8) {
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Password must be at least 8 characters', life: 3000 })
    return
  }

  creatingUser.value = true
  try {
    await createUser(newUser.value)
    toast.add({ severity: 'success', summary: 'Created', detail: 'User created successfully', life: 3000 })
    showCreateDialog.value = false
    await fetchUsers()
  } catch (err: any) {
    const detail = err.response?.data?.message || 'Failed to create user'
    toast.add({ severity: 'error', summary: 'Error', detail, life: 3000 })
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
    toast.add({ severity: 'success', summary: 'Updated', detail: 'User updated successfully', life: 3000 })
    showEditDialog.value = false
    await fetchUsers()
  } catch (err: any) {
    const detail = err.response?.data?.message || 'Failed to update user'
    toast.add({ severity: 'error', summary: 'Error', detail, life: 3000 })
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
    toast.add({ severity: 'warn', summary: 'Validation', detail: 'Password must be at least 8 characters', life: 3000 })
    return
  }

  resettingPassword.value = true
  try {
    await resetUserPassword(resetTarget.value.id, resetNewPassword.value)
    toast.add({ severity: 'success', summary: 'Reset', detail: `Password reset for ${resetTarget.value.username}`, life: 3000 })
    showResetDialog.value = false
  } catch (err: any) {
    const detail = err.response?.data?.message || 'Failed to reset password'
    toast.add({ severity: 'error', summary: 'Error', detail, life: 3000 })
  } finally {
    resettingPassword.value = false
  }
}

function confirmDeactivate(user: UserResponse) {
  confirmDialog.require({
    message: `Deactivate ${user.username}? They will no longer be able to log in.`,
    header: 'Confirm Deactivation',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await deleteUser(user.id)
        toast.add({ severity: 'success', summary: 'Deactivated', detail: `${user.username} has been deactivated`, life: 3000 })
        await fetchUsers()
      } catch (err: any) {
        const detail = err.response?.data?.message || 'Failed to deactivate user'
        toast.add({ severity: 'error', summary: 'Error', detail, life: 3000 })
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
})
const loadingChatCmd = ref(false)
const savingChatCmd = ref(false)

async function fetchChatCommandSettings() {
  loadingChatCmd.value = true
  try {
    chatCmdSettings.value = await getChatCommandSettings()
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to load chat command settings', life: 3000 })
  } finally {
    loadingChatCmd.value = false
  }
}

async function handleSaveChatCommands() {
  savingChatCmd.value = true
  try {
    await updateChatCommandSettings(chatCmdSettings.value)
    toast.add({ severity: 'success', summary: 'Saved', detail: 'Chat command settings updated', life: 3000 })
  } catch (err: any) {
    const detail = err.response?.data?.message || 'Failed to save settings'
    toast.add({ severity: 'error', summary: 'Error', detail, life: 3000 })
  } finally {
    savingChatCmd.value = false
  }
}

onMounted(() => {
  if (isAdmin.value) {
    fetchUsers()
    fetchChatCommandSettings()
  }
})
</script>

<template>
  <div class="settings-view">
    <h1 class="page-title">Settings</h1>

    <Tabs value="0">
      <TabList>
        <Tab value="0">Account</Tab>
        <Tab v-if="isAdmin" value="1">Users</Tab>
        <Tab v-if="isAdmin" value="2">Chat Commands</Tab>
      </TabList>
      <TabPanels>
        <!-- Account Tab -->
        <TabPanel value="0">
          <Card class="settings-card">
            <template #title>Change Password</template>
            <template #content>
              <div class="form-group">
                <label class="form-label">Current Password</label>
                <InputText v-model="currentPassword" type="password" class="form-input" />
              </div>
              <div class="form-group">
                <label class="form-label">New Password</label>
                <InputText v-model="newPassword" type="password" class="form-input" placeholder="Minimum 8 characters" />
              </div>
              <div class="form-group">
                <label class="form-label">Confirm New Password</label>
                <InputText v-model="confirmNewPassword" type="password" class="form-input" @keydown.enter="handleChangePassword" />
              </div>
              <Button
                label="Change Password"
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
            <Button label="Create User" icon="pi pi-plus" severity="info" @click="openCreateDialog" />
            <Button icon="pi pi-refresh" text severity="secondary" @click="fetchUsers" :loading="loadingUsers" />
          </div>

          <DataTable :value="users" :loading="loadingUsers" stripedRows class="users-table">
            <Column field="username" header="Username" sortable />
            <Column field="displayName" header="Display Name" sortable />
            <Column field="role" header="Role" sortable style="width: 120px">
              <template #body="{ data }">
                <Tag :value="data.role" :severity="roleSeverity(data.role) as any" />
              </template>
            </Column>
            <Column header="Status" style="width: 100px">
              <template #body="{ data }">
                <Tag :value="data.isActive ? 'Active' : 'Inactive'" :severity="data.isActive ? 'success' : 'secondary'" />
              </template>
            </Column>
            <Column field="lastLoginAt" header="Last Login" style="width: 160px">
              <template #body="{ data }">
                <span class="text-secondary">{{ data.lastLoginAt || 'Never' }}</span>
              </template>
            </Column>
            <Column header="Actions" style="width: 180px">
              <template #body="{ data }">
                <div class="action-buttons">
                  <Button icon="pi pi-pencil" text severity="info" size="small" @click="openEditDialog(data)" title="Edit" />
                  <Button icon="pi pi-key" text severity="warn" size="small" @click="openResetDialog(data)" title="Reset Password" />
                  <Button
                    v-if="data.isActive"
                    icon="pi pi-ban"
                    text
                    severity="danger"
                    size="small"
                    @click="confirmDeactivate(data)"
                    title="Deactivate"
                  />
                </div>
              </template>
            </Column>
            <template #empty>
              <div class="empty-state">
                <p>No users found</p>
              </div>
            </template>
          </DataTable>
        </TabPanel>

        <!-- Chat Commands Tab (admin only) -->
        <TabPanel v-if="isAdmin" value="2">
          <div v-if="loadingChatCmd" class="loading-state">Loading settings...</div>
          <div v-else class="chat-cmd-settings">
            <!-- Master Toggle -->
            <Card class="settings-card">
              <template #title>General</template>
              <template #content>
                <div class="toggle-row">
                  <label>Enable Chat Commands</label>
                  <ToggleSwitch v-model="chatCmdSettings.enabled" />
                </div>
                <div class="form-group">
                  <label class="form-label">Command Prefix</label>
                  <InputText v-model="chatCmdSettings.prefix" class="form-input prefix-input" />
                </div>
                <div class="form-group">
                  <label class="form-label">Default Cooldown (seconds)</label>
                  <InputNumber v-model="chatCmdSettings.defaultCooldownSeconds" :min="0" :max="3600" class="form-input" />
                </div>
              </template>
            </Card>

            <!-- Home Commands -->
            <Card class="settings-card">
              <template #title>Home Commands</template>
              <template #subtitle>/home, /sethome, /delhome, /homes</template>
              <template #content>
                <div class="toggle-row">
                  <label>Enabled</label>
                  <ToggleSwitch v-model="chatCmdSettings.homeEnabled" />
                </div>
                <div class="form-row">
                  <div class="form-group">
                    <label class="form-label">Max Homes Per Player</label>
                    <InputNumber v-model="chatCmdSettings.maxHomesPerPlayer" :min="1" :max="50" class="form-input" />
                  </div>
                  <div class="form-group">
                    <label class="form-label">Cooldown (seconds)</label>
                    <InputNumber v-model="chatCmdSettings.homeCooldownSeconds" :min="0" :max="3600" class="form-input" />
                  </div>
                </div>
              </template>
            </Card>

            <!-- Teleport Commands -->
            <Card class="settings-card">
              <template #title>Teleport Commands</template>
              <template #subtitle>/tp, /cities</template>
              <template #content>
                <div class="toggle-row">
                  <label>Enabled</label>
                  <ToggleSwitch v-model="chatCmdSettings.teleportEnabled" />
                </div>
                <div class="form-group">
                  <label class="form-label">Cooldown (seconds)</label>
                  <InputNumber v-model="chatCmdSettings.teleportCooldownSeconds" :min="0" :max="3600" class="form-input" />
                </div>
              </template>
            </Card>

            <!-- Points Commands -->
            <Card class="settings-card">
              <template #title>Points Commands</template>
              <template #subtitle>/points, /signin</template>
              <template #content>
                <div class="toggle-row">
                  <label>Enabled</label>
                  <ToggleSwitch v-model="chatCmdSettings.pointsEnabled" />
                </div>
              </template>
            </Card>

            <!-- Store Commands -->
            <Card class="settings-card">
              <template #title>Store Commands</template>
              <template #subtitle>/shop, /buy</template>
              <template #content>
                <div class="toggle-row">
                  <label>Enabled</label>
                  <ToggleSwitch v-model="chatCmdSettings.storeEnabled" />
                </div>
              </template>
            </Card>

            <Button
              label="Save Settings"
              icon="pi pi-save"
              @click="handleSaveChatCommands"
              :loading="savingChatCmd"
              severity="info"
              class="save-btn"
            />
          </div>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- Create User Dialog -->
    <Dialog v-model:visible="showCreateDialog" header="Create User" :modal="true" :style="{ width: '400px' }">
      <div class="dialog-form">
        <div class="form-group">
          <label class="form-label">Username</label>
          <InputText v-model="newUser.username" class="form-input" placeholder="lowercase, no spaces" />
        </div>
        <div class="form-group">
          <label class="form-label">Password</label>
          <InputText v-model="newUser.password" type="password" class="form-input" placeholder="Minimum 8 characters" />
        </div>
        <div class="form-group">
          <label class="form-label">Display Name</label>
          <InputText v-model="newUser.displayName" class="form-input" placeholder="Optional" />
        </div>
        <div class="form-group">
          <label class="form-label">Role</label>
          <Select v-model="newUser.role" :options="roleOptions" optionLabel="label" optionValue="value" class="form-input" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="showCreateDialog = false" />
        <Button label="Create" icon="pi pi-check" @click="handleCreateUser" :loading="creatingUser" severity="info" />
      </template>
    </Dialog>

    <!-- Edit User Dialog -->
    <Dialog v-model:visible="showEditDialog" header="Edit User" :modal="true" :style="{ width: '400px' }">
      <div class="dialog-form" v-if="editingUser">
        <div class="form-group">
          <label class="form-label">Username</label>
          <InputText :modelValue="editingUser.username" disabled class="form-input" />
        </div>
        <div class="form-group">
          <label class="form-label">Display Name</label>
          <InputText v-model="editForm.displayName" class="form-input" />
        </div>
        <div class="form-group">
          <label class="form-label">Role</label>
          <Select v-model="editForm.role" :options="roleOptions" optionLabel="label" optionValue="value" class="form-input" />
        </div>
        <div class="form-group">
          <label class="form-label">Active</label>
          <Select
            v-model="editForm.isActive"
            :options="[{ label: 'Active', value: true }, { label: 'Inactive', value: false }]"
            optionLabel="label"
            optionValue="value"
            class="form-input"
          />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="showEditDialog = false" />
        <Button label="Save" icon="pi pi-check" @click="handleUpdateUser" :loading="savingUser" severity="info" />
      </template>
    </Dialog>

    <!-- Reset Password Dialog -->
    <Dialog v-model:visible="showResetDialog" header="Reset Password" :modal="true" :style="{ width: '400px' }">
      <div class="dialog-form" v-if="resetTarget">
        <p class="reset-info">Resetting password for <strong>{{ resetTarget.username }}</strong></p>
        <div class="form-group">
          <label class="form-label">New Password</label>
          <InputText v-model="resetNewPassword" type="password" class="form-input" placeholder="Minimum 8 characters" @keydown.enter="handleResetPassword" />
        </div>
      </div>
      <template #footer>
        <Button label="Cancel" text severity="secondary" @click="showResetDialog = false" />
        <Button label="Reset Password" icon="pi pi-key" @click="handleResetPassword" :loading="resettingPassword" severity="warn" />
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

.loading-state {
  padding: 2rem;
  text-align: center;
  color: var(--kc-text-secondary);
}

@media (max-width: 768px) {
  .settings-card { max-width: none; }
  .chat-cmd-settings { max-width: none; }
  .form-row { flex-direction: column; gap: 0; }
}
</style>
