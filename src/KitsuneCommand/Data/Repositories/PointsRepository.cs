using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IPointsRepository
    {
        PointsInfo GetByPlayerId(string playerId);
        IEnumerable<PointsInfo> GetAll(int pageIndex, int pageSize, string search = null);
        int GetTotalCount(string search = null);
        void UpsertPlayer(string playerId, string playerName);
        int AdjustPoints(string playerId, int amount);
        bool TrySignIn(string playerId, int bonus);
    }

    public class PointsRepository : IPointsRepository
    {
        private readonly DbConnectionFactory _db;

        public PointsRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public PointsInfo GetByPlayerId(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<PointsInfo>(
                "SELECT * FROM points_info WHERE id = @PlayerId",
                new { PlayerId = playerId }
            );
        }

        public IEnumerable<PointsInfo> GetAll(int pageIndex, int pageSize, string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = BuildWhereClause(search);
            return conn.Query<PointsInfo>(
                $"SELECT * FROM points_info {where} ORDER BY points DESC LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, Search = $"%{search}%" }
            );
        }

        public int GetTotalCount(string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = BuildWhereClause(search);
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM points_info {where}",
                new { Search = $"%{search}%" }
            );
        }

        /// <summary>
        /// Creates a points row for a player if one doesn't already exist.
        /// Does NOT overwrite existing data.
        /// </summary>
        public void UpsertPlayer(string playerId, string playerName)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "INSERT OR IGNORE INTO points_info (id, player_name) VALUES (@Id, @PlayerName)",
                new { Id = playerId, PlayerName = playerName }
            );
            // Update name in case it changed
            conn.Execute(
                "UPDATE points_info SET player_name = @PlayerName WHERE id = @Id",
                new { Id = playerId, PlayerName = playerName }
            );
        }

        /// <summary>
        /// Atomically adjusts a player's points by the given amount (positive or negative).
        /// Returns the new points total.
        /// </summary>
        public int AdjustPoints(string playerId, int amount)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "UPDATE points_info SET points = points + @Amount WHERE id = @PlayerId",
                new { PlayerId = playerId, Amount = amount }
            );
            return conn.ExecuteScalar<int>(
                "SELECT points FROM points_info WHERE id = @PlayerId",
                new { PlayerId = playerId }
            );
        }

        /// <summary>
        /// Attempts a daily sign-in for the player. Awards bonus points if the player
        /// hasn't signed in today. Returns true if bonus was awarded.
        /// </summary>
        public bool TrySignIn(string playerId, int bonus)
        {
            using var conn = _db.CreateConnection();

            // Check if already signed in today
            var lastSignIn = conn.ExecuteScalar<string>(
                "SELECT last_sign_in_at FROM points_info WHERE id = @PlayerId",
                new { PlayerId = playerId }
            );

            if (!string.IsNullOrEmpty(lastSignIn))
            {
                var lastDate = DateTime.Parse(lastSignIn).Date;
                if (lastDate >= DateTime.UtcNow.Date)
                    return false; // Already signed in today
            }

            // Award bonus and update sign-in timestamp
            conn.Execute(@"
                UPDATE points_info
                SET points = points + @Bonus, last_sign_in_at = datetime('now')
                WHERE id = @PlayerId",
                new { PlayerId = playerId, Bonus = bonus }
            );

            return true;
        }

        private static string BuildWhereClause(string search)
        {
            if (!string.IsNullOrWhiteSpace(search))
                return "WHERE player_name LIKE @Search";
            return "";
        }
    }
}
