import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { PlayerInfo, PlayerPositionData } from '@/types'

export const usePlayersStore = defineStore('players', () => {
  const players = ref<Map<number, PlayerInfo>>(new Map())

  const playerList = computed(() => Array.from(players.value.values()))
  const onlineCount = computed(() => players.value.size)

  function setPlayers(list: PlayerInfo[]) {
    players.value.clear()
    for (const p of list) {
      players.value.set(p.entityId, p)
    }
  }

  function addPlayer(player: PlayerInfo) {
    players.value.set(player.entityId, player)
  }

  function removePlayer(entityId: number) {
    players.value.delete(entityId)
  }

  function updatePositions(positions: PlayerPositionData[]) {
    for (const pos of positions) {
      const player = players.value.get(pos.entityId)
      if (player) {
        player.positionX = pos.x
        player.positionY = pos.y
        player.positionZ = pos.z
      }
    }
  }

  function getPlayer(entityId: number): PlayerInfo | undefined {
    return players.value.get(entityId)
  }

  return { players, playerList, onlineCount, setPlayers, addPlayer, removePlayer, updatePositions, getPlayer }
})
