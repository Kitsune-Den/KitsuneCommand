using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface ICityLocationRepository
    {
        IEnumerable<CityLocation> GetAll(int pageIndex, int pageSize, string search = null);
        int GetTotalCount(string search = null);
        CityLocation GetById(int id);
        CityLocation GetByName(string cityName);
        int Insert(CityLocation city);
        void Update(CityLocation city);
        void Delete(int id);
    }

    public class CityLocationRepository : ICityLocationRepository
    {
        private readonly DbConnectionFactory _db;

        public CityLocationRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public IEnumerable<CityLocation> GetAll(int pageIndex, int pageSize, string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search) ? "" : "WHERE city_name LIKE @Search";
            return conn.Query<CityLocation>(
                $"SELECT * FROM city_locations {where} ORDER BY city_name LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, Search = $"%{search}%" });
        }

        public int GetTotalCount(string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search) ? "" : "WHERE city_name LIKE @Search";
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM city_locations {where}",
                new { Search = $"%{search}%" });
        }

        public CityLocation GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<CityLocation>(
                "SELECT * FROM city_locations WHERE id = @Id", new { Id = id });
        }

        public CityLocation GetByName(string cityName)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<CityLocation>(
                "SELECT * FROM city_locations WHERE city_name = @CityName COLLATE NOCASE",
                new { CityName = cityName });
        }

        public int Insert(CityLocation city)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO city_locations (city_name, points_required, position, view_direction)
                VALUES (@CityName, @PointsRequired, @Position, @ViewDirection);
                SELECT last_insert_rowid();", city);
        }

        public void Update(CityLocation city)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE city_locations
                SET city_name = @CityName, points_required = @PointsRequired,
                    position = @Position, view_direction = @ViewDirection
                WHERE id = @Id", city);
        }

        public void Delete(int id)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM city_locations WHERE id = @Id", new { Id = id });
        }
    }
}
