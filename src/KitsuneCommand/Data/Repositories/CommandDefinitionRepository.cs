using Dapper;
using KitsuneCommand.Data.Entities;
using CommandDefinition = KitsuneCommand.Data.Entities.CommandDefinition;

namespace KitsuneCommand.Data.Repositories
{
    public interface ICommandDefinitionRepository
    {
        IEnumerable<CommandDefinition> GetAll();
        CommandDefinition GetById(int id);
        int Insert(CommandDefinition command);
        void Update(CommandDefinition command);
        void Delete(int id);
    }

    public class CommandDefinitionRepository : ICommandDefinitionRepository
    {
        private readonly DbConnectionFactory _db;

        public CommandDefinitionRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public IEnumerable<CommandDefinition> GetAll()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CommandDefinition>("SELECT * FROM command_definitions ORDER BY command");
        }

        public CommandDefinition GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<CommandDefinition>(
                "SELECT * FROM command_definitions WHERE id = @Id", new { Id = id });
        }

        public int Insert(CommandDefinition command)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO command_definitions (command, run_in_main_thread, description)
                VALUES (@Command, @RunInMainThread, @Description);
                SELECT last_insert_rowid();", command);
        }

        public void Update(CommandDefinition command)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE command_definitions
                SET command = @Command, run_in_main_thread = @RunInMainThread,
                    description = @Description
                WHERE id = @Id", command);
        }

        public void Delete(int id)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM command_definitions WHERE id = @Id", new { Id = id });
        }
    }
}
