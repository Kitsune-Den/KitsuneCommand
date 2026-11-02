-- Suspicion log: server-side signs of cheating, recorded for admins to review.
-- Log-only. Nothing here kicks or bans; a row is a lead, not a verdict.
--
-- detector = inventory | movement | progression | killrate
-- rule     = the specific check inside the detector (creative_item, over_stack,
--            speed, flying, level_jump, kill_rate, ...)
-- details  = JSON with the numbers behind the flag (item, counts, speed, ...)
-- pos_*    = where the player was when it was flagged, when known
CREATE TABLE IF NOT EXISTS suspicion_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    player_id TEXT,
    player_name TEXT,
    entity_id INTEGER,
    detector TEXT NOT NULL,
    rule TEXT NOT NULL,
    severity TEXT NOT NULL DEFAULT 'low',
    summary TEXT,
    details TEXT,
    pos_x REAL,
    pos_y REAL,
    pos_z REAL,
    reviewed INTEGER NOT NULL DEFAULT 0,
    reviewed_by TEXT,
    reviewed_at TEXT,
    review_note TEXT
);

CREATE INDEX IF NOT EXISTS idx_suspicion_events_created_at ON suspicion_events(created_at);
CREATE INDEX IF NOT EXISTS idx_suspicion_events_player_id ON suspicion_events(player_id);
CREATE INDEX IF NOT EXISTS idx_suspicion_events_reviewed ON suspicion_events(reviewed, created_at);
