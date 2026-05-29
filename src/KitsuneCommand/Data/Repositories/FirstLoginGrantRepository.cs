using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IFirstLoginGrantRepository
    {
        /// <summary>
        /// Records that a player has received their first-login pack. Returns true
        /// if this is the first time (row inserted), false if a row already existed
        /// (INSERT OR IGNORE matched the primary key). This is the once-ever
        /// idempotency primitive: claim the slot FIRST, deliver the pack second,
        /// so two rapid spawn events can't double-deliver.
        /// </summary>
        bool TryClaim(string playerId, string playerName);

        /// <summary>Has this player already received their first-login pack?</summary>
        bool HasGrant(string playerId);
    }

    public class FirstLoginGrantRepository : IFirstLoginGrantRepository
    {
        private readonly DbConnectionFactory _db;

        public FirstLoginGrantRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public bool TryClaim(string playerId, string playerName)
        {
            using var conn = _db.CreateConnection();
            var rows = conn.Execute(
                "INSERT OR IGNORE INTO first_login_grants (player_id, player_name) VALUES (@PlayerId, @PlayerName)",
                new { PlayerId = playerId, PlayerName = playerName });
            return rows > 0;
        }

        public bool HasGrant(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM first_login_grants WHERE player_id = @PlayerId",
                new { PlayerId = playerId }) > 0;
        }
    }
}
