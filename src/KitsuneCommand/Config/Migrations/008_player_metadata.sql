-- Player metadata for web panel customizations and in-game name colors
CREATE TABLE IF NOT EXISTS player_metadata (
    player_id TEXT PRIMARY KEY,
    name_color TEXT,
    custom_tag TEXT,
    notes TEXT,
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);
