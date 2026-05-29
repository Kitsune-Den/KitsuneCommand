-- VIP tiers + first-login pack support (board #233, #234)

-- Classify players into admin-defined VIP tiers. null / empty = no tier ("pleb").
-- Tier names themselves live in the VipPerks feature settings, not here, so admins
-- can add/rename tiers without a migration. This column just records assignment.
ALTER TABLE player_metadata ADD COLUMN vip_tier TEXT;

-- One row per player, ever. Presence = the first-time-login item pack has already
-- been delivered to this player. PRIMARY KEY gives us the once-ever idempotency
-- guard for free (INSERT OR IGNORE returns 0 rows on a repeat spawn).
CREATE TABLE IF NOT EXISTS first_login_grants (
    player_id  TEXT PRIMARY KEY NOT NULL,
    player_name TEXT,
    granted_at TEXT NOT NULL DEFAULT (datetime('now'))
);
