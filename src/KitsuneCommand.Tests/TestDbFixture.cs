using System;
using System.Data.SQLite;
using System.IO;
using Dapper;
using KitsuneCommand.Data;

namespace KitsuneCommand.Tests
{
    /// <summary>
    /// Provides a temporary SQLite database with the full KitsuneCommand schema
    /// for integration testing of repositories.
    /// </summary>
    public static class TestDbFixture
    {
        /// <summary>
        /// Creates a temp database file and applies all schema migrations.
        /// Returns the path — caller is responsible for deleting it after tests.
        /// </summary>
        public static string CreateTempDatabase()
        {
            var path = Path.Combine(Path.GetTempPath(), $"kc_test_{Guid.NewGuid():N}.db");
            InitializeSchema(path);
            return path;
        }

        /// <summary>
        /// Initializes the database at the given path with the full schema.
        /// </summary>
        public static void InitializeSchema(string databasePath)
        {
            // Match the Dapper config from DatabaseBootstrap
            DefaultTypeMap.MatchNamesWithUnderscores = true;

            using var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
            connection.Open();
            connection.Execute("PRAGMA journal_mode=WAL;");
            connection.Execute("PRAGMA foreign_keys=ON;");

            // 001_initial_schema.sql
            connection.Execute(Schema001);

            // 002_user_accounts.sql
            connection.Execute(Schema002);

            // 003_purchase_history.sql
            connection.Execute(Schema003);

            // 004_vip_gift_enhancements.sql — ALTER TABLE statements
            try { connection.Execute(Schema004_ClaimPeriod); } catch { /* Column may already exist */ }
            try { connection.Execute(Schema004_PlayerName); } catch { /* Column may already exist */ }

            // 005_task_schedule_interval.sql — ALTER TABLE
            try { connection.Execute(Schema005); } catch { /* Column may already exist */ }

            // 006_backups.sql
            connection.Execute(Schema006);

            // 011_packrelay.sql — singleton encrypted settings row for
            // the PackRelay publish flow. Migrations 007-010 are
            // covered by other tests / aren't needed here, so we
            // skip directly to 011 (the test DB doesn't need a
            // continuous-migration property; it just needs the
            // tables the test under exercise references).
            connection.Execute(Schema011);
        }

        /// <summary>
        /// Creates a DbConnectionFactory pointing to the given database path.
        /// </summary>
        public static DbConnectionFactory CreateFactory(string databasePath)
        {
            return new DbConnectionFactory(databasePath);
        }

        /// <summary>
        /// Deletes the temp database file. Safe to call even if file doesn't exist.
        /// </summary>
        public static void Cleanup(string databasePath)
        {
            try
            {
                // Force SQLite to release the file
                SQLiteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                if (File.Exists(databasePath))
                    File.Delete(databasePath);

                // Also clean up WAL and SHM files
                var walPath = databasePath + "-wal";
                var shmPath = databasePath + "-shm";
                if (File.Exists(walPath)) File.Delete(walPath);
                if (File.Exists(shmPath)) File.Delete(shmPath);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        #region Schema SQL

        private const string Schema001 = @"
CREATE TABLE IF NOT EXISTS settings (
    name TEXT PRIMARY KEY NOT NULL,
    value TEXT
);

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

CREATE TABLE IF NOT EXISTS points_info (
    id TEXT PRIMARY KEY NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    player_name TEXT,
    points INTEGER NOT NULL DEFAULT 0,
    last_sign_in_at TEXT
);

CREATE TABLE IF NOT EXISTS city_locations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    city_name TEXT NOT NULL,
    points_required INTEGER NOT NULL DEFAULT 0,
    position TEXT NOT NULL,
    view_direction TEXT
);

CREATE TABLE IF NOT EXISTS home_locations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    player_id TEXT NOT NULL,
    player_name TEXT,
    home_name TEXT NOT NULL,
    position TEXT NOT NULL,
    UNIQUE(player_id, home_name)
);

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

CREATE TABLE IF NOT EXISTS colored_chat (
    id TEXT PRIMARY KEY NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    custom_name TEXT,
    name_color TEXT,
    text_color TEXT,
    description TEXT
);

CREATE TABLE IF NOT EXISTS command_definitions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    command TEXT NOT NULL,
    run_in_main_thread INTEGER NOT NULL DEFAULT 0,
    description TEXT
);

CREATE TABLE IF NOT EXISTS item_definitions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    item_name TEXT NOT NULL,
    count INTEGER NOT NULL DEFAULT 1,
    quality INTEGER NOT NULL DEFAULT 1,
    durability INTEGER NOT NULL DEFAULT 100,
    description TEXT
);

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
";

        private const string Schema002 = @"
CREATE TABLE IF NOT EXISTS user_accounts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    display_name TEXT,
    role TEXT NOT NULL DEFAULT 'admin',
    last_login_at TEXT,
    is_active INTEGER NOT NULL DEFAULT 1
);
";

        private const string Schema003 = @"
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
";

        private const string Schema004_ClaimPeriod =
            "ALTER TABLE vip_gifts ADD COLUMN claim_period TEXT;";

        private const string Schema004_PlayerName =
            "ALTER TABLE vip_gifts ADD COLUMN player_name TEXT;";

        private const string Schema005 =
            "ALTER TABLE task_schedules ADD COLUMN interval_minutes INTEGER NOT NULL DEFAULT 60;";

        private const string Schema006 = @"
CREATE TABLE IF NOT EXISTS backups (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    filename TEXT NOT NULL,
    world_name TEXT NOT NULL,
    size_bytes INTEGER NOT NULL,
    created_at TEXT NOT NULL,
    backup_type TEXT NOT NULL DEFAULT 'manual',
    notes TEXT
);
";

        // Mirrors Config/Migrations/011_packrelay.sql verbatim. Update
        // both when the schema changes.
        private const string Schema011 = @"
CREATE TABLE IF NOT EXISTS pack_relay_settings (
    id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    api_token_encrypted   BLOB,
    signing_key_encrypted BLOB,
    signing_key_public    TEXT,
    public_key_id         TEXT,
    publisher_slug        TEXT,
    updated_at            TEXT NOT NULL DEFAULT (datetime('now'))
);
";

        #endregion
    }
}
