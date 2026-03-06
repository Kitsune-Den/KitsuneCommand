using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// Server management endpoints: info, stats, command execution, save, shutdown.
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
                kitsuneCommandVersion = "2.0.0",
                localIp = GetLocalIp(),
                publicIp = GetPublicIp()
            };

            return Ok(ApiResponse.Ok(info));
        }

        /// <summary>
        /// Get server performance stats including system information.
        /// </summary>
        [HttpGet]
        [Route("stats")]
        public IHttpActionResult GetStats()
        {
            var gameManager = GameManager.Instance;
            var process = Process.GetCurrentProcess();

            var stats = new
            {
                fps = gameManager?.fps?.Counter ?? 0f,
                entityCount = gameManager?.World?.Entities?.Count ?? 0,
                playerCount = gameManager?.World?.Players?.Count ?? 0,
                uptime = UnityEngine.Time.time,
                gcMemory = GC.GetTotalMemory(false) / (1024 * 1024), // MB
                workingSetMemory = process.WorkingSet64 / (1024 * 1024), // MB
                peakWorkingSetMemory = process.PeakWorkingSet64 / (1024 * 1024), // MB
                threadCount = process.Threads.Count,
                system = new
                {
                    os = Environment.OSVersion.ToString(),
                    processorCount = Environment.ProcessorCount,
                    machineName = Environment.MachineName,
                    is64Bit = Environment.Is64BitOperatingSystem,
                }
            };

            return Ok(ApiResponse.Ok(stats));
        }

        /// <summary>
        /// Execute a server console command.
        /// </summary>
        [HttpPost]
        [Route("command")]
        [RoleAuthorize("admin")]
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

        /// <summary>
        /// Trigger a world save.
        /// </summary>
        [HttpPost]
        [Route("save")]
        [RoleAuthorize("admin")]
        public IHttpActionResult SaveWorld()
        {
            string result = null;
            var waitHandle = new ManualResetEventSlim(false);

            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    var output = SdtdConsole.Instance.ExecuteSync("saveworld", null);
                    result = output != null ? string.Join("\n", output) : "Save command executed.";
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

            waitHandle.Wait(TimeSpan.FromSeconds(30));

            return Ok(ApiResponse.Ok(new { message = result ?? "Save command timed out." }));
        }

        /// <summary>
        /// Initiate a graceful server shutdown with optional delay.
        /// </summary>
        [HttpPost]
        [Route("shutdown")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Shutdown([FromBody] ShutdownRequest request)
        {
            var delay = request?.DelaySeconds ?? 10;
            if (delay < 0) delay = 0;
            if (delay > 300) delay = 300; // Max 5 minute delay

            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    SdtdConsole.Instance.ExecuteSync($"shutdown {delay}", null);
                }
                catch (Exception ex)
                {
                    global::Log.Error($"[KitsuneCommand] Shutdown command failed: {ex.Message}");
                }
            }, null);

            return Ok(ApiResponse.Ok($"Server shutdown initiated with {delay} second delay."));
        }

        private static string GetLocalIp()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                return ip?.ToString() ?? "N/A";
            }
            catch
            {
                return "N/A";
            }
        }

        private static string _cachedPublicIp;
        private static DateTime _publicIpCacheTime = DateTime.MinValue;

        private static string GetPublicIp()
        {
            if (_cachedPublicIp != null && (DateTime.UtcNow - _publicIpCacheTime).TotalMinutes < 10)
                return _cachedPublicIp;

            try
            {
                using (var client = new WebClient())
                {
                    _cachedPublicIp = client.DownloadString("https://api.ipify.org").Trim();
                    _publicIpCacheTime = DateTime.UtcNow;
                    return _cachedPublicIp;
                }
            }
            catch
            {
                return _cachedPublicIp ?? "N/A";
            }
        }
    }

    public class CommandRequest
    {
        public string Command { get; set; }
    }

    public class ShutdownRequest
    {
        public int DelaySeconds { get; set; } = 10;
    }
}
