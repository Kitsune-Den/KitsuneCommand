using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Web.Http;
using KitsuneCommand.Core;
using KitsuneCommand.Services;
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
        private readonly SteamRegistrationTracker _steamTracker;

        public ServerController(SteamRegistrationTracker steamTracker)
        {
            _steamTracker = steamTracker;
        }

        /// <summary>
        /// Get basic server information.
        /// </summary>
        [HttpGet]
        [Route("info")]
        public IHttpActionResult GetInfo()
        {
            var world = GameManager.Instance?.World;
            var gameManager = GameManager.Instance;
            var serverVisibility = GamePrefs.GetInt(EnumGamePrefs.ServerVisibility);

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
                kitsuneCommandVersion = Core.ModEntry.ModVersion,
                localIp = GetLocalIp(),
                publicIp = GetPublicIp(),
                // Reachability: derived from Steam/EOS master-server registration state.
                // Not an active port check - it's "Steam thinks we're browse-listable".
                serverVisibility,                              // 0=hidden, 1=friends, 2=public
                steamRegistered = _steamTracker?.IsRegistered ?? false,
                eosRegistered = _steamTracker?.IsEosRegistered ?? false,
                steamServerId = _steamTracker?.SteamServerId
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
                    os = GetOsLabel(),
                    processorCount = Environment.ProcessorCount,
                    is64Bit = Environment.Is64BitOperatingSystem,
                }
            };

            return Ok(ApiResponse.Ok(stats));
        }

        /// <summary>
        /// Return a coarse OS label (Windows / Linux / macOS / Other) rather than the
        /// full kernel string, so the stats endpoint doesn't leak patch-level CVE recon.
        /// </summary>
        private static string GetOsLabel()
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                case PlatformID.Win32S:
                case PlatformID.Win32Windows:
                case PlatformID.WinCE:
                    return "Windows";
                case PlatformID.Unix:
                    return "Linux";
                case PlatformID.MacOSX:
                    return "macOS";
                default:
                    return "Other";
            }
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

        /// <summary>
        /// Restart the server. OS-aware:
        ///   - Linux: try `sudo -n systemctl restart 7daystodie.service` first
        ///     (works if install-linux-updater.sh has been run to add the
        ///     sudoers entry). If that fails, fall back to in-game shutdown
        ///     and rely on systemd `Restart=always` to bounce the service.
        ///   - Windows: skip the systemctl probe entirely — `systemctl`
        ///     doesn't exist on Windows, so probing it just wastes ~5s and
        ///     produces a misleading "systemctl failed" warning. Go straight
        ///     to in-game shutdown; NSSM (the standard Windows service
        ///     supervisor for 7DTD) ships with `AppExit Restart` as its
        ///     default and auto-bounces the service when the game exits.
        ///
        /// The strategy decision is delegated to <see cref="OsRestartStrategy"/>
        /// so it stays testable and shareable with any other restart entry
        /// point (e.g. krestart) we add later.
        /// </summary>
        [HttpPost]
        [Route("restart")]
        [RoleAuthorize("admin")]
        public IHttpActionResult Restart([FromBody] RestartRequest request)
        {
            var serviceName = request?.ServiceName ?? "7daystodie.service";
            // sanitize - only allow alphanumerics, dashes, dots, underscores
            if (!System.Text.RegularExpressions.Regex.IsMatch(serviceName, @"^[a-zA-Z0-9._-]+$"))
                return BadRequest("Invalid service name.");

            var strategy = OsRestartStrategy.DecideForCurrentHost();

            if (strategy == OsRestartStrategy.Kind.SystemctlThenInGameShutdown)
            {
                // Try systemctl first (Linux path).
                if (TryStart("sudo", $"-n systemctl restart {serviceName}", out var stderr, 5000))
                {
                    return Ok(ApiResponse.Ok($"Restart triggered via systemctl ({serviceName}). Server bouncing."));
                }

                global::Log.Warning($"[KitsuneCommand] systemctl restart failed or not available ({stderr}). Falling back to in-game shutdown.");
            }
            else
            {
                // Windows: NSSM (AppExit Restart) handles the bounce after the
                // game process exits. INFO log so the operator sees we *chose*
                // this path rather than failed into it.
                global::Log.Out("[KitsuneCommand] Windows install detected; using in-game shutdown + service-supervisor restart (NSSM AppExit Restart).");
            }

            // Common path: in-game shutdown with short delay. On Linux this is
            // the fallback after systemctl failed; on Windows it's the only
            // path we take.
            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    SdtdConsole.Instance.ExecuteSync("shutdown 5", null);
                }
                catch (Exception ex)
                {
                    global::Log.Error($"[KitsuneCommand] In-game shutdown command failed: {ex.Message}");
                }
            }, null);

            if (strategy == OsRestartStrategy.Kind.InGameShutdownOnly)
            {
                return Ok(ApiResponse.Ok("Restart requested via in-game shutdown (5s delay). The Windows service supervisor (NSSM) will auto-bounce 7DTD when the game exits, provided AppExit Restart is configured (the NSSM default)."));
            }

            return Ok(ApiResponse.Ok("Restart requested via in-game shutdown (5s delay). If systemd Restart=always is not set, server will stay down - run scripts/linux-updater/install-linux-updater.sh to configure."));
        }

        private static bool TryStart(string fileName, string arguments, out string stderr, int timeoutMs)
        {
            stderr = null;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        stderr = "timed out";
                        return false;
                    }
                    stderr = p.StandardError.ReadToEnd()?.Trim();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return false;
            }
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

    public class RestartRequest
    {
        /// <summary>Optional override for the systemd service name. Defaults to "7daystodie.service".</summary>
        public string ServiceName { get; set; }
    }
}
