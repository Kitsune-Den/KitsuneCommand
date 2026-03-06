<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { usePlayersStore } from '@/stores/players'
import { getOnlinePlayers, getKnownPlayers, getAllPlayerMetadata, kickPlayer, banPlayer } from '@/api/players'
import { usePermissions } from '@/composables/usePermissions'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import ProgressBar from 'primevue/progressbar'
import Avatar from 'primevue/avatar'
import type { PlayerInfo } from '@/types'
import PlayerEditDialog from '@/components/PlayerEditDialog.vue'
import PlayerPmDialog from '@/components/PlayerPmDialog.vue'
import PlayerGiveItemDialog from '@/components/PlayerGiveItemDialog.vue'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const playersStore = usePlayersStore()
const { isAdmin, canKickPlayers, canBanPlayers, canGiveItems, canSendChat } = usePermissions()
const loading = ref(true)
const searchFilter = ref('')

// View mode: 'grid' or 'table'
const viewMode = ref<'grid' | 'table'>(
  (localStorage.getItem('kc-players-view-mode') as 'grid' | 'table') || 'grid'
)
const showOffline = ref(localStorage.getItem('kc-players-show-offline') === 'true')
const knownTotal = ref(0)

function setViewMode(mode: 'grid' | 'table') {
  viewMode.value = mode
  localStorage.setItem('kc-players-view-mode', mode)
}

function toggleShowOffline() {
  showOffline.value = !showOffline.value
  localStorage.setItem('kc-players-show-offline', String(showOffline.value))
  fetchPlayers()
}

// Dialog state
const editDialogVisible = ref(false)
const pmDialogVisible = ref(false)
const giveDialogVisible = ref(false)
const selectedPlayer = ref<PlayerInfo | null>(null)

const filteredPlayers = computed(() => {
  const search = searchFilter.value.toLowerCase()
  if (!search) return playersStore.playerList
  return playersStore.playerList.filter(
    (p) => p.playerName.toLowerCase().includes(search) || p.playerId?.toLowerCase().includes(search)
  )
})

async function fetchPlayers() {
  loading.value = true
  try {
    const metadataPromise = getAllPlayerMetadata().catch(() => ({}))
    let players: PlayerInfo[]

    if (showOffline.value) {
      const [known, metadata] = await Promise.all([getKnownPlayers(0, 200), metadataPromise])
      players = known.items
      knownTotal.value = known.total
      playersStore.setMetadata(metadata)
    } else {
      const [online, metadata] = await Promise.all([getOnlinePlayers(), metadataPromise])
      players = online
      playersStore.setMetadata(metadata)
    }

    playersStore.setPlayers(players)
  } catch {
    toast.add({ severity: 'error', summary: t('common.error'), detail: t('players.failedToFetch'), life: 3000 })
  } finally {
    loading.value = false
  }
}

function viewPlayer(player: PlayerInfo) {
  router.push({ name: 'PlayerDetail', params: { entityId: player.entityId.toString() } })
}

function openEditDialog(player: PlayerInfo) {
  selectedPlayer.value = player
  editDialogVisible.value = true
}

function openPmDialog(player: PlayerInfo) {
  selectedPlayer.value = player
  pmDialogVisible.value = true
}

function openGiveDialog(player: PlayerInfo) {
  selectedPlayer.value = player
  giveDialogVisible.value = true
}

function confirmKick(player: PlayerInfo) {
  confirm.require({
    message: t('players.confirmKickMessage', { name: player.playerName }),
    header: t('players.confirmKickHeader'),
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-warning',
    accept: async () => {
      try {
        await kickPlayer(player.entityId)
        toast.add({ severity: 'success', summary: t('players.kicked'), detail: t('players.kickedDetail', { name: player.playerName }), life: 3000 })
      } catch {
        toast.add({ severity: 'error', summary: t('common.error'), detail: t('players.failedToKick'), life: 3000 })
      }
    },
  })
}

function confirmBan(player: PlayerInfo) {
  confirm.require({
    message: t('players.confirmBanMessage', { name: player.playerName }),
    header: t('players.confirmBanHeader'),
    icon: 'pi pi-ban',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await banPlayer(player.entityId)
        toast.add({ severity: 'success', summary: t('players.banned'), detail: t('players.bannedDetail', { name: player.playerName }), life: 3000 })
      } catch {
        toast.add({ severity: 'error', summary: t('common.error'), detail: t('players.failedToBan'), life: 3000 })
      }
    },
  })
}

