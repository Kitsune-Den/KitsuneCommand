-- Vote rewards audit log + idempotency table.
--
-- Players vote for the server on listing sites (7daystodie-servers.com, etc.),
-- and the VoteRewards feature polls each configured provider for unclaimed votes
-- and grants a configured reward. This table is both:
--
--   1. The audit log — every grant is recorded with what was given, when,
--      and which provider/site triggered it.
--
--   2. The idempotency guard — two polls racing (or a sweep + a /vote command)
--      must not double-grant. The (provider, steam_id, vote_date) unique index
--      makes the second insert fail, and we skip the grant on conflict.
--
-- vote_date is the YYYY-MM-DD of the grant (UTC); most listing sites have
-- a one-vote-per-day-per-player cadence, so day-granularity is the natural
-- key. If a provider ever supports a true vote_id we can extend this.
CREATE TABLE IF NOT EXISTS vote_grants (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    provider      TEXT    NOT NULL,         -- e.g. "7daystodie-servers"
    steam_id      TEXT    NOT NULL,         -- the SteamID64 the listing site reported
    player_name   TEXT,                     -- best-effort, may be null if player never connected
    vote_date     TEXT    NOT NULL,         -- YYYY-MM-DD UTC, idempotency component
    reward_type   TEXT    NOT NULL,         -- "points" | "vip_gift" | "cd_key"
    reward_value  TEXT    NOT NULL,         -- type-specific payload (points amount, gift id, key template)
    granted_at    TEXT    NOT NULL DEFAULT (datetime('now')),
    notes         TEXT                      -- free-form, e.g. "queued for offline player"
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_vote_grants_dedup
    ON vote_grants (provider, steam_id, vote_date);

CREATE INDEX IF NOT EXISTS idx_vote_grants_steam
    ON vote_grants (steam_id);

CREATE INDEX IF NOT EXISTS idx_vote_grants_granted_at
    ON vote_grants (granted_at);
