using System.Collections.Generic;
using System.Linq;
using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface ISuspicionEventRepository
    {
        int Insert(SuspicionEvent evt);
        IEnumerable<SuspicionEvent> GetPage(int pageIndex, int pageSize, SuspicionEventFilter filter = null);
        int GetCount(SuspicionEventFilter filter = null);
        SuspicionEvent GetById(int id);
        int SetReviewed(IEnumerable<int> ids, bool reviewed, string reviewedBy, string note = null);
        int DeleteOlderThan(int days);
    }

    public class SuspicionEventRepository : ISuspicionEventRepository
    {
        private readonly DbConnectionFactory _db;

        public SuspicionEventRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public int Insert(SuspicionEvent evt)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO suspicion_events
                    (player_id, player_name, entity_id, detector, rule, severity, summary, details, pos_x, pos_y, pos_z)
                VALUES
                    (@PlayerId, @PlayerName, @EntityId, @Detector, @Rule, @Severity, @Summary, @Details, @PosX, @PosY, @PosZ);
                SELECT last_insert_rowid();", evt);
        }

        public IEnumerable<SuspicionEvent> GetPage(int pageIndex, int pageSize, SuspicionEventFilter filter = null)
        {
            using var conn = _db.CreateConnection();
            var (where, args) = BuildWhere(filter);
            args.Add("Limit", pageSize);
            args.Add("Offset", pageIndex * pageSize);
            return conn.Query<SuspicionEvent>(
                $"SELECT * FROM suspicion_events {where} ORDER BY id DESC LIMIT @Limit OFFSET @Offset", args);
        }

        public int GetCount(SuspicionEventFilter filter = null)
        {
            using var conn = _db.CreateConnection();
            var (where, args) = BuildWhere(filter);
            return conn.ExecuteScalar<int>($"SELECT COUNT(*) FROM suspicion_events {where}", args);
        }

        public SuspicionEvent GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<SuspicionEvent>(
                "SELECT * FROM suspicion_events WHERE id = @Id", new { Id = id });
        }

        public int SetReviewed(IEnumerable<int> ids, bool reviewed, string reviewedBy, string note = null)
        {
            var idList = ids?.Distinct().ToList() ?? new List<int>();
            if (idList.Count == 0) return 0;

            using var conn = _db.CreateConnection();
            // Un-reviewing clears who/when/note so the row reads as fresh again.
            return conn.Execute(@"
                UPDATE suspicion_events
                SET reviewed = @Reviewed,
                    reviewed_by = CASE WHEN @Reviewed = 1 THEN @ReviewedBy ELSE NULL END,
                    reviewed_at = CASE WHEN @Reviewed = 1 THEN datetime('now') ELSE NULL END,
                    review_note = CASE WHEN @Reviewed = 1 THEN @Note ELSE NULL END
                WHERE id IN @Ids",
                new { Reviewed = reviewed ? 1 : 0, ReviewedBy = reviewedBy, Note = note, Ids = idList });
        }

        public int DeleteOlderThan(int days)
        {
            if (days <= 0) return 0;
            using var conn = _db.CreateConnection();
            return conn.Execute(
                "DELETE FROM suspicion_events WHERE created_at < datetime('now', @Cutoff)",
                new { Cutoff = $"-{days} days" });
        }

        private static (string Where, DynamicParameters Args) BuildWhere(SuspicionEventFilter filter)
        {
            var conditions = new List<string>();
            var args = new DynamicParameters();
            if (filter != null)
            {
                if (!string.IsNullOrWhiteSpace(filter.Search))
                {
                    conditions.Add("(player_name LIKE @Search OR player_id LIKE @Search)");
                    args.Add("Search", $"%{filter.Search.Trim()}%");
                }
                if (!string.IsNullOrWhiteSpace(filter.Detector))
                {
                    conditions.Add("detector = @Detector");
                    args.Add("Detector", filter.Detector);
                }
                if (!string.IsNullOrWhiteSpace(filter.Severity))
                {
                    conditions.Add("severity = @Severity");
                    args.Add("Severity", filter.Severity);
                }
                if (filter.Reviewed.HasValue)
                {
                    conditions.Add("reviewed = @Reviewed");
                    args.Add("Reviewed", filter.Reviewed.Value ? 1 : 0);
                }
            }
            var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            return (where, args);
        }
    }
}