function formatPosition(p: PlayerInfo): string {
  return `${Math.round(p.positionX)}, ${Math.round(p.positionY)}, ${Math.round(p.positionZ)}`
}

// Avatar helpers
function playerInitials(name: string): string {
  const parts = name.trim().split(/\s+/)
  if (parts.length >= 2) return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
  return name.substring(0, 2).toUpperCase()
}

function playerColor(playerId: string): string {
  let hash = 0
  for (let i = 0; i < playerId.length; i++) {
    hash = playerId.charCodeAt(i) + ((hash << 5) - hash)
  }
  const hue = Math.abs(hash) % 360
  return `hsl(${hue}, 55%, 40%)`
}

function getPlayerMeta(player: PlayerInfo) {
  return playersStore.getMetadata(player.playerId)
}

function nameColorStyle(player: PlayerInfo): Record<string, string> | undefined {
  const meta = getPlayerMeta(player)
  if (meta?.nameColor) return { color: `#${meta.nameColor}` }
  return undefined
}

function formatLastSeen(unixSeconds: number): string {
  if (!unixSeconds) return ''
  const date = new Date(unixSeconds * 1000)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  if (diffMins < 60) return `${diffMins}m ago`
  const diffHours = Math.floor(diffMins / 60)
  if (diffHours < 24) return `${diffHours}h ago`
  const diffDays = Math.floor(diffHours / 24)
  if (diffDays < 30) return `${diffDays}d ago`
  return date.toLocaleDateString()
}

function onMetadataSaved() {
  // Refresh metadata after edit
  getAllPlayerMetadata().then(data => playersStore.setMetadata(data)).catch(() => {})
}

onMounted(fetchPlayers)
</script>

