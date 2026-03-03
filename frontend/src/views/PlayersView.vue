<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { usePlayersStore } from '@/stores/players'
import { getOnlinePlayers, kickPlayer, banPlayer } from '@/api/players'
import { usePermissions } from '@/composables/usePermissions'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import ProgressBar from 'primevue/progressbar'
import type { PlayerInfo } from '@/types'

const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const playersStore = usePlayersStore()
const { canKickPlayers, canBanPlayers } = usePermissions()
const loading = ref(true)
const searchFilter = ref('')

const filteredPlayers = computed(() => {
  const search = searchFilter.value.toLowerCase()
  if (!search) return playersStore.playerList
  return playersStore.playerList.filter(
    (p) => p.playerName.toLowerCase().includes(search) || p.playerId?.toLowerCase().includes(search)
  )
})

async function fetchPlayers() {
  try {
    const players = await getOnlinePlayers()
    playersStore.setPlayers(players)
  } catch {
    toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to fetch players', life: 3000 })
  } finally {
    loading.value = false
  }
}

function viewPlayer(player: PlayerInfo) {
  router.push({ name: 'PlayerDetail', params: { entityId: player.entityId.toString() } })
}

function confirmKick(player: PlayerInfo) {
  confirm.require({
    message: `Kick ${player.playerName} from the server?`,
    header: 'Confirm Kick',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-warning',
    accept: async () => {
      try {
        await kickPlayer(player.entityId)
        toast.add({ severity: 'success', summary: 'Kicked', detail: `${player.playerName} has been kicked`, life: 3000 })
      } catch {
        toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to kick player', life: 3000 })
      }
    },
  })
}

function confirmBan(player: PlayerInfo) {
  confirm.require({
    message: `Ban ${player.playerName} from the server? This cannot be easily undone.`,
    header: 'Confirm Ban',
    icon: 'pi pi-ban',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await banPlayer(player.entityId)
        toast.add({ severity: 'success', summary: 'Banned', detail: `${player.playerName} has been banned`, life: 3000 })
      } catch {
        toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to ban player', life: 3000 })
      }
    },
  })
}

function formatPosition(p: PlayerInfo): string {
  return `${Math.round(p.positionX)}, ${Math.round(p.positionY)}, ${Math.round(p.positionZ)}`
}

onMounted(fetchPlayers)
</script>

<template>
  <div class="players-view">
    <div class="page-header">
      <h1 class="page-title">Players</h1>
      <Tag :value="`${playersStore.onlineCount} online`" severity="info" />
    </div>

    <div class="toolbar">
      <span class="p-input-icon-left search-wrapper">
        <i class="pi pi-search" />
        <InputText v-model="searchFilter" placeholder="Search players..." class="search-input" />
      </span>
      <Button icon="pi pi-refresh" text severity="secondary" @click="fetchPlayers" :loading="loading" />
    </div>

    <DataTable
      :value="filteredPlayers"
      :loading="loading"
      stripedRows
      sortField="playerName"
      :sortOrder="1"
      class="players-table"
      :rowHover="true"
      @row-click="(e: any) => viewPlayer(e.data)"
    >
      <Column field="playerName" header="Name" sortable>
        <template #body="{ data }">
          <div class="player-name">
            <i class="pi pi-user" />
            <span>{{ data.playerName }}</span>
            <Tag v-if="data.isAdmin" value="Admin" severity="warn" class="admin-tag" />
          </div>
        </template>
      </Column>

      <Column field="level" header="Level" sortable style="width: 80px" />

      <Column header="Health" style="width: 150px">
        <template #body="{ data }">
          <ProgressBar :value="data.health" :showValue="false" class="health-bar" style="height: 8px" />
          <span class="bar-label">{{ Math.round(data.health) }}</span>
        </template>
      </Column>

      <Column header="Position" style="width: 180px">
        <template #body="{ data }">
          <span class="position-text">{{ formatPosition(data) }}</span>
        </template>
      </Column>

      <Column field="zombieKills" header="Z Kills" sortable style="width: 80px" />
      <Column field="playerKills" header="P Kills" sortable style="width: 80px" />
      <Column field="deaths" header="Deaths" sortable style="width: 80px" />

      <Column v-if="canKickPlayers || canBanPlayers" header="Actions" style="width: 120px">
        <template #body="{ data }">
          <div class="action-buttons" @click.stop>
            <Button v-if="canKickPlayers" icon="pi pi-sign-out" text severity="warning" size="small" @click="confirmKick(data)" title="Kick" />
            <Button v-if="canBanPlayers" icon="pi pi-ban" text severity="danger" size="small" @click="confirmBan(data)" title="Ban" />
          </div>
        </template>
      </Column>

      <template #empty>
        <div class="empty-state">
          <i class="pi pi-users" style="font-size: 2rem; color: var(--kc-text-secondary)" />
          <p>No players online</p>
        </div>
      </template>
    </DataTable>
  </div>
</template>

<style scoped>
.players-view { display: flex; flex-direction: column; gap: 1rem; }
.page-header { display: flex; align-items: center; gap: 1rem; }
.page-title { font-size: 1.5rem; font-weight: 600; }
.toolbar { display: flex; align-items: center; gap: 0.5rem; }
.search-wrapper { flex: 1; max-width: 350px; }
.search-input { width: 100%; }
.players-table { cursor: pointer; }
.player-name { display: flex; align-items: center; gap: 0.5rem; }
.admin-tag { font-size: 0.65rem; }
.health-bar { margin-bottom: 2px; }
.bar-label { font-size: 0.75rem; color: var(--kc-text-secondary); }
.position-text { font-family: monospace; font-size: 0.85rem; color: var(--kc-text-secondary); }
.action-buttons { display: flex; gap: 0.25rem; }
.empty-state { display: flex; flex-direction: column; align-items: center; gap: 0.5rem; padding: 2rem; color: var(--kc-text-secondary); }

@media (max-width: 768px) {
  .page-header { flex-wrap: wrap; }
  .toolbar { flex-wrap: wrap; width: 100%; }
  .search-wrapper { max-width: none; flex: 1 1 100%; }
}
</style>
