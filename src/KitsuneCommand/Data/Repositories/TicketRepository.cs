using System.Collections.Generic;
using System.Linq;
using Dapper;
using KitsuneCommand.Data.Entities;

namespace KitsuneCommand.Data.Repositories
{
    public interface ITicketRepository
    {
        IEnumerable<Ticket> GetAll(int pageIndex, int pageSize, string status = null, string search = null);
        int GetTotalCount(string status = null, string search = null);
        Ticket GetById(int id);
        IEnumerable<Ticket> GetByPlayerId(string playerId);
        int GetOpenCountByPlayerId(string playerId);
        int Create(Ticket ticket);
        void UpdateStatus(int id, string status, string assignedTo = null);

        IEnumerable<TicketMessage> GetMessages(int ticketId);
        int AddMessage(TicketMessage message);
        IEnumerable<TicketMessage> GetUndeliveredMessages(string playerId);
        void MarkMessagesDelivered(IEnumerable<int> messageIds);

        int GetCountByStatus(string status);
    }

    public class TicketRepository : ITicketRepository
    {
        private readonly DbConnectionFactory _db;

        public TicketRepository(DbConnectionFactory db)
        {
            _db = db;
        }

        public IEnumerable<Ticket> GetAll(int pageIndex, int pageSize, string status = null, string search = null)
        {
            using var conn = _db.CreateConnection();
            var conditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(status))
                conditions.Add("status = @Status");
            if (!string.IsNullOrWhiteSpace(search))
                conditions.Add("(subject LIKE @Search OR player_name LIKE @Search)");

            var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            return conn.Query<Ticket>(
                $"SELECT * FROM tickets {where} ORDER BY CASE status WHEN 'open' THEN 0 WHEN 'in_progress' THEN 1 ELSE 2 END, updated_at DESC LIMIT @Limit OFFSET @Offset",
                new { Limit = pageSize, Offset = pageIndex * pageSize, Status = status, Search = $"%{search}%" });
        }

        public int GetTotalCount(string status = null, string search = null)
        {
            using var conn = _db.CreateConnection();
            var conditions = new List<string>();
            if (!string.IsNullOrWhiteSpace(status))
                conditions.Add("status = @Status");
            if (!string.IsNullOrWhiteSpace(search))
                conditions.Add("(subject LIKE @Search OR player_name LIKE @Search)");

            var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
            return conn.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM tickets {where}",
                new { Status = status, Search = $"%{search}%" });
        }

        public Ticket GetById(int id)
        {
            using var conn = _db.CreateConnection();
            return conn.QueryFirstOrDefault<Ticket>(
                "SELECT * FROM tickets WHERE id = @Id", new { Id = id });
        }

        public IEnumerable<Ticket> GetByPlayerId(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<Ticket>(
                "SELECT * FROM tickets WHERE player_id = @PlayerId ORDER BY updated_at DESC",
                new { PlayerId = playerId });
        }

        public int GetOpenCountByPlayerId(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM tickets WHERE player_id = @PlayerId AND status != 'closed'",
                new { PlayerId = playerId });
        }

        public int Create(Ticket ticket)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(@"
                INSERT INTO tickets (player_id, player_name, subject, status, priority, assigned_to)
                VALUES (@PlayerId, @PlayerName, @Subject, @Status, @Priority, @AssignedTo);
                SELECT last_insert_rowid();", ticket);
        }

        public void UpdateStatus(int id, string status, string assignedTo = null)
        {
            using var conn = _db.CreateConnection();
            conn.Execute(@"
                UPDATE tickets SET status = @Status, assigned_to = COALESCE(@AssignedTo, assigned_to),
                updated_at = datetime('now') WHERE id = @Id",
                new { Id = id, Status = status, AssignedTo = assignedTo });
        }

        public IEnumerable<TicketMessage> GetMessages(int ticketId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<TicketMessage>(
                "SELECT * FROM ticket_messages WHERE ticket_id = @TicketId ORDER BY created_at ASC",
                new { TicketId = ticketId });
        }

        public int AddMessage(TicketMessage message)
        {
            using var conn = _db.CreateConnection();
            var id = conn.ExecuteScalar<int>(@"
                INSERT INTO ticket_messages (ticket_id, sender_type, sender_id, sender_name, message, delivered)
                VALUES (@TicketId, @SenderType, @SenderId, @SenderName, @Message, @Delivered);
                SELECT last_insert_rowid();", message);

            // Touch the ticket's updated_at
            conn.Execute("UPDATE tickets SET updated_at = datetime('now') WHERE id = @TicketId",
                new { message.TicketId });

            return id;
        }

        public IEnumerable<TicketMessage> GetUndeliveredMessages(string playerId)
        {
            using var conn = _db.CreateConnection();
            return conn.Query<TicketMessage>(@"
                SELECT tm.* FROM ticket_messages tm
                JOIN tickets t ON t.id = tm.ticket_id
                WHERE t.player_id = @PlayerId AND tm.sender_type = 'admin' AND tm.delivered = 0
                ORDER BY tm.created_at ASC",
                new { PlayerId = playerId });
        }

        public void MarkMessagesDelivered(IEnumerable<int> messageIds)
        {
            var ids = messageIds.ToList();
            if (ids.Count == 0) return;

            using var conn = _db.CreateConnection();
            conn.Execute(
                $"UPDATE ticket_messages SET delivered = 1 WHERE id IN ({string.Join(",", ids)})");
        }

        public int GetCountByStatus(string status)
        {
            using var conn = _db.CreateConnection();
            return conn.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM tickets WHERE status = @Status",
                new { Status = status });
        }
    }
}
