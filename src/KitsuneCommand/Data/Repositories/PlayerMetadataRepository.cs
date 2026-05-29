using System.Collections.Generic;
using System.Linq;
using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface IPlayerMetadataRepository
    {
        PlayerMetadata GetByPlayerId(string playerId);
        IEnumerable<PlayerMetadata> GetAll();
        void Upsert(PlayerMetadata metadata);
        void Delete(string playerId);

        /// <summary>
        /// Sets (or clears, when tier is null/empty) a player's VIP tier without
        /// touching name_color / custom_tag / notes. Kept separate from Upsert
        /// because Upsert overwrites those three columns wholesale — routing a
        /// tier change through it would wipe a player's chat colour. Creates the
        /// metadata row if it doesn't exist yet.
        /// </summary>
        void SetTier(string playerId, string tier);

        /// <summary>
        /// Returns the player_ids currently assigned to the given tier.
        /// </summary>
        IEnumerable<string> GetPlayerIdsByTier(string tier);
    }

    public class PlayerMetadataRepository : IPlayerMetadataRepository
    {
        private readonly DbConnectionFactory _db;

        public PlayerMetadataRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public PlayerMetadata GetByPlayerId(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<PlayerMetadata>(
                "SELECT * FROM player_metadata WHERE player_id = @PlayerId",
                new { PlayerId = playerId });
        }

        public IEnumerable<PlayerMetadata> GetAll()
        {
            using var conn = _db.CreateConnection();
            return conn.Query<PlayerMetadata>("SELECT * FROM player_metadata");
        }

        public void Upsert(PlayerMetadata metadata)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                INSERT INTO player_metadata (player_id, name_color, custom_tag, notes, updated_at)
                VALUES (@PlayerId, @NameColor, @CustomTag, @Notes, datetime('now'))
                ON CONFLICT(player_id) DO UPDATE SET
                    name_color = @NameColor,
                    custom_tag = @CustomTag,
                    notes = @Notes,
                    updated_at = datetime('now')",
                metadata);
        }

        public void Delete(string playerId)
        {
            using var conn = _db.CreateConnection();
            conn.Execute("DELETE FROM player_metadata WHERE player_id = @PlayerId",
                new { PlayerId = playerId });
        }

        public void SetTier(string playerId, string tier)
        {
            // Normalize empty string to null so "pleb" is consistently NULL in the
            // column — keeps GetPlayerIdsByTier filtering and the IS NULL semantics simple.
            var normalized = string.IsNullOrWhiteSpace(tier) ? null : tier.Trim();
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                INSERT INTO player_metadata (player_id, vip_tier, updated_at)
                VALUES (@PlayerId, @Tier, datetime('now'))
                ON CONFLICT(player_id) DO UPDATE SET
                    vip_tier = @Tier,
                    updated_at = datetime('now')",
                new { PlayerId = playerId, Tier = normalized });
        }

        public IEnumerable<string> GetPlayerIdsByTier(string tier)
        {
            if (string.IsNullOrWhiteSpace(tier)) return new List<string>();
            using var conn = _db.CreateConnection();
            return conn.Query<string>(
                "SELECT player_id FROM player_metadata WHERE vip_tier = @Tier",
                new { Tier = tier.Trim() }).ToList();
        }
    }
}
