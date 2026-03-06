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
