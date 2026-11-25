import { defineStore } from 'pinia'
import { ref } from 'vue'
import apiClient from '@/api/client'

export interface ActivityItem {
  id: number
  type: 'login' | 'logout' | 'chat' | 'kill' | 'system'
  message: string
  timestamp: Date
}

/** The SkyChanged WebSocket event: the in-game clock, sent about once per in-game minute. */
export interface SkyChangedData {
  day: number
  hour: number
  minute: number
  isBloodMoon: boolean
  /** The day the game has picked for the next blood moon; 0 when blood moons are off. */
  bloodMoonDay?: number
  duskHour?: number
  dawnHour?: number
}

export const useServerStore = defineStore('server', () => {
  const gameDay = ref(0)
  const gameHour = ref(0)
  const gameMinute = ref(0)
  const isBloodMoon = ref(false)
  // The day the game has picked for the next blood moon; 0 when they're off or not heard yet.
  const bloodMoonDay = ref(0)
  const activity = ref<ActivityItem[]>([])
  const bloodMoonVote = ref({
    isActive: false,
    currentVotes: 0,
    requiredVotes: 0,
    totalOnline: 0,
    bloodMoonDay: 0,
  })

  // KitsuneCommand mod version, fetched from /api/server/info on app mount.
  // Backend reads this from ModInfo.xml at startup — no more hand-bumping in
  // C# + XML + TS in lockstep every release.
  const kcVersion = ref<string>('')

  async function fetchKcVersion() {
    if (kcVersion.value) return // already fetched
    try {
      const response = await apiClient.get('/api/server/info')
      kcVersion.value = response.data?.data?.kitsuneCommandVersion ?? ''
    } catch {
      // Silent — the brand-version just won't render until the next successful fetch.
    }
  }

  let activityId = 0

  function updateGameTime(data: SkyChangedData) {
    gameDay.value = data.day
    gameHour.value = data.hour
    gameMinute.value = data.minute
    isBloodMoon.value = data.isBloodMoon
    bloodMoonDay.value = data.bloodMoonDay ?? 0
  }

  function updateBloodMoonVote(data: { isActive: boolean; currentVotes: number; requiredVotes: number; totalOnline: number; bloodMoonDay: number }) {
    bloodMoonVote.value = data
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

  return { gameDay, gameHour, gameMinute, isBloodMoon, bloodMoonDay, activity, bloodMoonVote, kcVersion, updateGameTime, updateBloodMoonVote, addActivity, fetchKcVersion }
})
