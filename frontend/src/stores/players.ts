import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { PlayerInfo, PlayerPositionData, PlayerMetadata } from '@/types'

export const usePlayersStore = defineStore('players', () => {
  const players = ref<Map<string, PlayerInfo>>(new Map())
  const entityIdIndex = ref<Map<number, string>>(new Map())
  const metadata = ref<Map<string, PlayerMetadata>>(new Map())

  const playerList = computed(() => Array.from(players.value.values()))
  const onlineCount = computed(() => {
    let count = 0
    for (const p of players.value.values()) {
      if (p.isOnline) count++
    }
    return count
  })

  function setPlayers(list: PlayerInfo[]) {
    players.value.clear()
    entityIdIndex.value.clear()
    for (const p of list) {
      players.value.set(p.playerId, p)
      if (p.entityId > 0) {
        entityIdIndex.value.set(p.entityId, p.playerId)
      }
    }
  }

  function addPlayer(player: PlayerInfo) {
    players.value.set(player.playerId, player)
    if (player.entityId > 0) {
      entityIdIndex.value.set(player.entityId, player.playerId)
    }
  }

  function removePlayer(entityId: number) {
    const playerId = entityIdIndex.value.get(entityId)
    if (playerId) {
      players.value.delete(playerId)
      entityIdIndex.value.delete(entityId)
    }
  }

  function updatePositions(positions: PlayerPositionData[]) {
    for (const pos of positions) {
      const playerId = entityIdIndex.value.get(pos.entityId)
      const player = playerId ? players.value.get(playerId) : undefined
      if (player) {
        player.positionX = pos.x
        player.positionY = pos.y
        player.positionZ = pos.z
      }
    }
  }

  function getPlayer(entityId: number): PlayerInfo | undefined {
    const playerId = entityIdIndex.value.get(entityId)
    return playerId ? players.value.get(playerId) : undefined
  }

  function setMetadata(data: Record<string, PlayerMetadata>) {
    metadata.value.clear()
    for (const meta of Object.values(data)) {
      metadata.value.set(meta.playerId, meta)
    }
  }

  function getMetadata(playerId: string): PlayerMetadata | undefined {
    return metadata.value.get(playerId)
  }

  function updateMetadataEntry(playerId: string, meta: PlayerMetadata) {
    metadata.value.set(playerId, meta)
  }

  return {
    players, playerList, onlineCount, metadata,
    setPlayers, addPlayer, removePlayer, updatePositions, getPlayer,
    setMetadata, getMetadata, updateMetadataEntry
  }
})
