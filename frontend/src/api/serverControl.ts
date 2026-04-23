import apiClient from './client'

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
  localIp?: string
  publicIp?: string
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
  workingSetMemory: number
  peakWorkingSetMemory: number
  threadCount: number
  system: {
    os: string
    processorCount: number
    is64Bit: boolean
  }
}

export async function getServerInfo(): Promise<ServerInfo> {
  const res = await apiClient.get('/api/server/info')
  return res.data.data
}

export async function getServerStats(): Promise<ServerStats> {
  const res = await apiClient.get('/api/server/stats')
  return res.data.data
}

export async function saveWorld(): Promise<string> {
  const res = await apiClient.post('/api/server/save')
  return res.data.data.message
}

export async function shutdownServer(delaySeconds: number = 10): Promise<string> {
  const res = await apiClient.post('/api/server/shutdown', { delaySeconds })
  return res.data.message
}

export async function restartServer(): Promise<string> {
  const res = await apiClient.post('/api/server/restart', {})
  return res.data.data ?? res.data.message ?? 'Restart triggered.'
}
