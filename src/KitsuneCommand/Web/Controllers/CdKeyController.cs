using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// CD Key endpoints: CRUD for redeemable codes, link items/commands,
    /// redeem with item delivery, and redemption history.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/cdkeys")]
    public class CdKeyController : ApiController
    {
        private readonly ICdKeyRepository _cdKeyRepo;
        private readonly LivePlayerManager _playerManager;

        public CdKeyController(
            ICdKeyRepository cdKeyRepo,
            LivePlayerManager playerManager)
        {
            _cdKeyRepo = cdKeyRepo;
            _playerManager = playerManager;
        }

        // ─── CD Key CRUD ────────────────────────────────────────

        /// <summary>
        /// Admin: List all CD keys (paginated).
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetCdKeys(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] string search = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _cdKeyRepo.GetAll(pageIndex, pageSize, search);
            var total = _cdKeyRepo.GetTotalCount(search);

            return Ok(ApiResponse.Ok(new PaginatedResponse<CdKey>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        /// <summary>
        /// Admin: Get CD key detail with linked items/commands and redeem count.
        /// </summary>
        [HttpGet]
        [Route("{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetCdKeyDetail(int id)
        {
            var cdKey = _cdKeyRepo.GetById(id);
            if (cdKey == null) return NotFound();

            var detail = new CdKeyDetailResponse
            {
                Id = cdKey.Id,
                CreatedAt = cdKey.CreatedAt,
                Key = cdKey.Key,
                MaxRedeemCount = cdKey.MaxRedeemCount,
                ExpiryAt = cdKey.ExpiryAt,
                Description = cdKey.Description,
                CurrentRedeemCount = _cdKeyRepo.GetRedeemCount(id),
                Items = _cdKeyRepo.GetItemsForKey(id).ToList(),
                Commands = _cdKeyRepo.GetCommandsForKey(id).ToList()
            };

            return Ok(ApiResponse.Ok(detail));
        }

        /// <summary>
        /// Admin: Create a new CD key.
        /// </summary>
        [HttpPost]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult CreateCdKey([FromBody] CreateCdKeyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Key))
                return BadRequest("Key is required.");
            if (request.MaxRedeemCount < 1)
                return BadRequest("MaxRedeemCount must be at least 1.");

            // Check for duplicate key
            var existing = _cdKeyRepo.GetByKey(request.Key.Trim());
            if (existing != null)
                return BadRequest($"A CD key with the code '{request.Key.Trim()}' already exists.");

            var cdKey = new CdKey
            {
                Key = request.Key.Trim(),
                MaxRedeemCount = request.MaxRedeemCount,
                ExpiryAt = request.ExpiryAt,
                Description = request.Description
            };

            var id = _cdKeyRepo.Insert(cdKey);

            if (request.ItemIds?.Count > 0)
                _cdKeyRepo.SetKeyItems(id, request.ItemIds);
            if (request.CommandIds?.Count > 0)
                _cdKeyRepo.SetKeyCommands(id, request.CommandIds);

            return Ok(ApiResponse.Ok(new { id }));
        }

        /// <summary>
        /// Admin: Update a CD key.
        /// </summary>
        [HttpPut]
        [Route("{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateCdKey(int id, [FromBody] UpdateCdKeyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Key))
                return BadRequest("Key is required.");

            var existing = _cdKeyRepo.GetById(id);
            if (existing == null) return NotFound();

            // Check for duplicate key (different ID)
            var duplicate = _cdKeyRepo.GetByKey(request.Key.Trim());
            if (duplicate != null && duplicate.Id != id)
                return BadRequest($"A CD key with the code '{request.Key.Trim()}' already exists.");

            existing.Key = request.Key.Trim();
            existing.MaxRedeemCount = request.MaxRedeemCount;
            existing.ExpiryAt = request.ExpiryAt;
            existing.Description = request.Description;
            _cdKeyRepo.Update(existing);

            if (request.ItemIds != null)
                _cdKeyRepo.SetKeyItems(id, request.ItemIds);
            if (request.CommandIds != null)
                _cdKeyRepo.SetKeyCommands(id, request.CommandIds);

            return Ok(ApiResponse.Ok(new { id }));
        }

        /// <summary>
        /// Admin: Delete a CD key.
        /// </summary>
        [HttpDelete]
        [Route("{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult DeleteCdKey(int id)
        {
            var existing = _cdKeyRepo.GetById(id);
            if (existing == null) return NotFound();

            _cdKeyRepo.Delete(id);
            return Ok(ApiResponse.Ok("Deleted."));
        }

        // ─── Redemption ─────────────────────────────────────────

        /// <summary>
        /// Admin/Moderator: Redeem a CD key for a player.
        /// Validates expiry, max redeems, duplicate, and delivers items/commands.
        /// </summary>
        [HttpPost]
        [Route("{id:int}/redeem")]
        [RoleAuthorize("admin", "moderator")]
        public IHttpActionResult RedeemKey(int id, [FromBody] RedeemCdKeyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PlayerId))
                return BadRequest("PlayerId is required.");

            // 1. Load CD key
            var cdKey = _cdKeyRepo.GetById(id);
            if (cdKey == null) return NotFound();

            // 2. Check expiry
            if (!string.IsNullOrEmpty(cdKey.ExpiryAt))
            {
                if (DateTime.TryParse(cdKey.ExpiryAt, out var expiry) && DateTime.UtcNow > expiry)
                    return BadRequest("This CD key has expired.");
            }

            // 3. Check max redeem count
            var currentCount = _cdKeyRepo.GetRedeemCount(id);
            if (currentCount >= cdKey.MaxRedeemCount)
                return BadRequest($"This CD key has reached its maximum redemption limit ({cdKey.MaxRedeemCount}).");

            // 4. Check duplicate redemption
            if (_cdKeyRepo.HasPlayerRedeemed(id, request.PlayerId))
                return BadRequest("This player has already redeemed this CD key.");

            // 5. Find player online
            var onlinePlayers = _playerManager.GetAllOnline();
            var player = onlinePlayers.FirstOrDefault(p =>
                string.Equals(p.PlayerId, request.PlayerId, StringComparison.OrdinalIgnoreCase));
            if (player == null)
                return BadRequest("Player is not online. They must be connected to receive items.");

            // 6. Load linked items and commands
            var items = _cdKeyRepo.GetItemsForKey(id).ToList();
            var commands = _cdKeyRepo.GetCommandsForKey(id).ToList();

            // 7. Execute item gives
            var deliveryLog = new List<string>();
            foreach (var item in items)
            {
                var cmd = $"give {player.EntityId} {item.ItemName} {item.Count} {item.Quality}";
                var output = ExecuteConsoleCommand(cmd);
                deliveryLog.Add($"[Item] {item.ItemName} x{item.Count}: {output}");
            }

            // 8. Execute commands with placeholder substitution
            foreach (var cmdDef in commands)
            {
                var cmd = cmdDef.Command
                    .Replace("{entityId}", player.EntityId.ToString())
                    .Replace("{playerId}", request.PlayerId)
                    .Replace("{playerName}", player.PlayerName);

                var output = ExecuteConsoleCommand(cmd);
                deliveryLog.Add($"[Cmd] {cmdDef.Command}: {output}");
            }

            // 9. Record redemption
            _cdKeyRepo.InsertRedeemRecord(new CdKeyRedeemRecord
            {
                CdKeyId = id,
                PlayerId = request.PlayerId,
                PlayerName = request.PlayerName ?? player.PlayerName
            });

            return Ok(ApiResponse.Ok(new
            {
                message = $"CD key '{cdKey.Key}' redeemed for {player.PlayerName}.",
                deliveryLog,
                remainingRedemptions = cdKey.MaxRedeemCount - currentCount - 1
            }));
        }

        // ─── Redemption History ─────────────────────────────────

        /// <summary>
        /// Admin: Get redemption records for a specific key.
        /// </summary>
        [HttpGet]
        [Route("{id:int}/redemptions")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetKeyRedemptions(int id)
        {
            var cdKey = _cdKeyRepo.GetById(id);
            if (cdKey == null) return NotFound();

            var records = _cdKeyRepo.GetRedeemRecords(id);
            return Ok(ApiResponse.Ok(records));
        }

        /// <summary>
        /// Admin: Get all redemption records (paginated).
        /// </summary>
        [HttpGet]
        [Route("redemptions")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetAllRedemptions(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] int? cdKeyId = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _cdKeyRepo.GetRedeemHistory(pageIndex, pageSize, cdKeyId);
            var total = _cdKeyRepo.GetRedeemHistoryCount(cdKeyId);

            return Ok(ApiResponse.Ok(new PaginatedResponse<CdKeyRedeemRecord>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        // ─── Helpers ────────────────────────────────────────────

        /// <summary>
        /// Executes a console command on the main thread and returns the output.
        /// </summary>
        private static string ExecuteConsoleCommand(string command)
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
