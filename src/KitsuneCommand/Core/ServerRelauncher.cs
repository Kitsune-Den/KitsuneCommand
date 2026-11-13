using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using KitsuneCommand.Configuration;

namespace KitsuneCommand.Core
{
    /// <summary>
    /// Brings the server back after a restart when nothing else will.
    ///
    /// KitsuneCommand runs inside the game process, so it can't start the
    /// game again after shutting it down. With a service supervisor (systemd,
    /// NSSM) that's fine — the supervisor does it. Without one (the server was
    /// started from startdedicated.bat / startserver.sh), this class leaves a
    /// small helper script running that outlives the game, waits for it to
    /// exit, and launches it again with the same command line.
    /// </summary>
    public static class ServerRelauncher
    {
        private const string HelperDirName = "relaunch";
        private const string ArmedFileName = "relaunch.armed";
        private const string LogFileName = "relaunch.log";

        /// <summary>
        /// True when a service manager owns this process and is expected to
        /// restart it. The helper must not run in that case: the manager
        /// would kill it along with the service, or both would start a copy.
        /// </summary>
        public static bool IsSupervised()
        {
            return IsSupervised(
                PlatformHelper.IsWindows,
                Environment.GetEnvironmentVariable("INVOCATION_ID"),
                PlatformHelper.IsWindows ? CurrentWindowsSessionId() : -1);
        }

        /// <summary>
        /// Pure form of <see cref="IsSupervised()"/>.
        ///   - Linux: systemd sets INVOCATION_ID for every process of a unit.
        ///   - Windows: services run in session 0; a server started from a
        ///     desktop, RDP or console session never does.
        /// </summary>
        public static bool IsSupervised(bool isWindows, string systemdInvocationId, int windowsSessionId)
        {
            return isWindows
                ? windowsSessionId == 0
                : !string.IsNullOrEmpty(systemdInvocationId);
        }

        /// <summary>
        /// Starts the helper. Call right before issuing the in-game shutdown.
        /// Returns false (with a reason) if the helper could not be started;
        /// the caller decides whether to shut down anyway.
        /// </summary>
        public static bool TryArm(out string detail)
        {
            try
            {
                var plan = BuildPlanForCurrentProcess();
                var dir = Path.Combine(ConfigManager.ResolveWorldAgnosticDataDir(), HelperDirName);
                Directory.CreateDirectory(dir);

                var armedFile = Path.Combine(dir, ArmedFileName);
                var logFile = Path.Combine(dir, LogFileName);
                TrimLog(logFile);
                File.WriteAllText(armedFile, plan.ProcessId.ToString());

                ProcessStartInfo psi;
                if (PlatformHelper.IsWindows)
                {
                    var script = Path.Combine(dir, "relaunch-server.ps1");
                    // BOM so Windows PowerShell reads non-ASCII paths correctly.
                    File.WriteAllText(script, plan.RenderWindowsScript(armedFile, logFile), new UTF8Encoding(true));
                    psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + RelaunchPlan.QuoteWindowsArg(script),
                    };
                }
                else
                {
                    var script = Path.Combine(dir, "relaunch-server.sh");
                    File.WriteAllText(script, plan.RenderLinuxScript(armedFile, logFile), new UTF8Encoding(false));
                    psi = new ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = RelaunchPlan.QuoteShell(script),
                    };
                }

                // No redirected pipes: they would break when the game exits
                // and take the helper down with them.
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = dir;

                using (var helper = Process.Start(psi))
                {
                    if (helper == null)
                    {
                        Disarm();
                        detail = "the helper process did not start";
                        return false;
                    }
                }

                detail = $"helper armed for PID {plan.ProcessId}; log at {logFile}";
                return true;
            }
            catch (Exception ex)
            {
                Disarm();
                detail = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Cancels a pending relaunch. Used by the plain Shutdown button so a
        /// helper left over from an earlier restart attempt doesn't bring the
        /// server back after the admin asked for it to stay down.
        /// </summary>
        public static void Disarm()
        {
            try
            {
                var armedFile = Path.Combine(ConfigManager.ResolveWorldAgnosticDataDir(), HelperDirName, ArmedFileName);
                if (File.Exists(armedFile)) File.Delete(armedFile);
            }
            catch (Exception ex)
            {
                global::Log.Warning($"[KitsuneCommand] Could not disarm the relaunch helper: {ex.Message}");
            }
        }

        private static RelaunchPlan BuildPlanForCurrentProcess()
        {
            var commandLine = Environment.GetCommandLineArgs();
            using (var self = Process.GetCurrentProcess())
            {
                return RelaunchPlan.FromCommandLine(
                    self.Id,
                    ResolveExecutablePath(self, commandLine),
                    Environment.CurrentDirectory,
                    commandLine,
                    DateTime.Now);
            }
        }

        private static string ResolveExecutablePath(Process self, string[] commandLine)
        {
            try
            {
                var path = self.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
            catch
            {
                // Mono can refuse MainModule on some hosts; fall through to argv[0].
            }

            if (commandLine != null && commandLine.Length > 0 && !string.IsNullOrEmpty(commandLine[0]))
            {
                var candidate = Path.GetFullPath(commandLine[0]);
                if (File.Exists(candidate)) return candidate;
                if (PlatformHelper.IsWindows && File.Exists(candidate + ".exe")) return candidate + ".exe";
            }

            throw new InvalidOperationException("could not work out the server executable path");
        }

        /// <summary>Keeps the helper log from growing forever (one line set per restart).</summary>
        private static void TrimLog(string logFile)
        {
            try
            {
                var info = new FileInfo(logFile);
                if (info.Exists && info.Length > 256 * 1024) info.Delete();
            }
            catch
            {
                // Best effort.
            }
        }

        private static int CurrentWindowsSessionId()
        {
            try
            {
                return ProcessIdToSessionId(GetCurrentProcessId(), out var sessionId) ? (int)sessionId : -1;
            }
            catch
            {
                return -1;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    }
}
