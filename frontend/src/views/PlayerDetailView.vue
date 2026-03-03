<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import { getPlayer, kickPlayer, banPlayer } from '@/api/players'
import { usePermissions } from '@/composables/usePermissions'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Card from 'primevue/card'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import ProgressBar from 'primevue/progressbar'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import type { PlayerDetailInfo } from '@/types'

const props = defineProps<{ entityId: string }>()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const { canKickPlayers, canBanPlayers } = usePermissions()
const player = ref<PlayerDetailInfo | null>(null)
const loading = ref(true)
const error = ref('')

const entityIdNum = computed(() => parseInt(props.entityId))

async function fetchPlayer() {
  try {
    player.value = await getPlayer(entityIdNum.value)
  } catch {
    error.value = 'Failed to load player data.'
  } finally {
    loading.value = false
  }
}

function confirmKick() {
  if (!player.value) return
  const name = player.value.playerName
  confirm.require({
    message: `Kick ${name} from the server?`,
    header: 'Confirm Kick',
    icon: 'pi pi-exclamation-triangle',
    acceptClass: 'p-button-warning',
    accept: async () => {
      try {
        await kickPlayer(entityIdNum.value)
        toast.add({ severity: 'success', summary: 'Kicked', detail: `${name} has been kicked`, life: 3000 })
        router.push({ name: 'Players' })
      } catch {
        toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to kick player', life: 3000 })
      }
    },
  })
}

function confirmBan() {
  if (!player.value) return
  const name = player.value.playerName
  confirm.require({
    message: `Ban ${name} from the server?`,
    header: 'Confirm Ban',
    icon: 'pi pi-ban',
    acceptClass: 'p-button-danger',
    accept: async () => {
      try {
        await banPlayer(entityIdNum.value)
        toast.add({ severity: 'success', summary: 'Banned', detail: `${name} has been banned`, life: 3000 })
        router.push({ name: 'Players' })
      } catch {
        toast.add({ severity: 'error', summary: 'Error', detail: 'Failed to ban player', life: 3000 })
      }
    },
  })
}

onMounted(fetchPlayer)
</script>

