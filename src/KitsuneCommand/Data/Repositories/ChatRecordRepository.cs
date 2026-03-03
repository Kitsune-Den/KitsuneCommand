using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IChatRecordRepository
    {
        void Insert(ChatRecord record);
        IEnumerable<ChatRecord> GetHistory(int pageIndex, int pageSize, string search = null, int? chatType = null);
        int GetTotalCount(string search = null, int? chatType = null);
    }

    public class ChatRecordRepository : IChatRecordRepository
    {
        private readonly DbConnectionFactory _db;

        public ChatRecordRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public void Insert(ChatRecord record)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                INSERT INTO chat_records (player_id, entity_id, sender_name, chat_type, message)
                VALUES (@PlayerId, @EntityId, @SenderName, @ChatType, @Message)",
                record
            );
        }

        public IEnumerable<ChatRecord> GetHistory(int pageIndex, int pageSize, string search = null, int? chatType = null)
        {
            using var conn = _db.CreateConnection();
            var where = BuildWhereClause(search, chatType);
            return conn.Query<ChatRecord>(
                $"SELECT * FROM chat_records {where} ORDER BY id DESC LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, Search = $"%{search}%", ChatType = chatType }
            );
        }

        public int GetTotalCount(string search = null, int? chatType = null)
        {
            using var conn = _db.CreateConnection();
            var where = BuildWhereClause(search, chatType);
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM chat_records {where}",
                new { Search = $"%{search}%", ChatType = chatType }
            );
        }

        private static string BuildWhereClause(string search, int? chatType)
        {
            var clauses = new List<string>();
            if (!string.IsNullOrWhiteSpace(search))
                clauses.Add("(sender_name LIKE @Search OR message LIKE @Search)");
            if (chatType.HasValue)
                clauses.Add("chat_type = @ChatType");
            return clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        }
    }
}
