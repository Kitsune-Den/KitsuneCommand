using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using KitsuneCommand.Core;
using KitsuneCommand.Data;
// ModEntry used for deriving game root path

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Manages world save backups: create, restore, delete, and schedule auto-backups.
    /// Stores backup metadata in SQLite for the management UI.
    ///
    /// Connection lifetime note: <see cref="DbConnectionFactory.CreateConnection"/>
    /// returns an *already-opened* connection. Do NOT call <c>conn.Open()</c> again
    /// inside the using block. The custom-built System.Data.SQLite that ships with
    /// this mod silently drops subsequent INSERT/UPDATE statements when Open is
    /// invoked twice — no exception is thrown, the controller returns 200, but no
    /// row ends up in the table. (Reads happen to keep working, which is why the
    /// bug went unnoticed long enough to ship a UI for the feature.) Always trust
    /// the factory to return a ready-to-use connection.
    /// </summary>
    public class BackupService
    {
        private readonly DbConnectionFactory _db;
        private readonly IModEventBus _eventBus;
        private Timer _scheduleTimer;
        private BackupSettings _settings;
        private bool _isRunning;

        public BackupSettings Settings => _settings;

        public BackupService(DbConnectionFactory db, IModEventBus eventBus)
        {
            _db = db;
            _eventBus = eventBus;
            _settings = new BackupSettings();
        }

        /// <summary>
        /// Initialize backup service: load settings and start scheduler if enabled.
        /// </summary>
        public void Initialize()
        {
            LoadSettings();
            if (_settings.Enabled)
                StartScheduler();
        }

        /// <summary>
        /// Gets the world save directory path.
        /// </summary>
        public string GetSaveGameDir()
        {
            try
            {
                var saveDir = GameIO.GetSaveGameDir();
                if (!string.IsNullOrEmpty(saveDir) && Directory.Exists(saveDir))
                    return saveDir;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets the backup storage directory, creating it if needed.
        /// </summary>
        public string GetBackupDir()
        {
            var backupDir = _settings.BackupPath;
            if (!Path.IsPathRooted(backupDir))
            {
                var gameDir = Path.GetFullPath(Path.Combine(ModEntry.ModPath, "..", ".."));
                backupDir = Path.Combine(gameDir, backupDir);
            }

            Directory.CreateDirectory(backupDir);
            return backupDir;
        }

        /// <summary>
        /// Creates a backup ZIP of the current world save.
        /// </summary>
        public BackupRecord CreateBackup(string backupType = "manual", string notes = null)
        {
            var saveDir = GetSaveGameDir();
            if (saveDir == null)
                throw new InvalidOperationException("Save game directory not found.");

            var backupDir = GetBackupDir();
            var worldName = Path.GetFileName(saveDir);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"backup_{worldName}_{timestamp}.zip";
            var backupPath = Path.Combine(backupDir, filename);

            // Create ZIP archive of the save directory
            ZipFile.CreateFromDirectory(saveDir, backupPath, CompressionLevel.Optimal, false);

            var fileInfo = new FileInfo(backupPath);
            var record = new BackupRecord
            {
                Filename = filename,
                WorldName = worldName,
                SizeBytes = fileInfo.Length,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                BackupType = backupType,
                Notes = notes
            };

            // Save to database
            using (var conn = _db.CreateConnection())
            {
                Dapper.SqlMapper.Execute(conn,
                    @"INSERT INTO backups (filename, world_name, size_bytes, created_at, backup_type, notes)
                      VALUES (@Filename, @WorldName, @SizeBytes, @CreatedAt, @BackupType, @Notes)",
                    record);

                record.Id = (int)Dapper.SqlMapper.ExecuteScalar<long>(conn,
                    "SELECT last_insert_rowid()");
            }

            // Auto-prune old backups
            PruneBackups();

            return record;
        }

        /// <summary>
        /// Restores a backup by extracting it over the current save directory.
        /// Creates a safety backup of the current state first.
        /// </summary>
        public void RestoreBackup(int backupId)
        {
            var record = GetBackup(backupId);
            if (record == null)
                throw new FileNotFoundException("Backup record not found.");

            var backupDir = GetBackupDir();
            var backupPath = Path.Combine(backupDir, record.Filename);
            if (!File.Exists(backupPath))
                throw new FileNotFoundException($"Backup file '{record.Filename}' not found on disk.");

            var saveDir = GetSaveGameDir();
            if (saveDir == null)
                throw new InvalidOperationException("Save game directory not found.");

            // Create safety backup before restoring
            CreateBackup("pre-restore", $"Auto-backup before restoring {record.Filename}");

            // Clear the save directory and extract backup
            foreach (var file in Directory.GetFiles(saveDir, "*", SearchOption.AllDirectories))
                File.Delete(file);

            ZipFile.ExtractToDirectory(backupPath, saveDir);
        }

        /// <summary>
        /// Deletes a backup file and its database record.
        /// </summary>
        public void DeleteBackup(int backupId)
        {
            var record = GetBackup(backupId);
            if (record == null)
                throw new FileNotFoundException("Backup not found.");

            // Delete file
            var backupPath = Path.Combine(GetBackupDir(), record.Filename);
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            // Delete record
            using (var conn = _db.CreateConnection())
            {
                Dapper.SqlMapper.Execute(conn,
                    "DELETE FROM backups WHERE id = @Id",
                    new { Id = backupId });
            }
        }

        /// <summary>
        /// Gets all backup records.
        /// </summary>
        public List<BackupRecord> GetAll()
        {
            using (var conn = _db.CreateConnection())
            {
                return Dapper.SqlMapper.Query<BackupRecord>(conn,
                    "SELECT id as Id, filename as Filename, world_name as WorldName, size_bytes as SizeBytes, " +
                    "created_at as CreatedAt, backup_type as BackupType, notes as Notes " +
                    "FROM backups ORDER BY created_at DESC")
                    .ToList();
            }
        }

        /// <summary>
        /// Gets a single backup record by ID.
        /// </summary>
        public BackupRecord GetBackup(int id)
        {
            using (var conn = _db.CreateConnection())
            {
                return Dapper.SqlMapper.QueryFirstOrDefault<BackupRecord>(conn,
                    "SELECT id as Id, filename as Filename, world_name as WorldName, size_bytes as SizeBytes, " +
                    "created_at as CreatedAt, backup_type as BackupType, notes as Notes " +
                    "FROM backups WHERE id = @Id",
                    new { Id = id });
            }
        }

        /// <summary>
        /// Updates backup schedule settings and restarts the scheduler.
        /// </summary>
        public void UpdateSettings(BackupSettings settings)
        {
            _settings = settings;
            SaveSettings();

            StopScheduler();
            if (_settings.Enabled)
                StartScheduler();
        }

        /// <summary>
        /// Loads settings from the database.
        /// </summary>
        public void LoadSettings()
        {
            try
            {
                using (var conn = _db.CreateConnection())
                {
                        var json = Dapper.SqlMapper.QueryFirstOrDefault<string>(conn,
                        "SELECT value FROM settings WHERE name = 'backup_settings'");

                    if (!string.IsNullOrEmpty(json))
                        _settings = Newtonsoft.Json.JsonConvert.DeserializeObject<BackupSettings>(json) ?? new BackupSettings();
                }
            }
            catch
            {
                _settings = new BackupSettings();
            }
        }

        private void SaveSettings()
        {
            using (var conn = _db.CreateConnection())
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_settings);
                Dapper.SqlMapper.Execute(conn,
                    @"INSERT OR REPLACE INTO settings (name, value) VALUES ('backup_settings', @Value)",
                    new { Value = json });
            }
        }

        private void StartScheduler()
        {
            if (_isRunning) return;
            _isRunning = true;

            var intervalMs = _settings.IntervalMinutes * 60 * 1000;
            _scheduleTimer = new Timer(_ =>
            {
                try
                {
                    CreateBackup("scheduled");
                    global::Log.Out("[KitsuneCommand] Scheduled backup completed.");
                }
                catch (Exception ex)
                {
                    global::Log.Error($"[KitsuneCommand] Scheduled backup failed: {ex.Message}");
                }
            }, null, intervalMs, intervalMs);

            global::Log.Out($"[KitsuneCommand] Backup scheduler started (every {_settings.IntervalMinutes} min).");
        }

        private void StopScheduler()
        {
            _isRunning = false;
            _scheduleTimer?.Dispose();
            _scheduleTimer = null;
        }

        /// <summary>
        /// Removes oldest backups when exceeding MaxBackups limit.
        /// </summary>
        private void PruneBackups()
        {
            if (_settings.MaxBackups <= 0) return;

            var all = GetAll();
            if (all.Count <= _settings.MaxBackups) return;

            // Delete oldest beyond limit
            var toDelete = all.Skip(_settings.MaxBackups).ToList();
            foreach (var record in toDelete)
            {
                try
                {
                    DeleteBackup(record.Id);
                }
                catch { /* Log but don't fail */ }
            }
        }

        public void Shutdown()
        {
            StopScheduler();
        }
    }

    public class BackupSettings
    {
        public bool Enabled { get; set; } = false;
        public int IntervalMinutes { get; set; } = 60;
        public int MaxBackups { get; set; } = 10;
        public string BackupPath { get; set; } = "KitsuneBackups";
    }

    public class BackupRecord
    {
        public int Id { get; set; }
        public string Filename { get; set; }
        public string WorldName { get; set; }
        public long SizeBytes { get; set; }
        public string CreatedAt { get; set; }
        public string BackupType { get; set; }
        public string Notes { get; set; }
    }
}
