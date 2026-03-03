using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IItemDefinitionRepository
    {
        IEnumerable<ItemDefinition> GetAll();
        ItemDefinition GetById(int id);
        int Insert(ItemDefinition item);
        void Update(ItemDefinition item);
        void Delete(int id);
    }

    public class ItemDefinitionRepository : IItemDefinitionRepository
    {
        private readonly DbConnectionFactory _db;

        public ItemDefinitionRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public IEnumerable<ItemDefinition> GetAll()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<ItemDefinition>("SELECT * FROM item_definitions ORDER BY item_name");
        }

        public ItemDefinition GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<ItemDefinition>(
                "SELECT * FROM item_definitions WHERE id = @Id", new { Id = id });
        }

        public int Insert(ItemDefinition item)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO item_definitions (item_name, count, quality, durability, description)
                VALUES (@ItemName, @Count, @Quality, @Durability, @Description);
                SELECT last_insert_rowid();", item);
        }

        public void Update(ItemDefinition item)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE item_definitions
                SET item_name = @ItemName, count = @Count, quality = @Quality,
                    durability = @Durability, description = @Description
                WHERE id = @Id", item);
        }

        public void Delete(int id)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM item_definitions WHERE id = @Id", new { Id = id });
        }
    }
}