<template>
  <div class="players-view">
    <div class="page-header">
      <h1 class="page-title">{{ t('players.title') }}</h1>
      <Tag
        :value="showOffline
          ? t('players.totalKnown', { n: knownTotal })
          : t('players.online', { n: playersStore.onlineCount })"
        :severity="showOffline ? 'secondary' : 'info'"
      />
    </div>

    <div class="toolbar">
      <span class="p-input-icon-left search-wrapper">
        <i class="pi pi-search" />
        <InputText v-model="searchFilter" :placeholder="t('players.searchPlaceholder')" class="search-input" />
      </span>
      <Button icon="pi pi-refresh" text severity="secondary" @click="fetchPlayers" :loading="loading" />
      <Button
        :icon="showOffline ? 'pi pi-users' : 'pi pi-user'"
        :text="!showOffline"
        :severity="showOffline ? 'info' : 'secondary'"
        size="small"
        @click="toggleShowOffline"
        :title="t('players.showOffline')"
      />
      <div class="view-toggle">
        <Button
          icon="pi pi-th-large"
          :text="viewMode !== 'grid'"
          :severity="viewMode === 'grid' ? 'info' : 'secondary'"
          size="small"
          @click="setViewMode('grid')"
          :title="t('players.viewGrid')"
        />
        <Button
          icon="pi pi-list"
          :text="viewMode !== 'table'"
          :severity="viewMode === 'table' ? 'info' : 'secondary'"
          size="small"
          @click="setViewMode('table')"
          :title="t('players.viewTable')"
        />
      </div>
    </div>

    <!-- Empty state -->
    <div v-if="!loading && filteredPlayers.length === 0" class="empty-state">
      <i class="pi pi-users" style="font-size: 2rem; color: var(--kc-text-secondary)" />
      <p>{{ showOffline ? t('players.noPlayersFound') : t('players.noPlayersOnline') }}</p>
    </div>

    <!-- Loading state -->
    <div v-if="loading && filteredPlayers.length === 0" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 1.5rem" />
    </div>

    <!-- ═══ CARD GRID VIEW ═══ -->
    <div v-if="viewMode === 'grid' && filteredPlayers.length > 0" class="player-grid">
      <div
        v-for="player in filteredPlayers"
        :key="player.playerId"
        class="player-card"
        :class="{ 'player-card--offline': !player.isOnline }"
      >
        <!-- Card header: avatar + name -->
        <div class="card-header" @click="player.isOnline ? viewPlayer(player) : undefined">
          <div class="avatar-wrapper">
            <Avatar
              :label="playerInitials(player.playerName)"
              shape="circle"
              size="large"
              :style="{ backgroundColor: player.isOnline ? playerColor(player.playerId) : '#555', color: '#fff' }"
            />
            <i v-if="player.isAdmin" class="pi pi-shield admin-badge" />
            <i v-if="!player.isOnline" class="pi pi-circle offline-dot" />
          </div>
          <div class="card-identity">
            <span class="card-name" :style="nameColorStyle(player)">{{ player.playerName }}</span>
            <div class="card-tags">
              <Tag v-if="!player.isOnline" :value="t('players.offline')" severity="secondary" class="mini-tag" />
              <Tag v-if="player.isAdmin" value="Admin" severity="warn" class="mini-tag" />
              <Tag
                v-if="getPlayerMeta(player)?.customTag"
                :value="getPlayerMeta(player)!.customTag!"
                severity="info"
                class="mini-tag"
              />
            </div>
          </div>
        </div>

        <!-- Stats (online only) -->
        <div v-if="player.isOnline" class="card-stats">
          <div class="stat">
            <i class="pi pi-star" />
            <span class="stat-label">{{ t('players.level') }}</span>
            <span class="stat-value">{{ player.level }}</span>
          </div>
          <div class="stat">
            <i class="pi pi-heart" />
            <span class="stat-label">{{ t('players.health') }}</span>
            <span class="stat-value">{{ Math.round(player.health) }}</span>
          </div>
          <div class="stat">
            <i class="pi pi-bolt" />
            <span class="stat-label">{{ t('players.zKills') }}</span>
            <span class="stat-value">{{ player.zombieKills }}</span>
          </div>
          <div class="stat">
            <i class="pi pi-times-circle" />
            <span class="stat-label">{{ t('players.deaths') }}</span>
            <span class="stat-value">{{ player.deaths }}</span>
          </div>
        </div>

        <!-- Last seen (offline only) -->
        <div v-if="!player.isOnline && player.lastLogin" class="card-last-seen">
          <i class="pi pi-clock" />
          <span>{{ t('players.lastSeen') }}: {{ formatLastSeen(player.lastLogin) }}</span>
        </div>

        <!-- Health bar (online only) -->
        <ProgressBar v-if="player.isOnline" :value="player.health" :showValue="false" class="card-health-bar" style="height: 4px" />

        <!-- Position (online only) -->
        <div v-if="player.isOnline" class="card-position">
          <i class="pi pi-map-marker" />
          <span>{{ formatPosition(player) }}</span>
        </div>

        <!-- Quick actions -->
        <div class="card-actions">
          <Button
            v-if="player.isOnline"
            icon="pi pi-eye"
            text
            severity="info"
            size="small"
            @click="viewPlayer(player)"
            :title="t('players.viewDetails')"
          />
          <Button
            v-if="isAdmin"
            icon="pi pi-pencil"
            text
            severity="info"
            size="small"
            @click="openEditDialog(player)"
            :title="t('players.editPlayer')"
          />
          <Button
            v-if="canSendChat && player.isOnline"
            icon="pi pi-envelope"
            text
            severity="info"
            size="small"
            @click="openPmDialog(player)"
            :title="t('players.sendPm')"
          />
          <Button
            v-if="canGiveItems && player.isOnline"
            icon="pi pi-box"
            text
            severity="info"
            size="small"
            @click="openGiveDialog(player)"
            :title="t('players.giveItem')"
          />
          <Button
            v-if="canKickPlayers && player.isOnline"
            icon="pi pi-sign-out"
            text
            severity="warning"
            size="small"
            @click="confirmKick(player)"
            :title="t('playerDetail.kick')"
          />
          <Button
            v-if="canBanPlayers && player.isOnline"
            icon="pi pi-ban"
            text
            severity="danger"
            size="small"
            @click="confirmBan(player)"
            :title="t('playerDetail.ban')"
          />
        </div>
      </div>
    </div>

    <!-- ═══ TABLE VIEW ═══ -->
    <DataTable
      v-if="viewMode === 'table' && filteredPlayers.length > 0"
      :value="filteredPlayers"
      :loading="loading"
      stripedRows
      sortField="playerName"
      :sortOrder="1"
      class="players-table"
      :rowHover="true"
      @row-click="(e: any) => viewPlayer(e.data)"
    >
      <Column v-if="showOffline" field="isOnline" :header="t('players.status')" sortable style="width: 80px">
        <template #body="{ data }">
          <Tag :value="data.isOnline ? t('players.onlineStatus') : t('players.offline')" :severity="data.isOnline ? 'success' : 'secondary'" class="mini-tag" />
        </template>
      </Column>

      <Column field="playerName" :header="t('players.name')" sortable>
        <template #body="{ data }">
          <div class="player-name-cell" :class="{ 'player-name-cell--offline': !data.isOnline }">
            <Avatar
              :label="playerInitials(data.playerName)"
              shape="circle"
              size="normal"
              :style="{ backgroundColor: data.isOnline ? playerColor(data.playerId) : '#555', color: '#fff', fontSize: '0.75rem' }"
            />
            <span :style="nameColorStyle(data)">{{ data.playerName }}</span>
            <Tag v-if="data.isAdmin" value="Admin" severity="warn" class="admin-tag" />
            <Tag
              v-if="getPlayerMeta(data)?.customTag"
              :value="getPlayerMeta(data)!.customTag!"
              severity="info"
              class="admin-tag"
            />
          </div>
        </template>
      </Column>

      <Column field="level" :header="t('players.level')" sortable style="width: 80px" />

      <Column :header="t('players.health')" style="width: 150px">
        <template #body="{ data }">
          <template v-if="data.isOnline">
            <ProgressBar :value="data.health" :showValue="false" class="health-bar" style="height: 8px" />
            <span class="bar-label">{{ Math.round(data.health) }}</span>
          </template>
          <span v-else class="bar-label">—</span>
        </template>
      </Column>

      <Column :header="t('players.position')" style="width: 180px">
        <template #body="{ data }">
          <span v-if="data.isOnline" class="position-text">{{ formatPosition(data) }}</span>
          <span v-else-if="data.lastLogin" class="position-text">{{ formatLastSeen(data.lastLogin) }}</span>
          <span v-else class="position-text">—</span>
        </template>
      </Column>

      <Column field="zombieKills" :header="t('players.zKills')" sortable style="width: 80px" />
      <Column field="playerKills" :header="t('players.pKills')" sortable style="width: 80px" />
      <Column field="deaths" :header="t('players.deaths')" sortable style="width: 80px" />

      <Column :header="t('players.actions')" style="width: 180px">
        <template #body="{ data }">
          <div class="action-buttons" @click.stop>
            <Button v-if="isAdmin" icon="pi pi-pencil" text severity="info" size="small" @click="openEditDialog(data)" :title="t('players.editPlayer')" />
            <Button v-if="canSendChat && data.isOnline" icon="pi pi-envelope" text severity="info" size="small" @click="openPmDialog(data)" :title="t('players.sendPm')" />
            <Button v-if="canGiveItems && data.isOnline" icon="pi pi-box" text severity="info" size="small" @click="openGiveDialog(data)" :title="t('players.giveItem')" />
            <Button v-if="canKickPlayers && data.isOnline" icon="pi pi-sign-out" text severity="warning" size="small" @click="confirmKick(data)" :title="t('playerDetail.kick')" />
            <Button v-if="canBanPlayers && data.isOnline" icon="pi pi-ban" text severity="danger" size="small" @click="confirmBan(data)" :title="t('playerDetail.ban')" />
          </div>
        </template>
      </Column>

      <template #empty>
        <div class="empty-state">
          <i class="pi pi-users" style="font-size: 2rem; color: var(--kc-text-secondary)" />
          <p>{{ t('players.noPlayersOnline') }}</p>
        </div>
      </template>
    </DataTable>

    <!-- Dialogs -->
    <PlayerEditDialog
      v-model:visible="editDialogVisible"
      :player="selectedPlayer"
      @saved="onMetadataSaved"
    />
    <PlayerPmDialog
      v-model:visible="pmDialogVisible"
      :player="selectedPlayer"
    />
    <PlayerGiveItemDialog
      v-model:visible="giveDialogVisible"
      :player="selectedPlayer"
    />
  </div>
