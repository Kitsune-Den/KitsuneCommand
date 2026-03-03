-- Purchase history for the game store
CREATE TABLE IF NOT EXISTS purchase_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    player_id TEXT NOT NULL,
    player_name TEXT,
    goods_id INTEGER NOT NULL,
    goods_name TEXT NOT NULL,
    price INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_purchase_history_player_id ON purchase_history(player_id);
CREATE INDEX IF NOT EXISTS idx_purchase_history_created_at ON purchase_history(created_at);
