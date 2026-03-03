using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Points economy endpoints: list, view, adjust, sign-in.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/points")]
    public class PointsController : ApiController
    {
        private readonly IPointsRepository _pointsRepo;
        private readonly ModEventBus _eventBus;

        public PointsController(IPointsRepository pointsRepo, ModEventBus eventBus)
        {
            _pointsRepo = pointsRepo;
            _eventBus = eventBus;
        }

        /// <summary>
        /// Get paginated list of all player points, optionally filtered by name.
        /// </summary>
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] string search = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _pointsRepo.GetAll(pageIndex, pageSize, search);
            var total = _pointsRepo.GetTotalCount(search);

            return Ok(ApiResponse.Ok(new PaginatedResponse<PointsInfo>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        /// <summary>
        /// Get a specific player's points by their player ID.
        /// </summary>
        [HttpGet]
        [Route("{playerId}")]
        public IHttpActionResult GetByPlayerId(string playerId)
        {
            var info = _pointsRepo.GetByPlayerId(playerId);
            if (info == null)
                return NotFound();

            return Ok(ApiResponse.Ok(info));
        }

        /// <summary>
        /// Admin-only: Adjust a player's points by a positive or negative amount.
        /// </summary>
        [HttpPost]
        [Route("{playerId}/adjust")]
        [RoleAuthorize("admin")]
        public IHttpActionResult AdjustPoints(string playerId, [FromBody] AdjustPointsRequest request)
        {
            if (request == null || request.Amount == 0)
                return BadRequest("A non-zero amount is required.");

            var existing = _pointsRepo.GetByPlayerId(playerId);
            if (existing == null)
                return NotFound();

            var newTotal = _pointsRepo.AdjustPoints(playerId, request.Amount);
            var reason = request.Reason ?? "Manual adjustment";

            _eventBus.Publish(new PointsUpdateEvent
            {
                PlayerId = playerId,
                PlayerName = existing.PlayerName,
                Points = newTotal,
                Change = request.Amount,
                Reason = reason
            });

            return Ok(ApiResponse.Ok(new
            {
                playerId,
                playerName = existing.PlayerName,
                points = newTotal,
                change = request.Amount,
                reason
            }));
        }

        /// <summary>
        /// Admin/Moderator: Trigger daily sign-in bonus for a player.
        /// </summary>
        [HttpPost]
        [Route("{playerId}/sign-in")]
        [RoleAuthorize("admin", "moderator")]
        public IHttpActionResult TriggerSignIn(string playerId)
        {
            var existing = _pointsRepo.GetByPlayerId(playerId);
            if (existing == null)
                return NotFound();

            const int signInBonus = 100;
            var awarded = _pointsRepo.TrySignIn(playerId, signInBonus);

            if (!awarded)
                return Ok(ApiResponse.Ok(new
                {
                    playerId,
                    message = "Already signed in today.",
                    awarded = false,
                    points = existing.Points
                }));

            var updated = _pointsRepo.GetByPlayerId(playerId);

            _eventBus.Publish(new PointsUpdateEvent
            {
                PlayerId = playerId,
                PlayerName = existing.PlayerName,
                Points = updated.Points,
                Change = signInBonus,
                Reason = "Daily sign-in bonus"
            });

            return Ok(ApiResponse.Ok(new
            {
                playerId,
                message = "Sign-in bonus awarded!",
                awarded = true,
                points = updated.Points,
                change = signInBonus
            }));
        }
    }
}