</template>

<style scoped>
.players-view { display: flex; flex-direction: column; gap: 1rem; }
.page-header { display: flex; align-items: center; gap: 1rem; }
.page-title { font-size: 1.5rem; font-weight: 600; }

.toolbar { display: flex; align-items: center; gap: 0.5rem; }
.search-wrapper { flex: 1; max-width: 350px; }
.search-input { width: 100%; }
.view-toggle { display: flex; margin-left: auto; gap: 0.25rem; }

.loading-state {
  display: flex; align-items: center; justify-content: center;
  padding: 3rem; color: var(--kc-text-secondary);
}

.empty-state {
  display: flex; flex-direction: column; align-items: center;
  gap: 0.5rem; padding: 3rem; color: var(--kc-text-secondary);
}

/* ── Card Grid ── */
.player-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 1rem;
}

.player-card {
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
  border-radius: 10px;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  transition: border-color 0.15s, box-shadow 0.15s;
}

.player-card:hover {
  border-color: var(--kc-cyan);
  box-shadow: 0 0 12px rgba(0, 212, 255, 0.1);
}

.card-header {
  display: flex; align-items: center; gap: 0.75rem;
  cursor: pointer;
}

.avatar-wrapper { position: relative; flex-shrink: 0; }

.admin-badge {
  position: absolute; bottom: -2px; right: -2px;
  font-size: 0.65rem;
  background: var(--kc-orange);
  color: #000;
  border-radius: 50%;
  padding: 3px;
}

