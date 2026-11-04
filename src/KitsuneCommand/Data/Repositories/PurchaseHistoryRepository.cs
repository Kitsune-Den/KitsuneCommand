using System.Globalization;
using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IPurchaseHistoryRepository
    {
        void Insert(PurchaseRecord record);
        IEnumerable<PurchaseRecord> GetHistory(int pageIndex, int pageSize, string playerId = null);
        int GetTotalCount(string playerId = null);

        /// <summary>When the player last bought something (UTC), or null if they never have.</summary>
        DateTime? GetLastPurchaseUtc(string playerId);

        /// <summary>How many purchases the player has made at or after <paramref name="sinceUtc"/>.</summary>
        int CountSince(string playerId, DateTime sinceUtc);
    }

    public class PurchaseHistoryRepository : IPurchaseHistoryRepository
    {
        private readonly DbConnectionFactory _db;

        public PurchaseHistoryRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public void Insert(PurchaseRecord record)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                INSERT INTO purchase_history (player_id, player_name, goods_id, goods_name, price)
                VALUES (@PlayerId, @PlayerName, @GoodsId, @GoodsName, @Price)", record);
        }

        public IEnumerable<PurchaseRecord> GetHistory(int pageIndex, int pageSize, string playerId = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(playerId) ? "" : "WHERE player_id = @PlayerId";
            return conn.Query<PurchaseRecord>(
                $"SELECT * FROM purchase_history {where} ORDER BY id DESC LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, PlayerId = playerId });
        }

        public int GetTotalCount(string playerId = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(playerId) ? "" : "WHERE player_id = @PlayerId";
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM purchase_history {where}",
                new { PlayerId = playerId });
        }

        // created_at is SQLite's datetime('now'): UTC, "yyyy-MM-dd HH:mm:ss", so it sorts and
        // compares as text.
        private const string CreatedAtFormat = "yyyy-MM-dd HH:mm:ss";

        public DateTime? GetLastPurchaseUtc(string playerId)
        {
            using var conn = _db.CreateConnection();
            var createdAt = conn.ExecuteScalar<string>(
                "SELECT MAX(created_at) FROM purchase_history WHERE player_id = @PlayerId",
                new { PlayerId = playerId });
            if (string.IsNullOrEmpty(createdAt)) return null;

            return DateTime.ParseExact(createdAt, CreatedAtFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        public int CountSince(string playerId, DateTime sinceUtc)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM purchase_history WHERE player_id = @PlayerId AND created_at >= @Since",
                new { PlayerId = playerId, Since = sinceUtc.ToString(CreatedAtFormat, CultureInfo.InvariantCulture) });
        }
    }
}
