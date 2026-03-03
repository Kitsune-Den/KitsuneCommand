using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Game store endpoints: browse goods, CRUD item/command definitions,
    /// purchase with point deduction and in-game delivery.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/store")]
    public class StoreController : ApiController
    {
        private readonly IGoodsRepository _goodsRepo;
        private readonly IItemDefinitionRepository _itemDefRepo;
        private readonly ICommandDefinitionRepository _cmdDefRepo;
        private readonly IPurchaseHistoryRepository _purchaseRepo;
        private readonly IPointsRepository _pointsRepo;
        private readonly LivePlayerManager _playerManager;
        private readonly ModEventBus _eventBus;

        public StoreController(
            IGoodsRepository goodsRepo,
            IItemDefinitionRepository itemDefRepo,
            ICommandDefinitionRepository cmdDefRepo,
            IPurchaseHistoryRepository purchaseRepo,
            IPointsRepository pointsRepo,
            LivePlayerManager playerManager,
            ModEventBus eventBus)
        {
            _goodsRepo = goodsRepo;
            _itemDefRepo = itemDefRepo;
            _cmdDefRepo = cmdDefRepo;
            _purchaseRepo = purchaseRepo;
            _pointsRepo = pointsRepo;
            _playerManager = playerManager;
            _eventBus = eventBus;
        }

        // ─── Goods Browsing ──────────────────────────────────────

        /// <summary>
        /// Browse all store goods (paginated).
        /// </summary>
        [HttpGet]
        [Route("goods")]
        public IHttpActionResult GetGoods(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] string search = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _goodsRepo.GetAll(pageIndex, pageSize, search);
            var total = _goodsRepo.GetTotalCount(search);

            return Ok(ApiResponse.Ok(new PaginatedResponse<Goods>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        /// <summary>
        /// Get a single goods entry with its linked items and commands.
        /// </summary>
        [HttpGet]
        [Route("goods/{id:int}")]
        public IHttpActionResult GetGoodsDetail(int id)
        {
            var goods = _goodsRepo.GetById(id);
            if (goods == null) return NotFound();

            var detail = new GoodsDetailResponse
            {
                Id = goods.Id,
                CreatedAt = goods.CreatedAt,
                Name = goods.Name,
                Price = goods.Price,
                Description = goods.Description,
                Items = _goodsRepo.GetItemsForGoods(id).ToList(),
                Commands = _goodsRepo.GetCommandsForGoods(id).ToList()
            };

            return Ok(ApiResponse.Ok(detail));
        }

        /// <summary>
        /// Admin: Create a new goods entry.
        /// </summary>
        [HttpPost]
        [Route("goods")]
        [RoleAuthorize("admin")]
        public IHttpActionResult CreateGoods([FromBody] CreateGoodsRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Name is required.");
            if (request.Price < 0)
                return BadRequest("Price must be non-negative.");

            var goods = new Goods
            {
                Name = request.Name,
                Price = request.Price,
                Description = request.Description
            };

            var id = _goodsRepo.Insert(goods);

            if (request.ItemIds?.Count > 0)
                _goodsRepo.SetGoodsItems(id, request.ItemIds);
            if (request.CommandIds?.Count > 0)
                _goodsRepo.SetGoodsCommands(id, request.CommandIds);

            return Ok(ApiResponse.Ok(new { id }));
        }

        /// <summary>
        /// Admin: Update a goods entry.
        /// </summary>
        [HttpPut]
        [Route("goods/{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateGoods(int id, [FromBody] UpdateGoodsRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Name is required.");

            var existing = _goodsRepo.GetById(id);
            if (existing == null) return NotFound();

            existing.Name = request.Name;
            existing.Price = request.Price;
            existing.Description = request.Description;
            _goodsRepo.Update(existing);

            if (request.ItemIds != null)
                _goodsRepo.SetGoodsItems(id, request.ItemIds);
            if (request.CommandIds != null)
                _goodsRepo.SetGoodsCommands(id, request.CommandIds);

            return Ok(ApiResponse.Ok(new { id }));
        }

        /// <summary>
        /// Admin: Delete a goods entry.
        /// </summary>
        [HttpDelete]
        [Route("goods/{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult DeleteGoods(int id)
        {
            var existing = _goodsRepo.GetById(id);
            if (existing == null) return NotFound();

            _goodsRepo.Delete(id);
            return Ok(ApiResponse.Ok("Deleted."));
        }

        // ─── Purchase ────────────────────────────────────────────

        /// <summary>
        /// Admin/Moderator: Purchase a goods entry for a player.
        /// Deducts points, executes give/commands, records history.
        /// </summary>
        [HttpPost]
        [Route("goods/{id:int}/buy")]
        [RoleAuthorize("admin", "moderator")]
        public IHttpActionResult BuyGoods(int id, [FromBody] BuyGoodsRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PlayerId))
                return BadRequest("PlayerId is required.");

            // 1. Load goods + linked items + commands
            var goods = _goodsRepo.GetById(id);
            if (goods == null) return NotFound();

            var items = _goodsRepo.GetItemsForGoods(id).ToList();
            var commands = _goodsRepo.GetCommandsForGoods(id).ToList();

            // 2. Find buyer's entity ID (they must be online)
            var onlinePlayers = _playerManager.GetAllOnline();
            var buyer = onlinePlayers.FirstOrDefault(p =>
                string.Equals(p.PlayerId, request.PlayerId, StringComparison.OrdinalIgnoreCase));

            if (buyer == null)
                return BadRequest("Player is not online. They must be connected to receive items.");

            // 3. Check sufficient points
            var pointsInfo = _pointsRepo.GetByPlayerId(request.PlayerId);
            if (pointsInfo == null || pointsInfo.Points < goods.Price)
                return BadRequest($"Insufficient points. Required: {goods.Price}, Available: {pointsInfo?.Points ?? 0}");

            // 4. Deduct points
            var newBalance = _pointsRepo.AdjustPoints(request.PlayerId, -goods.Price);

            // 5. Execute item gives
            var deliveryLog = new List<string>();
            foreach (var item in items)
            {
                var cmd = $"give {buyer.EntityId} {item.ItemName} {item.Count} {item.Quality}";
                var output = ExecuteConsoleCommand(cmd);
                deliveryLog.Add($"[Item] {item.ItemName} x{item.Count}: {output}");
            }

            // 6. Execute commands
            foreach (var cmdDef in commands)
            {
                var cmd = cmdDef.Command
                    .Replace("{entityId}", buyer.EntityId.ToString())
                    .Replace("{playerId}", request.PlayerId)
                    .Replace("{playerName}", buyer.PlayerName);

                var output = ExecuteConsoleCommand(cmd);
                deliveryLog.Add($"[Cmd] {cmdDef.Command}: {output}");
            }

            // 7. Record purchase
            _purchaseRepo.Insert(new PurchaseRecord
            {
                PlayerId = request.PlayerId,
                PlayerName = request.PlayerName ?? buyer.PlayerName,
                GoodsId = goods.Id,
                GoodsName = goods.Name,
                Price = goods.Price
            });

            // 8. Broadcast points update
            _eventBus.Publish(new PointsUpdateEvent
            {
                PlayerId = request.PlayerId,
                PlayerName = buyer.PlayerName,
                Points = newBalance,
                Change = -goods.Price,
                Reason = $"Purchased {goods.Name}"
            });

            return Ok(ApiResponse.Ok(new
            {
                message = $"Successfully purchased {goods.Name} for {buyer.PlayerName}.",
                newBalance,
                deliveryLog
            }));
        }

        // ─── Purchase History ────────────────────────────────────

        /// <summary>
        /// Get paginated purchase history, optionally filtered by player.
        /// </summary>
        [HttpGet]
        [Route("history")]
        public IHttpActionResult GetPurchaseHistory(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] string playerId = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _purchaseRepo.GetHistory(pageIndex, pageSize, playerId);
            var total = _purchaseRepo.GetTotalCount(playerId);

            return Ok(ApiResponse.Ok(new PaginatedResponse<PurchaseRecord>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        // ─── Item Definitions CRUD ───────────────────────────────

        [HttpGet]
        [Route("item-definitions")]
        public IHttpActionResult GetItemDefinitions()
        {
            return Ok(ApiResponse.Ok(_itemDefRepo.GetAll()));
        }

        [HttpGet]
        [Route("item-definitions/{id:int}")]
        public IHttpActionResult GetItemDefinition(int id)
        {
            var item = _itemDefRepo.GetById(id);
            if (item == null) return NotFound();
            return Ok(ApiResponse.Ok(item));
        }

        [HttpPost]
        [Route("item-definitions")]
        [RoleAuthorize("admin")]
        public IHttpActionResult CreateItemDefinition([FromBody] CreateItemDefinitionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ItemName))
                return BadRequest("ItemName is required.");

            var item = new ItemDefinition
            {
                ItemName = request.ItemName,
                Count = request.Count,
                Quality = request.Quality,
                Durability = request.Durability,
                Description = request.Description
            };

            var id = _itemDefRepo.Insert(item);
            return Ok(ApiResponse.Ok(new { id }));
        }

        [HttpPut]
        [Route("item-definitions/{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateItemDefinition(int id, [FromBody] UpdateItemDefinitionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ItemName))
                return BadRequest("ItemName is required.");

            var existing = _itemDefRepo.GetById(id);
            if (existing == null) return NotFound();

            existing.ItemName = request.ItemName;
            existing.Count = request.Count;
            existing.Quality = request.Quality;
            existing.Durability = request.Durability;
            existing.Description = request.Description;
            _itemDefRepo.Update(existing);

            return Ok(ApiResponse.Ok(new { id }));
        }

        [HttpDelete]
        [Route("item-definitions/{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult DeleteItemDefinition(int id)
        {
            var existing = _itemDefRepo.GetById(id);
            if (existing == null) return NotFound();

            _itemDefRepo.Delete(id);
            return Ok(ApiResponse.Ok("Deleted."));
        }

        // ─── Command Definitions CRUD ────────────────────────────

        [HttpGet]
        [Route("command-definitions")]
        public IHttpActionResult GetCommandDefinitions()
        {
            return Ok(ApiResponse.Ok(_cmdDefRepo.GetAll()));
        }

        [HttpGet]
        [Route("command-definitions/{id:int}")]
        public IHttpActionResult GetCommandDefinition(int id)
        {
            var cmd = _cmdDefRepo.GetById(id);
            if (cmd == null) return NotFound();
            return Ok(ApiResponse.Ok(cmd));
        }

        [HttpPost]
        [Route("command-definitions")]
        [RoleAuthorize("admin")]
        public IHttpActionResult CreateCommandDefinition([FromBody] CreateCommandDefinitionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Command))
                return BadRequest("Command is required.");

            var cmd = new CommandDefinition
            {
                Command = request.Command,
                RunInMainThread = request.RunInMainThread,
                Description = request.Description
            };

            var id = _cmdDefRepo.Insert(cmd);
            return Ok(ApiResponse.Ok(new { id }));
        }

        [HttpPut]
        [Route("command-definitions/{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateCommandDefinition(int id, [FromBody] UpdateCommandDefinitionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Command))
                return BadRequest("Command is required.");

            var existing = _cmdDefRepo.GetById(id);
            if (existing == null) return NotFound();

            existing.Command = request.Command;
            existing.RunInMainThread = request.RunInMainThread;
            existing.Description = request.Description;
            _cmdDefRepo.Update(existing);

            return Ok(ApiResponse.Ok(new { id }));
        }

        [HttpDelete]
        [Route("command-definitions/{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult DeleteCommandDefinition(int id)
        {
            var existing = _cmdDefRepo.GetById(id);
            if (existing == null) return NotFound();

            _cmdDefRepo.Delete(id);
            return Ok(ApiResponse.Ok("Deleted."));
        }

        // ─── Helpers ─────────────────────────────────────────────

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
