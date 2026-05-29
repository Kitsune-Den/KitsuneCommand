import apiClient from './client'
import type { DashboardStats } from '@/types'

export interface ServerInfo {
  serverName: string
  serverPort: number
  maxPlayers: number
  gameWorld: string
  gameName: string
  gameMode: string
  difficulty: number
  dayNightLength: number
  bloodMoonFrequency: number
  currentDay: number
  currentTime: string
  onlinePlayers: number
  version: string
  kitsuneCommandVersion: string
  localIp: string
  publicIp: string
  serverVisibility?: number       // 0 = hidden, 1 = friends, 2 = public
  steamRegistered?: boolean
  eosRegistered?: boolean
  steamServerId?: string | null
}

export interface ServerStats {
  fps: number
  entityCount: number
  playerCount: number
  uptime: number
  gcMemory: number
}

export async function getServerInfo(): Promise<ServerInfo> {
  const response = await apiClient.get('/api/server/info')
  return response.data.data
}

export async function getServerStats(): Promise<ServerStats> {
  const response = await apiClient.get('/api/server/stats')
  return response.data.data
}

export async function executeCommand(command: string): Promise<string> {
  const response = await apiClient.post('/api/server/command', { command })
  return response.data.data.output
}

export async function getDashboardStats(): Promise<DashboardStats> {
  const response = await apiClient.get('/api/dashboard/stats')
  return response.data.data
}

/**
 * One entry from GET /api/server/timezones. The `id` is whatever string the
 * runtime will accept back via TimeZoneInfo.FindSystemTimeZoneById — Windows
 * registry IDs on .NET Framework / Windows ("Pacific Standard Time"), IANA
 * IDs on .NET Core / Linux ("America/Los_Angeles"). Don't try to interpret
 * it; just round-trip it.
 */
export interface TimezoneOption {
  id: string
  displayName: string
  baseUtcOffsetMinutes: number
}

export async function getTimezones(): Promise<TimezoneOption[]> {
  const response = await apiClient.get('/api/server/timezones')
  return response.data.data
}
