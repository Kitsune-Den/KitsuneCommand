using System.Globalization;
using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Teleport endpoints: city location CRUD, home location management,
    /// teleport execution with point deduction, and teleport history.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/teleport")]
    public class TeleportController : ApiController
    {
        private readonly ICityLocationRepository _cityRepo;
        private readonly IHomeLocationRepository _homeRepo;
        private readonly ITeleRecordRepository _teleRecordRepo;
        private readonly IPointsRepository _pointsRepo;
        private readonly LivePlayerManager _playerManager;
        private readonly ModEventBus _eventBus;

        public TeleportController(
            ICityLocationRepository cityRepo,
            IHomeLocationRepository homeRepo,
            ITeleRecordRepository teleRecordRepo,
            IPointsRepository pointsRepo,
            LivePlayerManager playerManager,
            ModEventBus eventBus)
        {
            _cityRepo = cityRepo;
            _homeRepo = homeRepo;
            _teleRecordRepo = teleRecordRepo;
            _pointsRepo = pointsRepo;
            _playerManager = playerManager;
            _eventBus = eventBus;
        }

        // ─── City Locations CRUD ────────────────────────────────────

        /// <summary>
        /// List all city locations (paginated).
        /// </summary>
        [HttpGet]
        [Route("cities")]
        public IHttpActionResult GetCities(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] string search = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _cityRepo.GetAll(pageIndex, pageSize, search);
            var total = _cityRepo.GetTotalCount(search);

            return Ok(ApiResponse.Ok(new PaginatedResponse<CityLocation>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        /// <summary>
        /// Get a single city location.
        /// </summary>
        [HttpGet]
        [Route("cities/{id:int}")]
        public IHttpActionResult GetCity(int id)
        {
            var city = _cityRepo.GetById(id);
            if (city == null) return NotFound();
            return Ok(ApiResponse.Ok(city));
        }

        /// <summary>
        /// Admin: Create a new city location.
        /// </summary>
        [HttpPost]
        [Route("cities")]
        [RoleAuthorize("admin")]
        public IHttpActionResult CreateCity([FromBody] CreateCityLocationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CityName))
                return BadRequest("CityName is required.");
            if (string.IsNullOrWhiteSpace(request.Position))
                return BadRequest("Position is required.");
            if (!TryParsePosition(request.Position, out _, out _, out _))
                return BadRequest("Position must be in 'x y z' format (e.g., '100 65 -200').");
            if (request.PointsRequired < 0)
                return BadRequest("PointsRequired must be non-negative.");

            var city = new CityLocation
            {
                CityName = request.CityName,
                PointsRequired = request.PointsRequired,
                Position = request.Position.Trim(),
                ViewDirection = request.ViewDirection?.Trim()
            };

            var id = _cityRepo.Insert(city);
            return Ok(ApiResponse.Ok(new { id }));
        }

        /// <summary>
        /// Admin: Update a city location.
        /// </summary>
        [HttpPut]
        [Route("cities/{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateCity(int id, [FromBody] UpdateCityLocationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CityName))
                return BadRequest("CityName is required.");
            if (string.IsNullOrWhiteSpace(request.Position))
                return BadRequest("Position is required.");
            if (!TryParsePosition(request.Position, out _, out _, out _))
                return BadRequest("Position must be in 'x y z' format (e.g., '100 65 -200').");

            var existing = _cityRepo.GetById(id);
            if (existing == null) return NotFound();

            existing.CityName = request.CityName;
            existing.PointsRequired = request.PointsRequired;
            existing.Position = request.Position.Trim();
            existing.ViewDirection = request.ViewDirection?.Trim();
            _cityRepo.Update(existing);

            return Ok(ApiResponse.Ok(new { id }));
        }

        /// <summary>
        /// Admin: Delete a city location.
        /// </summary>
        [HttpDelete]
        [Route("cities/{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult DeleteCity(int id)
        {
            var existing = _cityRepo.GetById(id);
            if (existing == null) return NotFound();

            _cityRepo.Delete(id);
            return Ok(ApiResponse.Ok("Deleted."));
        }

        /// <summary>
        /// Admin/Moderator: Teleport a player to a city location.
        /// Deducts points if the city has a cost.
        /// </summary>
        [HttpPost]
        [Route("cities/{id:int}/teleport")]
        [RoleAuthorize("admin", "moderator")]
        public IHttpActionResult TeleportToCity(int id, [FromBody] TeleportToCityRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PlayerId))
                return BadRequest("PlayerId is required.");

            // 1. Load city
            var city = _cityRepo.GetById(id);
            if (city == null) return NotFound();

            // 2. Find player online
            var onlinePlayers = _playerManager.GetAllOnline();
            var player = onlinePlayers.FirstOrDefault(p =>
                string.Equals(p.PlayerId, request.PlayerId, StringComparison.OrdinalIgnoreCase));
            if (player == null)
                return BadRequest("Player is not online.");

            // 3. Check & deduct points if cost > 0
            if (city.PointsRequired > 0)
            {
                var pointsInfo = _pointsRepo.GetByPlayerId(request.PlayerId);
                if (pointsInfo == null || pointsInfo.Points < city.PointsRequired)
                    return BadRequest($"Insufficient points. Required: {city.PointsRequired}, Available: {pointsInfo?.Points ?? 0}");

                var newBalance = _pointsRepo.AdjustPoints(request.PlayerId, -city.PointsRequired);
                _eventBus.Publish(new PointsUpdateEvent
                {
                    PlayerId = request.PlayerId,
                    PlayerName = player.PlayerName,
                    Points = newBalance,
                    Change = -city.PointsRequired,
                    Reason = $"Teleport to {city.CityName}"
                });
            }

            // 4. Parse city position
            TryParsePosition(city.Position, out var cx, out var cy, out var cz);

            // 5. Execute teleport command on main thread
            var cmd = $"teleportplayer {player.EntityId} {cx.ToString(CultureInfo.InvariantCulture)} {cy.ToString(CultureInfo.InvariantCulture)} {cz.ToString(CultureInfo.InvariantCulture)}";
            var output = ExecuteConsoleCommand(cmd);

            // 6. Record teleport
            var originPos = $"{player.PositionX.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionY.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionZ.ToString("F1", CultureInfo.InvariantCulture)}";
            _teleRecordRepo.Insert(new TeleRecord
            {
                PlayerId = request.PlayerId,
                PlayerName = request.PlayerName ?? player.PlayerName,
                TargetType = 0, // City
                TargetName = city.CityName,
                OriginPosition = originPos,
                TargetPosition = city.Position
            });

            return Ok(ApiResponse.Ok(new
            {
                message = $"Teleported {player.PlayerName} to {city.CityName}.",
                output
            }));
        }

        // ─── Home Locations ─────────────────────────────────────────

        /// <summary>
        /// List all home locations (paginated, searchable by player/home name).
        /// </summary>
        [HttpGet]
        [Route("homes")]
        public IHttpActionResult GetHomes(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] string search = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _homeRepo.GetAll(pageIndex, pageSize, search);
            var total = _homeRepo.GetTotalCount(search);

            return Ok(ApiResponse.Ok(new PaginatedResponse<HomeLocation>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        /// <summary>
        /// Get all homes for a specific player.
        /// </summary>
        [HttpGet]
        [Route("homes/player/{playerId}")]
        public IHttpActionResult GetPlayerHomes(string playerId)
        {
            var homes = _homeRepo.GetByPlayerId(playerId);
            return Ok(ApiResponse.Ok(homes));
        }

        /// <summary>
        /// Admin: Delete a home location.
        /// </summary>
        [HttpDelete]
        [Route("homes/{id:int}")]
        [RoleAuthorize("admin")]
        public IHttpActionResult DeleteHome(int id)
        {
            var existing = _homeRepo.GetById(id);
            if (existing == null) return NotFound();

            _homeRepo.Delete(id);
            return Ok(ApiResponse.Ok("Deleted."));
        }

        /// <summary>
        /// Admin/Moderator: Teleport a player to their home location.
        /// </summary>
        [HttpPost]
        [Route("homes/{id:int}/teleport")]
        [RoleAuthorize("admin", "moderator")]
        public IHttpActionResult TeleportToHome(int id, [FromBody] TeleportToHomeRequest request)
        {
            // 1. Load home
            var home = _homeRepo.GetById(id);
            if (home == null) return NotFound();

            // 2. Determine which player to teleport
            var playerId = request?.PlayerId;
            if (string.IsNullOrWhiteSpace(playerId))
                playerId = home.PlayerId;

            // 3. Find player online
            var onlinePlayers = _playerManager.GetAllOnline();
            var player = onlinePlayers.FirstOrDefault(p =>
                string.Equals(p.PlayerId, playerId, StringComparison.OrdinalIgnoreCase));
            if (player == null)
                return BadRequest("Player is not online.");

            // 4. Parse home position
            if (!TryParsePosition(home.Position, out var hx, out var hy, out var hz))
                return BadRequest("Home has an invalid position format.");

            // 5. Execute teleport
            var cmd = $"teleportplayer {player.EntityId} {hx.ToString(CultureInfo.InvariantCulture)} {hy.ToString(CultureInfo.InvariantCulture)} {hz.ToString(CultureInfo.InvariantCulture)}";
            var output = ExecuteConsoleCommand(cmd);

            // 6. Record teleport
            var originPos = $"{player.PositionX.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionY.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionZ.ToString("F1", CultureInfo.InvariantCulture)}";
            _teleRecordRepo.Insert(new TeleRecord
            {
                PlayerId = player.PlayerId,
                PlayerName = player.PlayerName,
                TargetType = 1, // Home
                TargetName = home.HomeName,
                OriginPosition = originPos,
                TargetPosition = home.Position
            });

            return Ok(ApiResponse.Ok(new
            {
                message = $"Teleported {player.PlayerName} to home '{home.HomeName}'.",
                output
            }));
        }

        // ─── Teleport History ───────────────────────────────────────

        /// <summary>
        /// Get paginated teleport history, optionally filtered by player.
        /// </summary>
        [HttpGet]
        [Route("history")]
        public IHttpActionResult GetTeleportHistory(
            [FromUri] int pageIndex = 0,
            [FromUri] int pageSize = 50,
            [FromUri] string playerId = null)
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 200);
            pageIndex = Math.Max(pageIndex, 0);

            var items = _teleRecordRepo.GetHistory(pageIndex, pageSize, playerId);
            var total = _teleRecordRepo.GetTotalCount(playerId);

            return Ok(ApiResponse.Ok(new PaginatedResponse<TeleRecord>
            {
                Items = items,
                Total = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            }));
        }

        // ─── Helpers ────────────────────────────────────────────────

        /// <summary>
        /// Parses a position string in "x y z" format.
        /// </summary>
        private static bool TryParsePosition(string position, out float x, out float y, out float z)
        {
            x = y = z = 0;
            if (string.IsNullOrWhiteSpace(position)) return false;
            var parts = position.Trim().Split(' ');
            return parts.Length == 3
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
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
