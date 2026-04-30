using Dapper;
using KitsuneCommand.Data.Entities;
using CommandDefinition = KitsuneCommand.Data.Entities.CommandDefinition;

namespace KitsuneCommand.Data.Repositories
{
    public interface IVipGiftRepository
    {
        // CRUD
        IEnumerable<VipGift> GetAll(int pageIndex, int pageSize, string search = null);
        int GetTotalCount(string search = null);
        VipGift GetById(int id);
        IEnumerable<VipGift> GetByPlayerId(string playerId);
        IEnumerable<VipGift> GetPendingForPlayer(string playerId);
        int Insert(VipGift gift);
        void Update(VipGift gift);
        void Delete(int id);

        // Junction tables
        IEnumerable<ItemDefinition> GetItemsForGift(int giftId);
        IEnumerable<CommandDefinition> GetCommandsForGift(int giftId);
        void SetGiftItems(int giftId, IEnumerable<int> itemDefinitionIds);
        void SetGiftCommands(int giftId, IEnumerable<int> commandDefinitionIds);

        // Templates: a "template" gift is a vip_gift row with the sentinel
        // player_id "_template_". Admins create one via the regular VIP Gifts
        // admin UI by typing "_template_" as the player_id; the VoteRewards
        // feature looks them up by name and clones them for voters.
        VipGift GetTemplateByName(string name);

        // Claiming
        void MarkAsClaimed(int giftId);
    }

    /// <summary>
    /// The sentinel player_id used to mark a vip_gift row as a template that
    /// other features (e.g. VoteRewards) can clone from. Underscore-prefixed
    /// to avoid colliding with any real Steam_/Epic_ cross-platform id.
    /// </summary>
    public static class VipGiftSentinels
    {
        public const string TemplatePlayerId = "_template_";
    }

    public class VipGiftRepository : IVipGiftRepository
    {
        private readonly DbConnectionFactory _db;

        public VipGiftRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        // ─── CRUD ────────────────────────────────────────────────

        public IEnumerable<VipGift> GetAll(int pageIndex, int pageSize, string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search)
                ? ""
                : "WHERE name LIKE @Search OR player_id LIKE @Search OR player_name LIKE @Search OR description LIKE @Search";
            return conn.Query<VipGift>(
                $"SELECT * FROM vip_gifts {where} ORDER BY id DESC LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, Search = $"%{search}%" });
        }

        public int GetTotalCount(string search = null)
        {
            using var conn = _db.CreateConnection();
            var where = string.IsNullOrWhiteSpace(search)
                ? ""
                : "WHERE name LIKE @Search OR player_id LIKE @Search OR player_name LIKE @Search OR description LIKE @Search";
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM vip_gifts {where}",
                new { Search = $"%{search}%" });
        }

        public VipGift GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<VipGift>(
                "SELECT * FROM vip_gifts WHERE id = @Id", new { Id = id });
        }

        public IEnumerable<VipGift> GetByPlayerId(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<VipGift>(
                "SELECT * FROM vip_gifts WHERE player_id = @PlayerId ORDER BY id DESC",
                new { PlayerId = playerId });
        }

        /// <summary>
        /// Returns gifts that the player can currently claim:
        /// one-time gifts not yet claimed, or repeatable gifts whose period has elapsed.
        /// </summary>
        public IEnumerable<VipGift> GetPendingForPlayer(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<VipGift>(@"
                SELECT * FROM vip_gifts
                WHERE player_id = @PlayerId AND (
                    (claim_period IS NULL AND claimed = 0)
                    OR (claim_period = 'daily'   AND (last_claimed_at IS NULL OR last_claimed_at < datetime('now', '-1 day')))
                    OR (claim_period = 'weekly'  AND (last_claimed_at IS NULL OR last_claimed_at < datetime('now', '-7 days')))
                    OR (claim_period = 'monthly' AND (last_claimed_at IS NULL OR last_claimed_at < datetime('now', '-30 days')))
                )
                ORDER BY id ASC",
                new { PlayerId = playerId });
        }

        public int Insert(VipGift gift)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO vip_gifts (player_id, player_name, name, description, claim_period)
                VALUES (@PlayerId, @PlayerName, @Name, @Description, @ClaimPeriod);
                SELECT last_insert_rowid();", gift);
        }

        public void Update(VipGift gift)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE vip_gifts
                SET name = @Name, description = @Description,
                    claim_period = @ClaimPeriod, player_name = @PlayerName
                WHERE id = @Id", gift);
        }

        public void Delete(int id)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM vip_gifts WHERE id = @Id", new { Id = id });
        }

        // ─── Junction Tables ─────────────────────────────────────

        public IEnumerable<ItemDefinition> GetItemsForGift(int giftId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<ItemDefinition>(@"
                SELECT d.* FROM item_definitions d
                INNER JOIN vip_gift_items vgi ON vgi.item_id = d.id
                WHERE vgi.vip_gift_id = @GiftId
                ORDER BY d.item_name",
                new { GiftId = giftId });
        }

        public IEnumerable<CommandDefinition> GetCommandsForGift(int giftId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<CommandDefinition>(@"
                SELECT d.* FROM command_definitions d
                INNER JOIN vip_gift_commands vgc ON vgc.command_id = d.id
                WHERE vgc.vip_gift_id = @GiftId
                ORDER BY d.command",
                new { GiftId = giftId });
        }

        public void SetGiftItems(int giftId, IEnumerable<int> itemDefinitionIds)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM vip_gift_items WHERE vip_gift_id = @GiftId",
                new { GiftId = giftId });

            foreach (var itemId in itemDefinitionIds)
            {
                conn.Execute(
                    "INSERT INTO vip_gift_items (vip_gift_id, item_id) VALUES (@GiftId, @ItemId)",
                    new { GiftId = giftId, ItemId = itemId });
            }
        }

        public void SetGiftCommands(int giftId, IEnumerable<int> commandDefinitionIds)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM vip_gift_commands WHERE vip_gift_id = @GiftId",
                new { GiftId = giftId });

            foreach (var commandId in commandDefinitionIds)
            {
                conn.Execute(
                    "INSERT INTO vip_gift_commands (vip_gift_id, command_id) VALUES (@GiftId, @CommandId)",
                    new { GiftId = giftId, CommandId = commandId });
            }
        }

        // ─── Templates ────────────────────────────────────────────

        /// <summary>
        /// Returns the first template gift matching the given name. If multiple
        /// rows share the same name + sentinel player_id (admin error / accident),
        /// the lowest id wins so behavior is deterministic across requests.
        /// Returns null if no template with that name exists.
        /// </summary>
        public VipGift GetTemplateByName(string name)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<VipGift>(@"
                SELECT * FROM vip_gifts
                WHERE player_id = @TemplateId AND name = @Name
                ORDER BY id ASC
                LIMIT 1",
                new { TemplateId = VipGiftSentinels.TemplatePlayerId, Name = name });
        }

        // ─── Claiming ────────────────────────────────────────────

        public void MarkAsClaimed(int giftId)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE vip_gifts
                SET claimed = 1,
                    total_claim_count = total_claim_count + 1,
                    last_claimed_at = datetime('now')
                WHERE id = @Id",
                new { Id = giftId });
        }
    }
}
