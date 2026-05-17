using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IPackRelaySettingsRepository
    {
        /// <summary>The one-and-only settings row, or null if never saved.</summary>
        PackRelaySettings GetCurrent();

        /// <summary>
        /// Insert if no row exists, otherwise update the existing
        /// row in place. Same singleton-via-repo pattern the
        /// modpack table uses.
        /// </summary>
        PackRelaySettings Upsert(PackRelaySettings record);

        /// <summary>Wipe the row entirely. Used by the "rotate/reset" UI path.</summary>
        void Delete();
    }

    public class PackRelaySettingsRepository : IPackRelaySettingsRepository
    {
        private readonly DbConnectionFactory _db;

        public PackRelaySettingsRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public PackRelaySettings GetCurrent()
        {
            using (var conn = _db.CreateConnection())
            {
                return conn.QueryFirstOrDefault<PackRelaySettings>(
                    "SELECT * FROM pack_relay_settings ORDER BY id DESC LIMIT 1");
            }
        }

        public PackRelaySettings Upsert(PackRelaySettings record)
        {
            using (var conn = _db.CreateConnection())
            {
                if (record.Id == 0)
                {
                    var newId = conn.ExecuteScalar<long>(@"
                        INSERT INTO pack_relay_settings
                            (api_token_encrypted, signing_key_encrypted, signing_key_public, public_key_id, publisher_slug, updated_at)
                        VALUES
                            (@ApiTokenEncrypted, @SigningKeyEncrypted, @SigningKeyPublic, @PublicKeyId, @PublisherSlug, datetime('now'));
                        SELECT last_insert_rowid();", record);
                    record.Id = (int)newId;
                }
                else
                {
                    conn.Execute(@"
                        UPDATE pack_relay_settings
                           SET api_token_encrypted   = @ApiTokenEncrypted,
                               signing_key_encrypted = @SigningKeyEncrypted,
                               signing_key_public    = @SigningKeyPublic,
                               public_key_id         = @PublicKeyId,
                               publisher_slug        = @PublisherSlug,
                               updated_at            = datetime('now')
                         WHERE id = @Id", record);
                }
                return record;
            }
        }

        public void Delete()
        {
            using (var conn = _db.CreateConnection())
            {
                conn.Execute("DELETE FROM pack_relay_settings");
            }
        }
    }
}
