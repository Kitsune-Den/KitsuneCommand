-- KitsuneCommand Initial Schema
-- Settings (key-value configuration store)
CREATE TABLE IF NOT EXISTS settings (
    name TEXT PRIMARY KEY NOT NULL,
    value TEXT
);

-- Chat records
CREATE TABLE IF NOT EXISTS chat_records (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    player_id TEXT,
    entity_id INTEGER NOT NULL,
    sender_name TEXT NOT NULL,
    chat_type INTEGER NOT NULL,
    message TEXT
);
CREATE INDEX IF NOT EXISTS idx_chat_records_player_id ON chat_records(player_id);
CREATE INDEX IF NOT EXISTS idx_chat_records_sender_name ON chat_records(sender_name);
CREATE INDEX IF NOT EXISTS idx_chat_records_created_at ON chat_records(created_at);

-- Points system
CREATE TABLE IF NOT EXISTS points_info (
    id TEXT PRIMARY KEY NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    player_name TEXT,
    points INTEGER NOT NULL DEFAULT 0,
    last_sign_in_at TEXT
);

-- Teleport: City locations
CREATE TABLE IF NOT EXISTS city_locations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    city_name TEXT NOT NULL,
    points_required INTEGER NOT NULL DEFAULT 0,
    position TEXT NOT NULL,
    view_direction TEXT
);

-- Teleport: Home locations
CREATE TABLE IF NOT EXISTS home_locations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    player_id TEXT NOT NULL,
    player_name TEXT,
    home_name TEXT NOT NULL,
    position TEXT NOT NULL,
    UNIQUE(player_id, home_name)
);

-- Teleport: Records
CREATE TABLE IF NOT EXISTS tele_records (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    player_id TEXT NOT NULL,
    player_name TEXT,
    target_type INTEGER NOT NULL,
    target_name TEXT,
    origin_position TEXT,
    target_position TEXT
);
CREATE INDEX IF NOT EXISTS idx_tele_records_player ON tele_records(player_id);

-- Colored chat
CREATE TABLE IF NOT EXISTS colored_chat (
    id TEXT PRIMARY KEY NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    custom_name TEXT,
    name_color TEXT,
    text_color TEXT,
    description TEXT
);

-- Reusable: Command definitions
CREATE TABLE IF NOT EXISTS command_definitions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    command TEXT NOT NULL,
    run_in_main_thread INTEGER NOT NULL DEFAULT 0,
    description TEXT
);

-- Reusable: Item definitions
CREATE TABLE IF NOT EXISTS item_definitions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    item_name TEXT NOT NULL,
    count INTEGER NOT NULL DEFAULT 1,
    quality INTEGER NOT NULL DEFAULT 1,
    durability INTEGER NOT NULL DEFAULT 100,
    description TEXT
);

-- Game Store: Goods
CREATE TABLE IF NOT EXISTS goods (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    name TEXT NOT NULL,
    price INTEGER NOT NULL,
    description TEXT
);

CREATE TABLE IF NOT EXISTS goods_items (
    goods_id INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    PRIMARY KEY (goods_id, item_id),
    FOREIGN KEY (goods_id) REFERENCES goods(id) ON DELETE CASCADE,
    FOREIGN KEY (item_id) REFERENCES item_definitions(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS goods_commands (
    goods_id INTEGER NOT NULL,
    command_id INTEGER NOT NULL,
    PRIMARY KEY (goods_id, command_id),
    FOREIGN KEY (goods_id) REFERENCES goods(id) ON DELETE CASCADE,
    FOREIGN KEY (command_id) REFERENCES command_definitions(id) ON DELETE CASCADE
);

-- VIP Gifts
CREATE TABLE IF NOT EXISTS vip_gifts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    player_id TEXT NOT NULL,
    name TEXT NOT NULL,
    claimed INTEGER NOT NULL DEFAULT 0,
    total_claim_count INTEGER NOT NULL DEFAULT 0,
    last_claimed_at TEXT,
    description TEXT
);

CREATE TABLE IF NOT EXISTS vip_gift_items (
    vip_gift_id INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    PRIMARY KEY (vip_gift_id, item_id),
    FOREIGN KEY (vip_gift_id) REFERENCES vip_gifts(id) ON DELETE CASCADE,
    FOREIGN KEY (item_id) REFERENCES item_definitions(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS vip_gift_commands (
    vip_gift_id INTEGER NOT NULL,
    command_id INTEGER NOT NULL,
    PRIMARY KEY (vip_gift_id, command_id),
    FOREIGN KEY (vip_gift_id) REFERENCES vip_gifts(id) ON DELETE CASCADE,
    FOREIGN KEY (command_id) REFERENCES command_definitions(id) ON DELETE CASCADE
);

-- CD Keys
CREATE TABLE IF NOT EXISTS cd_keys (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    key TEXT NOT NULL UNIQUE,
    max_redeem_count INTEGER NOT NULL DEFAULT 1,
    expiry_at TEXT,
    description TEXT
);

CREATE TABLE IF NOT EXISTS cd_key_items (
    cd_key_id INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    PRIMARY KEY (cd_key_id, item_id),
    FOREIGN KEY (cd_key_id) REFERENCES cd_keys(id) ON DELETE CASCADE,
    FOREIGN KEY (item_id) REFERENCES item_definitions(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS cd_key_commands (
    cd_key_id INTEGER NOT NULL,
    command_id INTEGER NOT NULL,
    PRIMARY KEY (cd_key_id, command_id),
    FOREIGN KEY (cd_key_id) REFERENCES cd_keys(id) ON DELETE CASCADE,
    FOREIGN KEY (command_id) REFERENCES command_definitions(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS cd_key_redeem_records (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    cd_key_id INTEGER NOT NULL,
    player_id TEXT NOT NULL,
    player_name TEXT,
    UNIQUE(cd_key_id, player_id),
    FOREIGN KEY (cd_key_id) REFERENCES cd_keys(id) ON DELETE CASCADE
);

-- Task scheduling
CREATE TABLE IF NOT EXISTS task_schedules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    name TEXT NOT NULL UNIQUE,
    cron_expression TEXT NOT NULL,
    is_enabled INTEGER NOT NULL DEFAULT 1,
    last_run_at TEXT,
    description TEXT
);

CREATE TABLE IF NOT EXISTS task_schedule_commands (
    task_schedule_id INTEGER NOT NULL,
    command_id INTEGER NOT NULL,
    PRIMARY KEY (task_schedule_id, command_id),
    FOREIGN KEY (task_schedule_id) REFERENCES task_schedules(id) ON DELETE CASCADE,
    FOREIGN KEY (command_id) REFERENCES command_definitions(id) ON DELETE CASCADE
);
