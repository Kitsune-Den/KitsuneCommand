import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { PointsInfo } from '@/api/points'

export interface PointsUpdateEvent {
  playerId: string
  playerName: string
  points: number
  change: number
  reason: string
}

export const useEconomyStore = defineStore('economy', () => {
  const pointsList = ref<PointsInfo[]>([])
  const pointsTotal = ref(0)

  function setPointsList(items: PointsInfo[], total: number) {
    pointsList.value = items
    pointsTotal.value = total
  }

  /** Handle real-time PointsUpdate WebSocket events */
  function handlePointsUpdate(event: PointsUpdateEvent) {
    const idx = pointsList.value.findIndex((p) => p.id === event.playerId)
    if (idx >= 0) {
      pointsList.value[idx] = {
        ...pointsList.value[idx],
        points: event.points,
        playerName: event.playerName,
      }
    }
    // If player not in current list page, that's fine — they'll appear on refresh
  }

  function clearPoints() {
    pointsList.value = []
    pointsTotal.value = 0
  }

  return { pointsList, pointsTotal, setPointsList, handlePointsUpdate, clearPoints }
})
