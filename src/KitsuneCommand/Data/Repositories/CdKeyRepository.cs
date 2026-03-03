using Dapper;
using KitsuneCommand.Data.Entities;
using CommandDefinition = KitsuneCommand.Data.Entities.CommandDefinition;

namespace KitsuneCommand.Data.Repositories
{
    public interface ICdKeyRepository
    {
        // CRUD
        IEnumerable<CdKey> GetAll(int pageIndex, int pageSize, string search = null);
        int GetTotalCount(string search = null);
        CdKey GetById(int id);
        CdKey GetByKey(string key);
        int Insert(CdKey cdKey);
        void Update(CdKey cdKey);
        void Delete(int id);

        // Junction tables
        IEnumerable<ItemDefinition> GetItemsForKey(int cdKeyId);
        IEnumerable<CommandDefinition> GetCommandsForKey(int cdKeyId);
        void SetKeyItems(int cdKeyId, IEnumerable<int> itemDefinitionIds);
        void SetKeyCommands(int cdKeyId, IEnumerable<int> commandDefinitionIds);

        // Redemption
        int GetRedeemCount(int cdKeyId);
        bool HasPlayerRedeemed(int cdKeyId, string playerId);
        void InsertRedeemRecord(CdKeyRedeemRecord record);
        IEnumerable<CdKeyRedeemRecord> GetRedeemRecords(int cdKeyId);

        // Redemption history (all keys)
        IEnumerable<CdKeyRedeemRecord> GetRedeemHistory(int pageIndex, int pageSize, int? cdKeyId = null);
        int GetRedeemHistoryCount(int? cdKeyId = null);
    }

    public class CdKeyRepository : ICdKeyRepository
    {
        private readonly DbConnectionFactory _db;

        public CdKeyRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        // ─── CRUD ────────────────────────────────────────────────

        public IEnumerable<CdKey> GetAll(int pageIndex, int pageSize, string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search)
                ? ""
                : "WHERE key LIKE @Search OR description LIKE @Search";
            return conn.Query<CdKey>(
                $"SELECT * FROM cd_keys {where} ORDER BY id DESC LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, Search = $"%{search}%" });
        }

        public int GetTotalCount(string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search)
                ? ""
                : "WHERE key LIKE @Search OR description LIKE @Search";
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM cd_keys {where}",
                new { Search = $"%{search}%" });
        }

        public CdKey GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<CdKey>(
                "SELECT * FROM cd_keys WHERE id = @Id", new { Id = id });
        }

        public CdKey GetByKey(string key)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<CdKey>(
                "SELECT * FROM cd_keys WHERE key = @Key", new { Key = key });
        }

        public int Insert(CdKey cdKey)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO cd_keys (key, max_redeem_count, expiry_at, description)
                VALUES (@Key, @MaxRedeemCount, @ExpiryAt, @Description);
                SELECT last_insert_rowid();", cdKey);
        }

        public void Update(CdKey cdKey)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE cd_keys
                SET key = @Key, max_redeem_count = @MaxRedeemCount,
                    expiry_at = @ExpiryAt, description = @Description
                WHERE id = @Id", cdKey);
        }

        public void Delete(int id)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM cd_keys WHERE id = @Id", new { Id = id });
        }

        // ─── Junction Tables ─────────────────────────────────────

        public IEnumerable<ItemDefinition> GetItemsForKey(int cdKeyId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<ItemDefinition>(@"
                SELECT d.* FROM item_definitions d
                INNER JOIN cd_key_items cki ON cki.item_id = d.id
                WHERE cki.cd_key_id = @CdKeyId
                ORDER BY d.item_name",
                new { CdKeyId = cdKeyId });
        }

        public IEnumerable<CommandDefinition> GetCommandsForKey(int cdKeyId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CommandDefinition>(@"
                SELECT d.* FROM command_definitions d
                INNER JOIN cd_key_commands ckc ON ckc.command_id = d.id
                WHERE ckc.cd_key_id = @CdKeyId
                ORDER BY d.command",
                new { CdKeyId = cdKeyId });
        }

        public void SetKeyItems(int cdKeyId, IEnumerable<int> itemDefinitionIds)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM cd_key_items WHERE cd_key_id = @CdKeyId",
                new { CdKeyId = cdKeyId });

            foreach (var itemId in itemDefinitionIds)
            {
                conn.Execute(
                    "INSERT INTO cd_key_items (cd_key_id, item_id) VALUES (@CdKeyId, @ItemId)",
                    new { CdKeyId = cdKeyId, ItemId = itemId });
            }
        }

        public void SetKeyCommands(int cdKeyId, IEnumerable<int> commandDefinitionIds)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM cd_key_commands WHERE cd_key_id = @CdKeyId",
                new { CdKeyId = cdKeyId });

            foreach (var commandId in commandDefinitionIds)
            {
                conn.Execute(
                    "INSERT INTO cd_key_commands (cd_key_id, command_id) VALUES (@CdKeyId, @CommandId)",
                    new { CdKeyId = cdKeyId, CommandId = commandId });
            }
        }

        // ─── Redemption ──────────────────────────────────────────

        public int GetRedeemCount(int cdKeyId)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM cd_key_redeem_records WHERE cd_key_id = @CdKeyId",
                new { CdKeyId = cdKeyId });
        }

        public bool HasPlayerRedeemed(int cdKeyId, string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM cd_key_redeem_records WHERE cd_key_id = @CdKeyId AND player_id = @PlayerId",
                new { CdKeyId = cdKeyId, PlayerId = playerId }) > 0;
        }

        public void InsertRedeemRecord(CdKeyRedeemRecord record)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                INSERT INTO cd_key_redeem_records (cd_key_id, player_id, player_name)
                VALUES (@CdKeyId, @PlayerId, @PlayerName)", record);
        }

        public IEnumerable<CdKeyRedeemRecord> GetRedeemRecords(int cdKeyId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CdKeyRedeemRecord>(
                "SELECT * FROM cd_key_redeem_records WHERE cd_key_id = @CdKeyId ORDER BY id DESC",
                new { CdKeyId = cdKeyId });
        }

        // ─── Redemption History ──────────────────────────────────

        public IEnumerable<CdKeyRedeemRecord> GetRedeemHistory(int pageIndex, int pageSize, int? cdKeyId = null)
        {
            using var conn = _db.CreateConnection();
            var where = cdKeyId.HasValue ? "WHERE cd_key_id = @CdKeyId" : "";
            return conn.Query<CdKeyRedeemRecord>(
                $"SELECT * FROM cd_key_redeem_records {where} ORDER BY id DESC LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, CdKeyId = cdKeyId });
        }

        public int GetRedeemHistoryCount(int? cdKeyId = null)
        {
            using var conn = _db.CreateConnection();
            var where = cdKeyId.HasValue ? "WHERE cd_key_id = @CdKeyId" : "";
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM cd_key_redeem_records {where}",
                new { CdKeyId = cdKeyId });
        }
    }
}