.card-identity { flex: 1; min-width: 0; }

.card-name {
  font-weight: 600; font-size: 1rem;
  color: var(--kc-text-primary);
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  display: block;
}

.card-tags { display: flex; gap: 0.25rem; margin-top: 0.25rem; flex-wrap: wrap; }
.mini-tag { font-size: 0.6rem; padding: 0.1rem 0.35rem; line-height: 1; }

.card-stats {
  display: grid; grid-template-columns: repeat(4, 1fr);
  gap: 0.25rem;
}

.stat {
  display: flex; flex-direction: column; align-items: center; gap: 0.1rem;
  padding: 0.35rem 0.25rem;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 6px;
}

.stat i { font-size: 0.7rem; color: var(--kc-text-secondary); }
.stat-label { font-size: 0.6rem; color: var(--kc-text-secondary); text-transform: uppercase; letter-spacing: 0.03em; }
.stat-value { font-size: 0.9rem; font-weight: 600; color: var(--kc-text-primary); }

.card-health-bar { border-radius: 2px; }

.card-position {
  display: flex; align-items: center; gap: 0.4rem;
  font-family: monospace; font-size: 0.8rem;
  color: var(--kc-text-secondary);
}

.card-position i { font-size: 0.75rem; }

/* ── Offline card styling ── */
.player-card--offline {
  opacity: 0.65;
}

.player-card--offline:hover {
  opacity: 0.85;
}

.offline-dot {
  position: absolute; bottom: -2px; right: -2px;
  font-size: 0.55rem;
  color: var(--kc-text-secondary);
}

.card-last-seen {
  display: flex; align-items: center; gap: 0.4rem;
  font-size: 0.8rem; color: var(--kc-text-secondary);
}

.card-last-seen i { font-size: 0.75rem; }

.player-name-cell--offline { opacity: 0.6; }

.card-actions {
  display: flex; gap: 0.15rem;
  border-top: 1px solid var(--kc-border);
  padding-top: 0.5rem;
  flex-wrap: wrap;
}

/* ── Table View ── */
.players-table { cursor: pointer; }
.player-name-cell { display: flex; align-items: center; gap: 0.5rem; }
.admin-tag { font-size: 0.65rem; }
.health-bar { margin-bottom: 2px; }
.bar-label { font-size: 0.75rem; color: var(--kc-text-secondary); }
.position-text { font-family: monospace; font-size: 0.85rem; color: var(--kc-text-secondary); }
.action-buttons { display: flex; gap: 0.15rem; }

@media (max-width: 768px) {
  .page-header { flex-wrap: wrap; }
  .toolbar { flex-wrap: wrap; width: 100%; }
  .search-wrapper { max-width: none; flex: 1 1 100%; }
  .player-grid { grid-template-columns: 1fr; }
}
</style>