<template>
  <div class="player-detail">
    <div class="page-header">
      <Button icon="pi pi-arrow-left" text severity="secondary" @click="router.push({ name: 'Players' })" />
      <h1 class="page-title">{{ player?.playerName ?? 'Player Details' }}</h1>
      <Tag v-if="player?.isAdmin" value="Admin" severity="warn" />
    </div>

    <div v-if="loading" class="loading-state">
      <i class="pi pi-spin pi-spinner" style="font-size: 2rem"></i>
      <p>Loading player data...</p>
    </div>

    <div v-else-if="error" class="error-state">
      <i class="pi pi-exclamation-triangle" style="font-size: 2rem; color: var(--kc-orange)"></i>
      <p>{{ error }}</p>
    </div>

    <template v-else-if="player">
      <!-- Stats Overview -->
      <div class="stats-grid">
        <Card class="stat-card">
          <template #content>
            <div class="stat-label">Level</div>
            <div class="stat-value">{{ player.level }}</div>
          </template>
        </Card>
        <Card class="stat-card">
          <template #content>
            <div class="stat-label">Health</div>
            <ProgressBar :value="player.health" :showValue="false" style="height: 8px; margin: 4px 0" />
            <div class="stat-value">{{ Math.round(player.health) }}</div>
          </template>
        </Card>
        <Card class="stat-card">
          <template #content>
            <div class="stat-label">Stamina</div>
            <ProgressBar :value="player.stamina" :showValue="false" style="height: 8px; margin: 4px 0" />
            <div class="stat-value">{{ Math.round(player.stamina) }}</div>
          </template>
        </Card>
        <Card class="stat-card">
          <template #content>
            <div class="stat-label">Position</div>
            <div class="stat-value mono">
              {{ Math.round(player.positionX) }}, {{ Math.round(player.positionY) }}, {{ Math.round(player.positionZ) }}
            </div>
          </template>
        </Card>
        <Card class="stat-card">
          <template #content>
            <div class="stat-label">Zombie Kills</div>
            <div class="stat-value">{{ player.zombieKills }}</div>
          </template>
        </Card>
        <Card class="stat-card">
          <template #content>
            <div class="stat-label">Player Kills</div>
            <div class="stat-value">{{ player.playerKills }}</div>
          </template>
        </Card>
        <Card class="stat-card">
          <template #content>
            <div class="stat-label">Deaths</div>
            <div class="stat-value">{{ player.deaths }}</div>
          </template>
        </Card>
        <Card class="stat-card">
          <template #content>
            <div class="stat-label">Score</div>
            <div class="stat-value">{{ player.score }}</div>
          </template>
        </Card>
      </div>

      <!-- Actions -->
      <div class="actions-bar" v-if="canKickPlayers || canBanPlayers">
        <Button v-if="canKickPlayers" label="Kick" icon="pi pi-sign-out" severity="warning" size="small" @click="confirmKick" />
        <Button v-if="canBanPlayers" label="Ban" icon="pi pi-ban" severity="danger" size="small" @click="confirmBan" />
      </div>

      <!-- Inventory: Belt -->
      <Card class="section-card" v-if="player.beltItems?.length">
        <template #title>Toolbar (Belt)</template>
        <template #content>
          <DataTable :value="player.beltItems" size="small" stripedRows>
            <Column field="slotIndex" header="Slot" style="width: 60px" />
            <Column field="itemName" header="Item" />
            <Column field="count" header="Qty" style="width: 60px" />
            <Column field="quality" header="Quality" style="width: 80px" />
          </DataTable>
        </template>
      </Card>

      <!-- Inventory: Bag -->
      <Card class="section-card" v-if="player.bagItems?.length">
        <template #title>Backpack</template>
        <template #content>
          <DataTable :value="player.bagItems" size="small" stripedRows>
            <Column field="slotIndex" header="Slot" style="width: 60px" />
            <Column field="itemName" header="Item" />
            <Column field="count" header="Qty" style="width: 60px" />
            <Column field="quality" header="Quality" style="width: 80px" />
          </DataTable>
        </template>
      </Card>

      <!-- Skills -->
      <Card class="section-card" v-if="player.skills?.length">
        <template #title>Skills &amp; Perks</template>
        <template #content>
          <DataTable :value="player.skills" size="small" stripedRows sortField="name" :sortOrder="1">
            <Column field="name" header="Skill" sortable />
            <Column header="Level" sortable sortField="level" style="width: 120px">
              <template #body="{ data }">
                {{ data.level }} / {{ data.maxLevel }}
              </template>
            </Column>
            <Column header="Status" style="width: 100px">
              <template #body="{ data }">
                <Tag v-if="data.isLocked" value="Locked" severity="secondary" />
                <Tag v-else-if="data.level >= data.maxLevel" value="Max" severity="success" />
              </template>
            </Column>
          </DataTable>
        </template>
      </Card>
    </template>
  </div>
</template>

<style scoped>
.player-detail { display: flex; flex-direction: column; gap: 1rem; }
.page-header { display: flex; align-items: center; gap: 0.75rem; }
.page-title { font-size: 1.5rem; font-weight: 600; }

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 0.75rem;
}

.stat-card {
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
}

.stat-label {
  font-size: 0.75rem;
  color: var(--kc-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 0.25rem;
}

.stat-value {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--kc-text-primary);
}

.stat-value.mono { font-family: monospace; font-size: 0.95rem; }

.actions-bar { display: flex; gap: 0.5rem; }

.section-card {
  background: var(--kc-bg-card);
  border: 1px solid var(--kc-border);
}

.loading-state, .error-state {
  display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: 1rem; padding: 4rem 0; color: var(--kc-text-secondary);
}
</style>
