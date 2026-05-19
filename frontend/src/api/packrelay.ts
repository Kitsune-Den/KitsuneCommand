// API client for the PackRelay publish flow. Mirrors the
// PackRelayController endpoint shapes — every method maps 1:1 to
// a controller route.
//
// Polling pattern: getJob() is called every ~1s while a publish is
// in flight; the view drops the interval the moment the job's
// status is anything other than "Running".

import apiClient from './client'

/**
 * Wire-safe status of the panel's PackRelay credentials. Never
 * carries plaintext (see PackRelaySettingsService.GetStatus on
 * the backend for the redaction contract).
 */
export interface PackRelayStatus {
  hasApiToken: boolean
  hasSigningKey: boolean
  signingKeyPublic: string | null
  publicKeyId: string | null
  publisherSlug: string | null
  updatedAt: string | null
}

/**
 * Save any subset. Pass null/undefined for fields you don't want to
 * change. Sending an empty string for apiToken or signingKeyBase64
 * is treated as "do not touch" on the backend — to clear a single
 * field, DELETE the whole row first.
 */
export interface SaveSettingsPayload {
  apiToken?: string
  signingKeyBase64?: string
  publicKeyId?: string
  publisherSlug?: string
}

export type PublishPhase =
  | 'Walking'
  | 'Hashing'
  | 'Uploading'
  | 'Signing'
  | 'Posting'
  | 'Done'

export interface PublishProgressDto {
  phase: PublishPhase
  filesDone: number
  filesTotal: number
  bytesDone: number
  bytesTotal: number
  currentFile: string | null
}

export interface PublishResultDto {
  slug: string
  version: string
  fileCount: number
  totalSize: number
  alreadyPublished: boolean
}

export type PublishJobStatus = 'Running' | 'Done' | 'Error' | 'Cancelled'

export interface PublishJobSnapshot {
  jobId: string
  modpackId: number
  status: PublishJobStatus
  latestProgress: PublishProgressDto | null
  result: PublishResultDto | null
  errorMessage: string | null
  errorCode: string | null
  startedAtUtc: string
  updatedAtUtc: string
}

export interface PublishStartResponse {
  jobId: string
  modpackId: number
}

// ─── Settings ────────────────────────────────────────────────────────

export async function getPackRelaySettings(): Promise<PackRelayStatus> {
  const r = await apiClient.get('/api/packrelay/settings')
  return r.data.data
}

export async function savePackRelaySettings(
  payload: SaveSettingsPayload
): Promise<PackRelayStatus> {
  const r = await apiClient.post('/api/packrelay/settings', payload)
  return r.data.data
}

export async function resetPackRelaySettings(): Promise<void> {
  await apiClient.delete('/api/packrelay/settings')
}

// ─── Publish ─────────────────────────────────────────────────────────

export async function startPublishToPackRelay(
  modpackId: number
): Promise<PublishStartResponse> {
  const r = await apiClient.post(`/api/packrelay/publish/${modpackId}`)
  return r.data.data
}

export async function getPublishJob(
  jobId: string
): Promise<PublishJobSnapshot> {
  const r = await apiClient.get(`/api/packrelay/jobs/${jobId}`)
  return r.data.data
}

// ─── Curator handoff (#152) ──────────────────────────────────────────

export interface DraftSeedResponse {
  /** One-shot URL on packrelay.cloud the user opens in their default
   *  browser. Carries an opaque token; we don't track it client-side
   *  ~ the cloud handles claim + expiry. */
  url: string
  /** ISO 8601 timestamp when the seed becomes unclaimable. */
  expiresAt: string
  /** How many installed mods went into the seed payload. The button
   *  surface uses this in the success toast so the admin sees
   *  exactly what got sent. */
  modCount: number
}

/**
 * "Create new pack on packrelay.cloud" handoff. KC backend builds
 * the installed-mods snapshot, POSTs it anonymously to
 * packrelay.cloud, and hands back the claim URL. Caller is expected
 * to window.open() the URL straight away ~ the seed expires after
 * an hour even if the user never claims it.
 */
export async function createDraftSeed(): Promise<DraftSeedResponse> {
  const r = await apiClient.post('/api/packrelay/draft-seed')
  return r.data.data
}
