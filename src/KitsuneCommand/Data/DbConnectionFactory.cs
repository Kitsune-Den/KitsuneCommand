using Microsoft.Data.Sqlite;
using System.Data;

namespace KitsuneCommand.Data
{
    /// <summary>
    /// Creates SQLite database connections.
    /// </summary>
    public class DbConnectionFactory
    {
        private readonly string _connectionString;

        public DbConnectionFactory(string databasePath)
        {
            _connectionString = $"Data Source={databasePath}";
        }

        public IDbConnection CreateConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        public string ConnectionString => _connectionString;
    }
}
