using Dapper;
using KitsuneCommand.Data.Entities;
using CommandDefinition = KitsuneCommand.Data.Entities.CommandDefinition;

namespace KitsuneCommand.Data.Repositories
{
    public interface IGoodsRepository
    {
        IEnumerable<Goods> GetAll(int pageIndex, int pageSize, string search = null);
        int GetTotalCount(string search = null);
        Goods GetById(int id);
        Goods GetByName(string name);
        IEnumerable<ItemDefinition> GetItemsForGoods(int goodsId);
        IEnumerable<CommandDefinition> GetCommandsForGoods(int goodsId);
        int Insert(Goods goods);
        void Update(Goods goods);
        void Delete(int id);
        void SetGoodsItems(int goodsId, IEnumerable<int> itemDefinitionIds);
        void SetGoodsCommands(int goodsId, IEnumerable<int> commandDefinitionIds);
    }

    public class GoodsRepository : IGoodsRepository
    {
        private readonly DbConnectionFactory _db;

        public GoodsRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public IEnumerable<Goods> GetAll(int pageIndex, int pageSize, string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search) ? "" : "WHERE name LIKE @Search";
            return conn.Query<Goods>(
                $"SELECT * FROM goods {where} ORDER BY name LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, Search = $"%{search}%" });
        }

        public int GetTotalCount(string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search) ? "" : "WHERE name LIKE @Search";
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM goods {where}",
                new { Search = $"%{search}%" });
        }

        public Goods GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<Goods>(
                "SELECT * FROM goods WHERE id = @Id", new { Id = id });
        }

        public Goods GetByName(string name)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<Goods>(
                "SELECT * FROM goods WHERE name = @Name COLLATE NOCASE",
                new { Name = name });
        }

        public IEnumerable<ItemDefinition> GetItemsForGoods(int goodsId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<ItemDefinition>(@"
                SELECT d.* FROM item_definitions d
                INNER JOIN goods_items gi ON gi.item_id = d.id
                WHERE gi.goods_id = @GoodsId
                ORDER BY d.item_name",
                new { GoodsId = goodsId });
        }

        public IEnumerable<CommandDefinition> GetCommandsForGoods(int goodsId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CommandDefinition>(@"
                SELECT d.* FROM command_definitions d
                INNER JOIN goods_commands gc ON gc.command_id = d.id
                WHERE gc.goods_id = @GoodsId
                ORDER BY d.command",
                new { GoodsId = goodsId });
        }

        public int Insert(Goods goods)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO goods (name, price, description)
                VALUES (@Name, @Price, @Description);
                SELECT last_insert_rowid();", goods);
        }

        public void Update(Goods goods)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE goods
                SET name = @Name, price = @Price, description = @Description
                WHERE id = @Id", goods);
        }

        public void Delete(int id)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM goods WHERE id = @Id", new { Id = id });
        }

        /// <summary>
        /// Replaces all item definition links for a goods entry.
        /// </summary>
        public void SetGoodsItems(int goodsId, IEnumerable<int> itemDefinitionIds)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM goods_items WHERE goods_id = @GoodsId",
                new { GoodsId = goodsId });

            foreach (var itemId in itemDefinitionIds)
            {
                conn.Execute(
                    "INSERT INTO goods_items (goods_id, item_id) VALUES (@GoodsId, @ItemId)",
                    new { GoodsId = goodsId, ItemId = itemId });
            }
        }

        /// <summary>
        /// Replaces all command definition links for a goods entry.
        /// </summary>
        public void SetGoodsCommands(int goodsId, IEnumerable<int> commandDefinitionIds)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM goods_commands WHERE goods_id = @GoodsId",
                new { GoodsId = goodsId });

            foreach (var commandId in commandDefinitionIds)
            {
                conn.Execute(
                    "INSERT INTO goods_commands (goods_id, command_id) VALUES (@GoodsId, @CommandId)",
                    new { GoodsId = goodsId, CommandId = commandId });
            }
        }
    }
}
