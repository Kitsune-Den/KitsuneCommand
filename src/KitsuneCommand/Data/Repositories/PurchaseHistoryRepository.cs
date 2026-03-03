using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IPurchaseHistoryRepository
    {
        void Insert(PurchaseRecord record);
        IEnumerable<PurchaseRecord> GetHistory(int pageIndex, int pageSize, string playerId = null);
        int GetTotalCount(string playerId = null);
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
    }
}
