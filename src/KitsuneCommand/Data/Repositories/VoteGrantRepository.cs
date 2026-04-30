using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IVoteGrantRepository
    {
        /// <summary>
        /// Inserts a grant row. Returns true on success, false if a row with the same
        /// (provider, steam_id, vote_date) already exists — caller should skip the grant.
        /// This is the idempotency primitive: insert FIRST, grant second. If insert
        /// fails, another sweep beat us to it and we MUST NOT also grant the reward.
        /// </summary>
        bool TryInsert(VoteGrant grant);

        /// <summary>
        /// Has this player already been granted a reward for a vote on this date?
        /// Cheap pre-check used by the /vote command before hitting the provider API,
        /// so we don't waste a network round trip when the answer is "already given."
        /// TryInsert is still the source of truth — this is just an optimization.
        /// </summary>
        bool HasGrantForDate(string provider, string steamId, string voteDate);

        IEnumerable<VoteGrant> GetRecent(int limit = 50);

        IEnumerable<VoteGrant> GetForPlayer(string steamId, int limit = 50);

        int GetTotalCount();
    }

    public class VoteGrantRepository : IVoteGrantRepository
    {
        private readonly DbConnectionFactory _db;

        public VoteGrantRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public bool TryInsert(VoteGrant grant)
        {
            using var conn = _db.CreateConnection();
            try
            {
                conn.Execute(@"
                    INSERT INTO vote_grants
                        (provider, steam_id, player_name, vote_date, reward_type, reward_value, notes)
                    VALUES
                        (@Provider, @SteamId, @PlayerName, @VoteDate, @RewardType, @RewardValue, @Notes)",
                    grant);
                return true;
            }
            catch (System.Data.SQLite.SQLiteException ex)
                when (ex.ResultCode == System.Data.SQLite.SQLiteErrorCode.Constraint)
            {
                // Unique-index violation on (provider, steam_id, vote_date).
                // Another sweep / /vote handler already inserted — caller skips.
                return false;
            }
        }

        public bool HasGrantForDate(string provider, string steamId, string voteDate)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM vote_grants
                WHERE provider = @Provider AND steam_id = @SteamId AND vote_date = @VoteDate",
                new { Provider = provider, SteamId = steamId, VoteDate = voteDate }) > 0;
        }

        public IEnumerable<VoteGrant> GetRecent(int limit = 50)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<VoteGrant>(
                "SELECT * FROM vote_grants ORDER BY id DESC LIMIT @Limit",
                new { Limit = limit });
        }

        public IEnumerable<VoteGrant> GetForPlayer(string steamId, int limit = 50)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<VoteGrant>(
                "SELECT * FROM vote_grants WHERE steam_id = @SteamId ORDER BY id DESC LIMIT @Limit",
                new { SteamId = steamId, Limit = limit });
        }

        public int GetTotalCount()
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM vote_grants");
        }
    }
}
