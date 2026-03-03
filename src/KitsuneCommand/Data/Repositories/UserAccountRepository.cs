using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public class UserAccountRepository : IUserAccountRepository
    {
        private readonly DbConnectionFactory _db;

        public UserAccountRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public UserAccount GetByUsername(string username)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<UserAccount>(
                "SELECT * FROM user_accounts WHERE username = @Username AND is_active = 1",
                new { Username = username }
            );
        }

        public UserAccount GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<UserAccount>(
                "SELECT * FROM user_accounts WHERE id = @Id",
                new { Id = id }
            );
        }

        public IEnumerable<UserAccount> GetAll()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<UserAccount>("SELECT * FROM user_accounts ORDER BY created_at");
        }

        public int Create(UserAccount account)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO user_accounts (username, password_hash, display_name, role)
                VALUES (@Username, @PasswordHash, @DisplayName, @Role);
                SELECT last_insert_rowid();",
                account
            );
        }

        public void Update(UserAccount account)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE user_accounts
                SET display_name = @DisplayName, role = @Role, is_active = @IsActive
                WHERE id = @Id",
                account
            );
        }

        public void UpdateLastLogin(string username)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "UPDATE user_accounts SET last_login_at = datetime('now') WHERE username = @Username",
                new { Username = username }
            );
        }

        public void UpdatePassword(int id, string passwordHash)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "UPDATE user_accounts SET password_hash = @PasswordHash WHERE id = @Id",
                new { Id = id, PasswordHash = passwordHash }
            );
        }

        public int Count()
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM user_accounts");
        }

        public int CountActiveAdmins()
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM user_accounts WHERE role = 'admin' AND is_active = 1"
            );
        }
    }
}
