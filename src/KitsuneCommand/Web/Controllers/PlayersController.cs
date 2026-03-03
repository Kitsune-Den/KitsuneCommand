using System.Web.Http;
using KitsuneCommand.Core;
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

        public PlayersController(LivePlayerManager playerManager)
        {
            _playerManager = playerManager;
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
                return NotFound();

            if (string.IsNullOrWhiteSpace(request?.ItemName))
                return BadRequest("ItemName is required.");

            var count = request.Count > 0 ? request.Count : 1;
            var quality = request.Quality > 0 ? request.Quality : 1;

            var output = ExecuteConsoleCommand($"give {entityId} {request.ItemName} {count} {quality}");
            return Ok(ApiResponse.Ok(new { output }));
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
