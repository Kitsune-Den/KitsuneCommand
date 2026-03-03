using Dapper;

namespace KitsuneCommand.Data.Repositories
{
    public interface ISettingsRepository
    {
        string Get(string name);
        void Set(string name, string value);
    }

    public class SettingsRepository : ISettingsRepository
    {
        private readonly DbConnectionFactory _db;

        public SettingsRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public string Get(string name)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<string>(
                "SELECT value FROM settings WHERE name = @Name",
                new { Name = name });
        }

        public void Set(string name, string value)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "INSERT OR REPLACE INTO settings (name, value) VALUES (@Name, @Value)",
                new { Name = name, Value = value });
        }
    }
}
