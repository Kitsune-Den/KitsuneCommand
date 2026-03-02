using System.Net;
using System.Net.Http;
using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Server management endpoints: info, stats, command execution, restart.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/server")]
    public class ServerController : ApiController
    {
        /// <summary>
        /// Get basic server information.
        /// </summary>
        [HttpGet]
        [Route("info")]
        public IHttpActionResult GetInfo()
        {
            var world = GameManager.Instance?.World;
            var gameManager = GameManager.Instance;

            var info = new
            {
                serverName = GamePrefs.GetString(EnumGamePrefs.ServerName),
                serverPort = GamePrefs.GetInt(EnumGamePrefs.ServerPort),
                maxPlayers = GamePrefs.GetInt(EnumGamePrefs.ServerMaxPlayerCount),
                gameWorld = GamePrefs.GetString(EnumGamePrefs.GameWorld),
                gameName = GamePrefs.GetString(EnumGamePrefs.GameName),
                gameMode = GamePrefs.GetString(EnumGamePrefs.GameMode),
                difficulty = GamePrefs.GetInt(EnumGamePrefs.GameDifficulty),
                dayNightLength = GamePrefs.GetInt(EnumGamePrefs.DayNightLength),
                bloodMoonFrequency = GamePrefs.GetInt(EnumGamePrefs.BloodMoonFrequency),
                currentDay = world != null ? GameUtils.WorldTimeToDays(world.worldTime) : 0,
                currentTime = world != null ? GameUtils.WorldTimeToString(world.worldTime) : "N/A",
                onlinePlayers = gameManager?.World?.Players?.Count ?? 0,
                version = Constants.cVersionInformation.LongString,
                kitsuneCommandVersion = "2.0.0"
            };

            return Ok(ApiResponse.Ok(info));
        }

        /// <summary>
        /// Get server performance stats.
        /// </summary>
        [HttpGet]
        [Route("stats")]
        public IHttpActionResult GetStats()
        {
            var gameManager = GameManager.Instance;
            var stats = new
            {
                fps = GameManager.fps,
                entityCount = gameManager?.World?.Entities?.Count ?? 0,
                playerCount = gameManager?.World?.Players?.Count ?? 0,
                uptime = Time.time,
                gcMemory = GC.GetTotalMemory(false) / (1024 * 1024) // MB
            };

            return Ok(ApiResponse.Ok(stats));
        }

        /// <summary>
        /// Execute a server console command.
        /// </summary>
        [HttpPost]
        [Route("command")]
        public IHttpActionResult ExecuteCommand([FromBody] CommandRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Command))
                return BadRequest("Command is required.");

            // Execute on main thread
            string result = null;
            var waitHandle = new ManualResetEventSlim(false);

            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    var output = SdtdConsole.Instance.ExecuteSync(request.Command, null);
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

            return Ok(ApiResponse.Ok(new { output = result ?? "Command timed out." }));
        }
    }

    public class CommandRequest
    {
        public string Command { get; set; }
    }
}
