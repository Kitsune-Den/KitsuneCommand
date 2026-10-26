using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// VIP Gift endpoints: admin CRUD for player gift packages,
    /// claim with item/command delivery.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/vipgifts")]
    public class VipGiftController : ApiController
    {
        private readonly IVipGiftRepository _giftRepo;
        private readonly LivePlayerManager _playerManager;

        public VipGiftController(
            IVipGiftRepository giftRepo,
            LivePlayerManager playerManager)
        {
            _giftRepo = giftRepo;
            _playerManager = playerManager;
        }

        // ─── VIP Gift CRUD ──────────────────────────────────────

        /// <summary>
        /// Admin: List all VIP gifts (paginated).
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetVipGifts(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] string search = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _giftRepo.GetAll(pageIndex, pageSize, search);
            var total = _giftRepo.GetTotalCount(search);

            // Check claimability for each gift
            var enriched = items.Select(g => new VipGiftDetailResponse
            {
                Id = g.Id,
                CreatedAt = g.CreatedAt,
                PlayerId = g.PlayerId,
                PlayerName = g.PlayerName,
                Name = g.Name,
                Claimed = g.Claimed,
                TotalClaimCount = g.TotalClaimCount,
                LastClaimedAt = g.LastClaimedAt,
                Description = g.Description,
                ClaimPeriod = g.ClaimPeriod,
                IsClaimable = IsGiftClaimable(g)
            });

            return Ok(ApiResponse.Ok(new PaginatedResponse<VipGiftDetailResponse>
            {
                Items = enriched,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        /// <summary>
        /// Admin: Get VIP gift detail with linked items/commands.
        /// </summary>
        [HttpGet]
        [Route("{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetVipGiftDetail(int id)
        {
            var gift = _giftRepo.GetById(id);
            if (gift == null) return NotFound();

            var detail = new VipGiftDetailResponse
            {
                Id = gift.Id,
                CreatedAt = gift.CreatedAt,
                PlayerId = gift.PlayerId,
                PlayerName = gift.PlayerName,
                Name = gift.Name,
                Claimed = gift.Claimed,
                TotalClaimCount = gift.TotalClaimCount,
                LastClaimedAt = gift.LastClaimedAt,
                Description = gift.Description,
                ClaimPeriod = gift.ClaimPeriod,
                IsClaimable = IsGiftClaimable(gift),
                Items = _giftRepo.GetItemsForGift(id).ToList(),
                Commands = _giftRepo.GetCommandsForGift(id).ToList()
            };

            return Ok(ApiResponse.Ok(detail));
        }

        /// <summary>
        /// Admin: Create a new VIP gift for a player.
        /// </summary>
        [HttpPost]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult CreateVipGift([FromBody] CreateVipGiftRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PlayerId))
                return BadRequest("PlayerId is required.");
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Gift name is required.");

            // Validate claim period
            if (!string.IsNullOrEmpty(request.ClaimPeriod) &&
                request.ClaimPeriod != "daily" && request.ClaimPeriod != "weekly" && request.ClaimPeriod != "monthly")
                return BadRequest("ClaimPeriod must be null, 'daily', 'weekly', or 'monthly'.");

            var gift = new VipGift
            {
                PlayerId = request.PlayerId.Trim(),
                PlayerName = request.PlayerName?.Trim(),
                Name = request.Name.Trim(),
                Description = request.Description,
                ClaimPeriod = string.IsNullOrEmpty(request.ClaimPeriod) ? null : request.ClaimPeriod
            };

            var id = _giftRepo.Insert(gift);

            if (request.ItemIds?.Count > 0)
                _giftRepo.SetGiftItems(id, request.ItemIds);
            if (request.CommandIds?.Count > 0)
                _giftRepo.SetGiftCommands(id, request.CommandIds);

            return Ok(ApiResponse.Ok(new { id }));
        }

        /// <summary>
        /// Admin: Update a VIP gift.
        /// </summary>
        [HttpPut]
        [Route("{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateVipGift(int id, [FromBody] UpdateVipGiftRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Gift name is required.");

            var existing = _giftRepo.GetById(id);
            if (existing == null) return NotFound();

            // Validate claim period
            if (!string.IsNullOrEmpty(request.ClaimPeriod) &&
                request.ClaimPeriod != "daily" && request.ClaimPeriod != "weekly" && request.ClaimPeriod != "monthly")
                return BadRequest("ClaimPeriod must be null, 'daily', 'weekly', or 'monthly'.");

            existing.Name = request.Name.Trim();
            existing.Description = request.Description;
            existing.ClaimPeriod = string.IsNullOrEmpty(request.ClaimPeriod) ? null : request.ClaimPeriod;
            if (request.PlayerName != null)
                existing.PlayerName = request.PlayerName.Trim();

            _giftRepo.Update(existing);

            if (request.ItemIds != null)
                _giftRepo.SetGiftItems(id, request.ItemIds);
            if (request.CommandIds != null)
                _giftRepo.SetGiftCommands(id, request.CommandIds);

            return Ok(ApiResponse.Ok(new { id }));
        }

        /// <summary>
        /// Admin: Delete a VIP gift.
        /// </summary>
        [HttpDelete]
        [Route("{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult DeleteVipGift(int id)
        {
            var existing = _giftRepo.GetById(id);
            if (existing == null) return NotFound();

            _giftRepo.Delete(id);
            return Ok(ApiResponse.Ok("Deleted."));
        }

        // ─── Claiming ───────────────────────────────────────────

        /// <summary>
        /// Admin: Claim a VIP gift for its assigned player.
        /// Validates the gift is claimable, finds the player online,
        /// delivers items/commands, and marks as claimed.
        /// </summary>
        [HttpPost]
        [Route("{id:int}/claim")]
        [RoleAuthorize("admin")]
        public IHttpActionResult ClaimGift(int id)
        {
            // 1. Load gift
            var gift = _giftRepo.GetById(id);
            if (gift == null) return NotFound();

            // 2. Check claimability
            if (!IsGiftClaimable(gift))
                return BadRequest("This gift is not currently claimable.");

            // 3. Find player online
            var onlinePlayers = _playerManager.GetAllOnline();
            var player = onlinePlayers.FirstOrDefault(p =>
                string.Equals(p.PlayerId, gift.PlayerId, StringComparison.OrdinalIgnoreCase));
            if (player == null)
                return BadRequest("Player is not online. They must be connected to receive items.");

            // 4. Load linked items and commands
            var items = _giftRepo.GetItemsForGift(id).ToList();
            var commands = _giftRepo.GetCommandsForGift(id).ToList();

            var itemError = GameIntegration.GameItems.UnknownItemsError(items.Select(i => i.ItemName));
            if (itemError != null) return BadRequest(itemError);

            // 5. Execute item gives
            var deliveryLog = new List<string>();
            foreach (var item in items)
            {
                var cmd = $"give {player.EntityId} {item.ItemName} {item.Count} {item.Quality}";
                var output = ExecuteConsoleCommand(cmd);
                deliveryLog.Add($"[Item] {item.ItemName} x{item.Count}: {output}");
            }

            // 6. Execute commands with placeholder substitution
            foreach (var cmdDef in commands)
            {
                var cmd = cmdDef.Command
                    .Replace("{entityId}", player.EntityId.ToString())
                    .Replace("{playerId}", gift.PlayerId)
                    .Replace("{playerName}", player.PlayerName);

                var output = ExecuteConsoleCommand(cmd);
                deliveryLog.Add($"[Cmd] {cmdDef.Command}: {output}");
            }

            // 7. Mark as claimed
            _giftRepo.MarkAsClaimed(id);

            return Ok(ApiResponse.Ok(new
            {
                message = $"VIP gift '{gift.Name}' claimed for {player.PlayerName}.",
                deliveryLog
            }));
        }

        // ─── Helpers ────────────────────────────────────────────

        /// <summary>
        /// Determines if a gift is currently claimable based on its period and last claim time.
        /// </summary>
        private static bool IsGiftClaimable(VipGift gift)
        {
            // One-time gift: claimable only if not yet claimed
            if (string.IsNullOrEmpty(gift.ClaimPeriod))
                return gift.Claimed == 0;

            // Repeatable gift: check if enough time has elapsed
            if (string.IsNullOrEmpty(gift.LastClaimedAt))
                return true; // Never claimed before

            if (!DateTime.TryParse(gift.LastClaimedAt, out var lastClaimed))
                return true; // Parse error, allow claim

            var elapsed = DateTime.UtcNow - lastClaimed;

            switch (gift.ClaimPeriod.ToLowerInvariant())
            {
                case "daily":   return elapsed.TotalHours >= 24;
                case "weekly":  return elapsed.TotalDays >= 7;
                case "monthly": return elapsed.TotalDays >= 30;
                default:        return false;
            }
        }

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
