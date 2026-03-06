using System.Collections.Generic;
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
    }
}
