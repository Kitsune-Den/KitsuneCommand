using Dapper;
using Microsoft.Data.Sqlite;

namespace KitsuneCommand.Data
{
    /// <summary>
    /// Initializes the database and runs SQL migrations in order.
    /// </summary>
    public static class DatabaseBootstrap
    {
        public static void Initialize(string databasePath, string modPath)
        {
            // Ensure directory exists
            var dbDir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
            }

            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();

            // Enable WAL mode and foreign keys
            connection.Execute("PRAGMA journal_mode=WAL;");
            connection.Execute("PRAGMA foreign_keys=ON;");

            // Create migrations tracking table
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS _migrations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    applied_at TEXT NOT NULL DEFAULT (datetime('now'))
                );
            ");

            // Run pending migrations
            ApplyMigrations(connection, modPath);
        }

        public static void ApplyMigrations(SqliteConnection connection, string modPath)
        {
            var migrationsDir = Path.Combine(modPath, "Config", "Migrations");
            if (!Directory.Exists(migrationsDir))
            {
                Log.Warning("[KitsuneCommand] Migrations directory not found: " + migrationsDir);
                return;
            }

            var sqlFiles = Directory.GetFiles(migrationsDir, "*.sql")
                                    .OrderBy(f => Path.GetFileName(f))
                                    .ToArray();

            var applied = connection.Query<string>("SELECT name FROM _migrations")
                                    .ToHashSet();

            foreach (var sqlFile in sqlFiles)
            {
                var migrationName = Path.GetFileName(sqlFile);
                if (applied.Contains(migrationName))
                    continue;

                Log.Out($"[KitsuneCommand] Applying migration: {migrationName}");

                var sql = File.ReadAllText(sqlFile);
                try
                {
                    connection.Execute(sql);
                    connection.Execute(
                        "INSERT INTO _migrations (name) VALUES (@Name)",
                        new { Name = migrationName }
                    );
                    Log.Out($"[KitsuneCommand] Migration applied: {migrationName}");
                }
                catch (Exception ex)
                {
                    Log.Error($"[KitsuneCommand] Migration failed: {migrationName} - {ex.Message}");
                    throw;
                }
            }
        }
    }
}
