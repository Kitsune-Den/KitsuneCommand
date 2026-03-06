using System;
using System.Linq;
using System.Web.Http;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.Services;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// API controller for managing support tickets.
    /// Admins and moderators can view, reply to, and close tickets.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/tickets")]
    [RoleAuthorize("admin", "moderator")]
    public class TicketsController : ApiController
    {
        private readonly ITicketRepository _ticketRepo;
        private readonly TicketFeature _ticketFeature;
        private readonly DiscordWebhookService _discordService;
        private readonly LivePlayerManager _playerManager;
        private readonly ModEventBus _eventBus;

        public TicketsController(
            ITicketRepository ticketRepo,
            TicketFeature ticketFeature,
            DiscordWebhookService discordService,
            LivePlayerManager playerManager,
            ModEventBus eventBus)
        {
            _ticketRepo = ticketRepo;
            _ticketFeature = ticketFeature;
            _discordService = discordService;
            _playerManager = playerManager;
            _eventBus = eventBus;
        }

        /// <summary>
        /// Get paginated list of tickets with optional filters.
        /// </summary>
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetTickets(int pageIndex = 0, int pageSize = 50, string status = null, string search = null)
        {
            try
            {
                var tickets = _ticketRepo.GetAll(pageIndex, pageSize, status, search);
                var total = _ticketRepo.GetTotalCount(status, search);
                return Ok(ApiResponse.Ok(new { items = tickets, total }));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to get tickets: {ex.Message}"));
            }
        }

        /// <summary>
        /// Get a single ticket with its messages.
        /// </summary>
        [HttpGet]
        [Route("{id:int}")]
        public IHttpActionResult GetTicket(int id)
        {
            try
            {
                var ticket = _ticketRepo.GetById(id);
                if (ticket == null)
                    return Ok(ApiResponse.Error(404, "Ticket not found."));

                var messages = _ticketRepo.GetMessages(id);
                return Ok(ApiResponse.Ok(new
                {
                    ticket.Id,
                    ticket.CreatedAt,
                    ticket.UpdatedAt,
                    ticket.PlayerId,
                    ticket.PlayerName,
                    ticket.Subject,
                    ticket.Status,
                    ticket.Priority,
                    ticket.AssignedTo,
                    messages
                }));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to get ticket: {ex.Message}"));
            }
        }

        /// <summary>
        /// Reply to a ticket. Delivers in-game if the player is online; queued otherwise.
        /// </summary>
        [HttpPost]
        [Route("{id:int}/reply")]
        public IHttpActionResult ReplyToTicket(int id, [FromBody] TicketReplyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message is required.");

            try
            {
                var ticket = _ticketRepo.GetById(id);
                if (ticket == null)
                    return Ok(ApiResponse.Error(404, "Ticket not found."));

                var adminName = User.Identity.Name ?? "Admin";
                var message = new TicketMessage
                {
                    TicketId = id,
                    SenderType = "admin",
                    SenderId = adminName,
                    SenderName = adminName,
                    Message = request.Message,
                    Delivered = 0 // Will be delivered on player login or immediately if online
                };

                var msgId = _ticketRepo.AddMessage(message);
                message.Id = msgId;

                // If ticket was closed, reopen it
                if (ticket.Status == "closed")
                {
                    _ticketRepo.UpdateStatus(id, "in_progress", adminName);
                    ticket.Status = "in_progress";
                }

                // Try to deliver immediately if the player is online
                var onlinePlayer = _playerManager.GetAllOnline()
                    .FirstOrDefault(p => p.PlayerId == ticket.PlayerId);
                if (onlinePlayer != null)
                {
                    try
                    {
                        var reply = $"[Ticket #{id}] {adminName}: {request.Message}";
                        var safeReply = reply.Replace("\"", "'");
                        SdtdConsole.Instance.ExecuteSync($"pm {onlinePlayer.EntityId} \"{safeReply}\"", null);
                        _ticketRepo.MarkMessagesDelivered(new[] { msgId });
                        message.Delivered = 1;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[KitsuneCommand] Failed to deliver ticket reply in-game: {ex.Message}");
                    }
                }

                // Discord notification
                var settings = _ticketFeature.Settings;
                if (settings.DiscordNotifyOnReply && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
                {
                    _discordService.SendTicketReply(settings.DiscordWebhookUrl, ticket, message);
                }

                _eventBus.Publish(new TicketUpdatedEvent
                {
                    TicketId = id,
                    Status = ticket.Status,
                    UpdatedBy = adminName
                });

                return Ok(ApiResponse.Ok(new { messageId = msgId, delivered = message.Delivered == 1 }));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to reply: {ex.Message}"));
            }
        }

        /// <summary>
        /// Update ticket status (open, in_progress, closed).
        /// </summary>
        [HttpPut]
        [Route("{id:int}/status")]
        public IHttpActionResult UpdateStatus(int id, [FromBody] TicketStatusRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Status))
                return BadRequest("Status is required.");

            var validStatuses = new[] { "open", "in_progress", "closed" };
            if (!validStatuses.Contains(request.Status))
                return BadRequest("Status must be: open, in_progress, or closed.");

            try
            {
                var ticket = _ticketRepo.GetById(id);
                if (ticket == null)
                    return Ok(ApiResponse.Error(404, "Ticket not found."));

                var adminName = User.Identity.Name ?? "Admin";
                _ticketRepo.UpdateStatus(id, request.Status, request.AssignedTo ?? adminName);

                // Discord notification on close
                var settings = _ticketFeature.Settings;
                if (request.Status == "closed" && settings.DiscordNotifyOnClose
                    && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
                {
                    _discordService.SendTicketClosed(settings.DiscordWebhookUrl, ticket, adminName);
                }

                _eventBus.Publish(new TicketUpdatedEvent
                {
                    TicketId = id,
                    Status = request.Status,
                    UpdatedBy = adminName
                });

                return Ok(ApiResponse.Ok("Status updated."));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to update status: {ex.Message}"));
            }
        }

        /// <summary>
        /// Get ticket count statistics for the dashboard.
        /// </summary>
        [HttpGet]
        [Route("stats")]
        public IHttpActionResult GetStats()
        {
            try
            {
                return Ok(ApiResponse.Ok(new
                {
                    openCount = _ticketRepo.GetCountByStatus("open"),
                    inProgressCount = _ticketRepo.GetCountByStatus("in_progress"),
                    closedCount = _ticketRepo.GetCountByStatus("closed")
                }));
            }
            catch (Exception ex)
            {
                return Ok(ApiResponse.Error(500, $"Failed to get stats: {ex.Message}"));
            }
        }

        /// <summary>
        /// Get ticket settings.
        /// </summary>
        [HttpGet]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            return Ok(ApiResponse.Ok(_ticketFeature.Settings));
        }

        /// <summary>
        /// Update ticket settings.
        /// </summary>
        [HttpPut]
        [Route("settings")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] TicketSettings settings)
        {
            if (settings == null)
                return BadRequest("Settings body is required.");

            if (settings.MaxOpenTicketsPerPlayer < 1) settings.MaxOpenTicketsPerPlayer = 1;
            if (settings.CooldownSeconds < 0) settings.CooldownSeconds = 0;

            _ticketFeature.UpdateSettings(settings);
            return Ok(ApiResponse.Ok("Ticket settings updated."));
        }
    }

    public class TicketReplyRequest
    {
        public string Message { get; set; }
    }

    public class TicketStatusRequest
    {
        public string Status { get; set; }
        public string AssignedTo { get; set; }
    }
}
