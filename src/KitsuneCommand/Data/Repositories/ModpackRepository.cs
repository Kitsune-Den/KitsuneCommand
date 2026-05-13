using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IModpackRepository
    {
        /// <summary>The one and only modpack row, or null if none exists yet.</summary>
        Modpack GetCurrent();

        /// <summary>The modpack only if it's currently published, else null. Used by the public endpoint.</summary>
        Modpack GetPublished();

        /// <summary>
        /// Insert if no row exists, otherwise update the existing row in place.
        /// "Singleton" semantics — the table is structurally allowed to hold
        /// many rows, but this repo enforces one-and-only-one.
        /// </summary>
        Modpack Upsert(Modpack record);

        /// <summary>Increments download_count atomically. Cheap, doesn't return anything.</summary>
        void IncrementDownloadCount(int id);

        /// <summary>Removes the row entirely. The on-disk zip is the caller's responsibility.</summary>
        void Delete(int id);
    }

    public class ModpackRepository : IModpackRepository
    {
        private readonly DbConnectionFactory _db;

        public ModpackRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public Modpack GetCurrent()
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<Modpack>(
                "SELECT * FROM modpack ORDER BY id DESC LIMIT 1");
        }

        public Modpack GetPublished()
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<Modpack>(
                "SELECT * FROM modpack WHERE status = 'published' ORDER BY id DESC LIMIT 1");
        }

        public Modpack Upsert(Modpack record)
        {
            using var conn = _db.CreateConnection();
            if (record.Id == 0)
            {
                // Fresh row. updated_at gets the same value as created_at so
                // sort-by-mtime queries don't surface a "stale" pack the moment
                // it's created.
                var newId = conn.ExecuteScalar<long>(@"
                    INSERT INTO modpack
                        (name, version, status, filename, size_bytes, mod_count, mod_list, description, updated_at)
                    VALUES
                        (@Name, @Version, @Status, @Filename, @SizeBytes, @ModCount, @ModList, @Description, datetime('now'));
                    SELECT last_insert_rowid();", record);
                record.Id = (int)newId;
            }
            else
            {
                conn.Execute(@"
                    UPDATE modpack
                       SET name = @Name,
                           version = @Version,
                           status = @Status,
                           filename = @Filename,
                           size_bytes = @SizeBytes,
                           mod_count = @ModCount,
                           mod_list = @ModList,
                           description = @Description,
                           updated_at = datetime('now')
                     WHERE id = @Id", record);
            }
            return record;
        }

        public void IncrementDownloadCount(int id)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("UPDATE modpack SET download_count = download_count + 1 WHERE id = @Id",
                new { Id = id });
        }

        public void Delete(int id)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM modpack WHERE id = @Id", new { Id = id });
        }
    }
}
