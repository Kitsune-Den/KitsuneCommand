using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface ITeleRecordRepository
    {
        void Insert(TeleRecord record);
        IEnumerable<TeleRecord> GetHistory(int pageIndex, int pageSize, string playerId = null);
        int GetTotalCount(string playerId = null);
    }

    public class TeleRecordRepository : ITeleRecordRepository
    {
        private readonly DbConnectionFactory _db;

        public TeleRecordRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public void Insert(TeleRecord record)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                INSERT INTO tele_records (player_id, player_name, target_type, target_name, origin_position, target_position)
                VALUES (@PlayerId, @PlayerName, @TargetType, @TargetName, @OriginPosition, @TargetPosition)", record);
        }

        public IEnumerable<TeleRecord> GetHistory(int pageIndex, int pageSize, string playerId = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(playerId) ? "" : "WHERE player_id = @PlayerId";
            return conn.Query<TeleRecord>(
                $"SELECT * FROM tele_records {where} ORDER BY id DESC LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, PlayerId = playerId });
        }

        public int GetTotalCount(string playerId = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(playerId) ? "" : "WHERE player_id = @PlayerId";
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM tele_records {where}",
                new { PlayerId = playerId });
        }
    }
}
