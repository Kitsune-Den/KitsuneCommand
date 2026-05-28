import apiClient from './client'

/**
 * One event in a client's connection lifecycle on the server side, captured
 * by KitsuneCommand's AuthWrapperServerDiagnostics Harmony patches. One
 * "click Direct Connect" typically produces 10-30 of these as LiteNetLib
 * retries and the auth-state state machine transitions — bursts that the
 * panel renders as one logical "join attempt" by grouping on peerIp:peerPort.
 *
 * Matches the JSON shape returned by the C# `JoinAttemptEvent` (see
 * `KitsuneCommand/Diagnostics/JoinAttemptEvent.cs`).
 */
export interface JoinAttemptEvent {
  /** UTC ISO-8601 timestamp the event was recorded. */
  timestamp: string

  /** One of: ConnReq, Recv, Conn, Disc, Update. */
  eventType: string

  /** Source IP. Null for Update events. */
  peerIp: string | null

  /** Source port. Null for Update events. */
  peerPort: number | null

  /**
   * For ConnReq: Accept / Reject / RejectForce / None.
   * For Disc: LiteNetLib DisconnectReason name (PeerNotFound, Timeout,
   * DisconnectPeerCalled, etc.) — the field operators most care about.
   * Null otherwise.
   */
  result: string | null

  /**
   * ConnReq payload size in bytes. 2 = bare LiteNetLib version handshake,
   * 0 = wrapper already consumed it (Accept happened). Null otherwise.
   */
  dataBytes: number | null

  /** Channel byte for Recv events. */
  channel: number | null

  /** ReliableOrdered / Unreliable / ... for Recv events. */
  deliveryMethod: string | null

  /** Size of disconnect packet's optional payload (Disc events only). */
  extraDataBytes: number | null

  /** authStates dict size at the moment of this event. */
  authStateCount: number | null
}

export interface JoinAttemptListResponse {
  events: JoinAttemptEvent[]
  /** Process-lifetime monotonic counter — useful for "Hey, the ring is filling fast". */
  totalRecorded: number
  /** Whether [KC-NetDiag] verbose console logging is currently on. */
  verboseLogging: boolean
  /** Ring buffer capacity (events older than this get overwritten). */
  capacity: number
}

/**
 * Fetch up to `limit` most-recent events, optionally only those at or after
 * `since` (ISO-8601). Returns newest-first.
 */
export async function getJoinAttempts(
  limit: number = 100,
  since: string | null = null
): Promise<JoinAttemptListResponse> {
  const params: Record<string, string | number> = { limit }
  if (since) params.since = since
  const res = await apiClient.get('/api/join-attempts', { params })
  return res.data.data
}

/** Empty the ring buffer. Doesn't reset the monotonic totalRecorded counter. */
export async function clearJoinAttempts(): Promise<string> {
  const res = await apiClient.post('/api/join-attempts/clear')
  return res.data.message
}

/**
 * Turn the [KC-NetDiag] verbose console logging on or off at runtime.
 * Ring-buffer recording is unaffected (it's always on).
 */
export async function setVerboseLogging(enabled: boolean): Promise<{ enabled: boolean }> {
  const res = await apiClient.post('/api/join-attempts/verbose', { enabled })
  return res.data.data
}
