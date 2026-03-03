using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IHomeLocationRepository
    {
        IEnumerable<HomeLocation> GetAll(int pageIndex, int pageSize, string search = null);
        int GetTotalCount(string search = null);
        IEnumerable<HomeLocation> GetByPlayerId(string playerId);
        HomeLocation GetById(int id);
        HomeLocation GetByPlayerIdAndName(string playerId, string homeName);
        int GetCountByPlayerId(string playerId);
        int Insert(HomeLocation home);
        void Update(HomeLocation home);
        void Delete(int id);
    }

    public class HomeLocationRepository : IHomeLocationRepository
    {
        private readonly DbConnectionFactory _db;

        public HomeLocationRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public IEnumerable<HomeLocation> GetAll(int pageIndex, int pageSize, string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search)
                ? ""
                : "WHERE player_name LIKE @Search OR home_name LIKE @Search";
            return conn.Query<HomeLocation>(
                $"SELECT * FROM home_locations {where} ORDER BY player_name, home_name LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, Search = $"%{search}%" });
        }

        public int GetTotalCount(string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search)
                ? ""
                : "WHERE player_name LIKE @Search OR home_name LIKE @Search";
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM home_locations {where}",
                new { Search = $"%{search}%" });
        }

        public IEnumerable<HomeLocation> GetByPlayerId(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<HomeLocation>(
                "SELECT * FROM home_locations WHERE player_id = @PlayerId ORDER BY home_name",
                new { PlayerId = playerId });
        }

        public HomeLocation GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<HomeLocation>(
                "SELECT * FROM home_locations WHERE id = @Id", new { Id = id });
        }

        public HomeLocation GetByPlayerIdAndName(string playerId, string homeName)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<HomeLocation>(
                "SELECT * FROM home_locations WHERE player_id = @PlayerId AND home_name = @HomeName COLLATE NOCASE",
                new { PlayerId = playerId, HomeName = homeName });
        }

        public int GetCountByPlayerId(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM home_locations WHERE player_id = @PlayerId",
                new { PlayerId = playerId });
        }

        public int Insert(HomeLocation home)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO home_locations (player_id, player_name, home_name, position)
                VALUES (@PlayerId, @PlayerName, @HomeName, @Position);
                SELECT last_insert_rowid();", home);
        }

        public void Update(HomeLocation home)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE home_locations
                SET player_name = @PlayerName, home_name = @HomeName, position = @Position
                WHERE id = @Id", home);
        }

        public void Delete(int id)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM home_locations WHERE id = @Id", new { Id = id });
        }
    }
}
