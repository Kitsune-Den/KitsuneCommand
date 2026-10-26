import apiClient from './client'

/**
 * One row of the suspicion log. Matches the C# `SuspicionEvent`
 * (see `KitsuneCommand/Data/Entities/SuspicionEvent.cs`).
 * The log only records; nothing in it has kicked or banned anyone.
 */
export interface SuspicionEvent {
  id: number
  /** UTC, "yyyy-MM-dd HH:mm:ss" (SQLite datetime). */
  createdAt: string
  playerId: string | null
  playerName: string | null
  entityId: number | null
  /** inventory, movement, progression or killrate. */
  detector: SuspicionDetector
  /** The check inside the detector, e.g. over_stack, speed, kill_rate. */
  rule: string
  severity: SuspicionSeverity
  /** Server-written English summary; the panel prefers a localized line built from `details`. */
  summary: string | null
  /** JSON object with the numbers behind the flag. */
  details: string | null
  posX: number | null
  posY: number | null
  posZ: number | null
  reviewed: boolean
  reviewedBy: string | null
  reviewedAt: string | null
  reviewNote: string | null
}

export type SuspicionDetector = 'inventory' | 'movement' | 'progression' | 'killrate'
export type SuspicionSeverity = 'low' | 'medium' | 'high'

export interface SuspicionListResponse {
  items: SuspicionEvent[]
  total: number
  /** Unreviewed rows in the whole log, regardless of filters. */
  unreviewed: number
}

export interface SuspicionQuery {
  pageIndex?: number
  pageSize?: number
  detector?: string
  severity?: string
  reviewed?: boolean
  search?: string
}

export interface SuspicionSettings {
  enabled: boolean
  ignoreAdmins: boolean
  exemptPlayerIds: string[]
  repeatCooldownMinutes: number
  retentionDays: number
  discordWebhookUrl: string
  discordMinSeverity: SuspicionSeverity
  discordCooldownMinutes: number
  inventory: {
    enabled: boolean
    scanIntervalSeconds: number
    flagDevItems: boolean
    flagHiddenItems: boolean
    flagOverQuality: boolean
    maxQuality: number
    flagOverStack: boolean
    flagCountJumps: boolean
    countJumpThreshold: number
    allowList: string[]
  }
  movement: {
    enabled: boolean
    maxHorizontalSpeed: number
    speedSamplesRequired: number
    jumpDistance: number
    flagFlying: boolean
    flyHeight: number
    flySamplesRequired: number
    graceSeconds: number
  }
  progression: {
    enabled: boolean
    windowMinutes: number
    maxLevelGain: number
    maxUnexplainedSkillPoints: number
    warmupSeconds: number
  }
  killRate: {
    enabled: boolean
    maxZombieKillsPerMinute: number
  }
}

/** The controller reports failures as HTTP 200 with a non-200 `code`; surface those as errors. */
function unwrap<T>(body: { code?: number; message?: string; data?: T }): T {
  if (body?.code !== undefined && body.code !== 200) throw new Error(body.message || `Request failed (${body.code})`)
  return body.data as T
}

export async function getSuspicionEvents(query: SuspicionQuery = {}): Promise<SuspicionListResponse> {
  const response = await apiClient.get('/api/suspicion', { params: query })
  return unwrap<SuspicionListResponse>(response.data)
}

/** Mark rows reviewed (or back to unreviewed). Returns how many rows changed. */
export async function reviewSuspicionEvents(ids: number[], reviewed = true, note?: string): Promise<number> {
  const response = await apiClient.post('/api/suspicion/review', { ids, reviewed, note })
  return unwrap<{ updated: number }>(response.data).updated
}

export async function getSuspicionSettings(): Promise<SuspicionSettings> {
  const response = await apiClient.get('/api/suspicion/settings')
  return unwrap<SuspicionSettings>(response.data)
}

/** Saves and returns the settings as the server normalized them (clamped numbers, cleaned lists). */
export async function updateSuspicionSettings(settings: SuspicionSettings): Promise<SuspicionSettings> {
  const response = await apiClient.put('/api/suspicion/settings', settings)
  return unwrap<SuspicionSettings>(response.data)
}

/** Parses an event's details JSON; an empty object when there are none or they don't parse. */
export function parseSuspicionDetails(evt: Pick<SuspicionEvent, 'details'>): Record<string, unknown> {
  if (!evt.details) return {}
  try {
    const parsed = JSON.parse(evt.details)
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {}
  } catch {
    return {}
  }
}
