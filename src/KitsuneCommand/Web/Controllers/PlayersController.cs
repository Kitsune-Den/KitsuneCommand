using System.Web.Http;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Player management endpoints: list, detail, inventory, skills, and admin actions.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/players")]
    public class PlayersController : ApiController
    {
        private readonly LivePlayerManager _playerManager;
        private readonly IPointsRepository _pointsRepo;

        public PlayersController(LivePlayerManager playerManager, IPointsRepository pointsRepo)
        {
            _playerManager = playerManager;
            _pointsRepo = pointsRepo;
        }

        /// <summary>
        /// Get all currently online players.
        /// </summary>
        [HttpGet]
        [Route("")]
        public IHttpActionResult GetOnlinePlayers()
        {
            var players = _playerManager.GetAllOnline();
            return Ok(ApiResponse.Ok(players));
        }

        /// <summary>
        /// Get all known players (online + offline) with pagination.
        /// Online players include full live stats; offline players show name and last-seen time.
        /// </summary>
        [HttpGet]
        [Route("known")]
        public IHttpActionResult GetKnownPlayers(int pageIndex = 0, int pageSize = 100, string search = null)
        {
            var knownPlayers = _pointsRepo.GetAll(pageIndex, pageSize, search);
            var total = _pointsRepo.GetTotalCount(search);

            // Build a lookup of online players by playerId
            var onlinePlayers = _playerManager.GetAllOnline();
            var onlineByPlayerId = new Dictionary<string, PlayerInfo>();
            foreach (var op in onlinePlayers)
            {
                if (!string.IsNullOrEmpty(op.PlayerId))
                    onlineByPlayerId[op.PlayerId] = op;
            }

            var result = new List<PlayerInfo>();
            foreach (var known in knownPlayers)
            {
                if (onlineByPlayerId.TryGetValue(known.Id, out var online))
                {
                    result.Add(online);
                }
                else
                {
                    // Build a minimal offline record
                    long lastSeenTicks = 0;
                    if (!string.IsNullOrEmpty(known.LastSignInAt) &&
                        DateTime.TryParse(known.LastSignInAt, out var lastSeen))
                    {
                        lastSeenTicks = new DateTimeOffset(lastSeen, TimeSpan.Zero).ToUnixTimeSeconds();
                    }
                    else if (!string.IsNullOrEmpty(known.CreatedAt) &&
                             DateTime.TryParse(known.CreatedAt, out var created))
                    {
                        lastSeenTicks = new DateTimeOffset(created, TimeSpan.Zero).ToUnixTimeSeconds();
                    }

                    result.Add(new PlayerInfo
                    {
                        PlayerId = known.Id,
                        PlayerName = known.PlayerName ?? "Unknown",
                        IsOnline = false,
                        LastLogin = lastSeenTicks,
                        EntityId = -1
                    });
                }
            }

            return Ok(ApiResponse.Ok(new { items = result, total, pageIndex, pageSize }));
        }

        /// <summary>
        /// Get detailed player info including inventory and skills.
        /// </summary>
        [HttpGet]
        [Route("{entityId:int}")]
        public IHttpActionResult GetPlayer(int entityId)
        {
            var detail = _playerManager.GetPlayerDetail(entityId);
            if (detail == null)
                return NotFound();

            return Ok(ApiResponse.Ok(detail));
        }

        /// <summary>
        /// Get player inventory (bag + belt).
        /// </summary>
        [HttpGet]
        [Route("{entityId:int}/inventory")]
        public IHttpActionResult GetInventory(int entityId)
        {
            var player = _playerManager.GetByEntityId(entityId);
            if (player == null)
                return NotFound();

            var (bag, belt) = _playerManager.GetPlayerInventory(entityId);
            return Ok(ApiResponse.Ok(new { bagItems = bag, beltItems = belt }));
        }

        /// <summary>
        /// Get player skills and perks.
        /// </summary>
        [HttpGet]
        [Route("{entityId:int}/skills")]
        public IHttpActionResult GetSkills(int entityId)
        {
            var player = _playerManager.GetByEntityId(entityId);
            if (player == null)
                return NotFound();

            var skills = _playerManager.GetPlayerSkills(entityId);
            return Ok(ApiResponse.Ok(skills));
        }

        /// <summary>
        /// Kick a player from the server.
        /// </summary>
        [HttpPost]
        [Route("{entityId:int}/kick")]
        [RoleAuthorize("admin", "moderator")]
        public IHttpActionResult KickPlayer(int entityId, [FromBody] KickRequest request)
        {
            var player = _playerManager.GetByEntityId(entityId);
            if (player == null)
                return NotFound();

            var reason = request?.Reason ?? "Kicked by admin";
            var output = ExecuteConsoleCommand($"kick {entityId} \"{reason}\"");
            return Ok(ApiResponse.Ok(new { output }));
        }

        /// <summary>
        /// Ban a player from the server.
        /// </summary>
        [HttpPost]
        [Route("{entityId:int}/ban")]
        [RoleAuthorize("admin")]
        public IHttpActionResult BanPlayer(int entityId, [FromBody] BanRequest request)
        {
            var player = _playerManager.GetByEntityId(entityId);
            if (player == null)
                return NotFound();

            var reason = request?.Reason ?? "Banned by admin";
            var duration = request?.DurationMinutes ?? 0;

            // ban add <name/entity_id/platform_id> <duration> <reason>
            var cmd = duration > 0
                ? $"ban add {entityId} {duration} minutes \"{reason}\""
                : $"ban add {entityId} \"{reason}\"";

            var output = ExecuteConsoleCommand(cmd);
            return Ok(ApiResponse.Ok(new { output }));
        }

        /// <summary>
        /// Give an item to a player.
        /// </summary>
        [HttpPost]
        [Route("{entityId:int}/give")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GiveItem(int entityId, [FromBody] GiveRequest request)
        {
            var player = _playerManager.GetByEntityId(entityId);
            if (player == null)
                return Content(System.Net.HttpStatusCode.NotFound,
                    ApiResponse.Error(404, $"Player with entity {entityId} not found or not online."));

            if (string.IsNullOrWhiteSpace(request?.ItemName))
                return BadRequest("ItemName is required.");

            var count = request.Count > 0 ? request.Count : 1;
            var quality = request.Quality > 0 ? request.Quality : 1;

            try
            {
                var result = _playerManager.GiveItemToPlayer(entityId, request.ItemName, count, quality);
                if (result == null)
                    return Content(System.Net.HttpStatusCode.InternalServerError,
                        ApiResponse.Error(500, "Give item timed out — the game server main thread may be busy."));

                if (!result.Success)
                    return Content(System.Net.HttpStatusCode.BadRequest, ApiResponse.Error(400, result.Message));

                return Ok(ApiResponse.Ok(new { message = result.Message }));
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] GiveItem error: {ex}");
                return Content(System.Net.HttpStatusCode.InternalServerError,
                    ApiResponse.Error(500, $"Server error: {ex.Message}"));
            }
        }

        /// <summary>
        /// Teleport a player to coordinates.
        /// </summary>
        [HttpPost]
        [Route("{entityId:int}/teleport")]
        [RoleAuthorize("admin")]
        public IHttpActionResult TeleportPlayer(int entityId, [FromBody] TeleportRequest request)
        {
            var player = _playerManager.GetByEntityId(entityId);
            if (player == null)
                return NotFound();

            var output = ExecuteConsoleCommand(
                $"teleportplayer {entityId} {(int)request.X} {(int)request.Y} {(int)request.Z}");
            return Ok(ApiResponse.Ok(new { output }));
        }

        /// <summary>
        /// Change a player's in-game admin permission level.
        /// </summary>
        [HttpPost]
        [Route("{entityId:int}/admin-level")]
        [RoleAuthorize("admin")]
        public IHttpActionResult SetAdminLevel(int entityId, [FromBody] SetAdminLevelRequest request)
        {
            var player = _playerManager.GetByEntityId(entityId);
            if (player == null)
                return NotFound();

            if (request == null)
                return BadRequest("Request body is required.");

            // Prefer PlayerId (EOS crossplatform ID) for admin commands, fall back to PlatformId
            var adminId = !string.IsNullOrEmpty(player.PlayerId) ? player.PlayerId : player.PlatformId;

            string output;
            if (request.Level >= 1000)
            {
                // Remove admin privileges
                output = ExecuteConsoleCommand($"admin remove {adminId}");
            }
            else
            {
                // Set admin level (0 = admin, 1 = moderator)
                output = ExecuteConsoleCommand($"admin add {adminId} {request.Level}");
            }

            return Ok(ApiResponse.Ok(new { output }));
        }

        /// <summary>
        /// Executes a console command on the main thread and returns the output.
        /// </summary>
        private string ExecuteConsoleCommand(string command)
        {
            string result = null;
            var waitHandle = new ManualResetEventSlim(false);

            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    var output = SdtdConsole.Instance.ExecuteSync(command, null);
                    result = output != null ? string.Join("\n", output) : "";
                }
                catch (Exception ex)
                {
                    result = $"Error: {ex.Message}";
                }
                finally
                {
                    waitHandle.Set();
                }
            }, null);

            waitHandle.Wait(TimeSpan.FromSeconds(10));
            return result ?? "Command timed out.";
        }
    }
}
