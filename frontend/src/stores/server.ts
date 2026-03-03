import { defineStore } from 'pinia'
import { ref } from 'vue'

export interface ActivityItem {
  id: number
  type: 'login' | 'logout' | 'chat' | 'kill' | 'system'
  message: string
  timestamp: Date
}

export const useServerStore = defineStore('server', () => {
  const gameDay = ref(0)
  const gameHour = ref(0)
  const gameMinute = ref(0)
  const isBloodMoon = ref(false)
  const activity = ref<ActivityItem[]>([])

  let activityId = 0

  function updateGameTime(data: { day: number; hour: number; minute: number; isBloodMoon: boolean }) {
    gameDay.value = data.day
    gameHour.value = data.hour
    gameMinute.value = data.minute
    isBloodMoon.value = data.isBloodMoon
  }

  function addActivity(type: ActivityItem['type'], message: string) {
    activity.value.unshift({
      id: ++activityId,
      type,
      message,
      timestamp: new Date(),
    })
    // Keep last 50 items
    if (activity.value.length > 50) {
      activity.value = activity.value.slice(0, 50)
    }
  }

  return { gameDay, gameHour, gameMinute, isBloodMoon, activity, updateGameTime, addActivity }
})
