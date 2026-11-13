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
            if (PlatformHelper.IsWindows)
                return IsSupervisedOnWindows(CurrentWindowsSessionId());

            int selfPid;
            using (var self = Process.GetCurrentProcess()) selfPid = self.Id;

            return IsSupervisedOnLinux(
                Environment.GetEnvironmentVariable("INVOCATION_ID"),
                Environment.GetEnvironmentVariable("SYSTEMD_EXEC_PID"),
                selfPid,
                ReadParentPid);
        }

        /// <summary>
        /// Services run in session 0; a server started from a desktop, RDP or
        /// console session never does.
        /// </summary>
        public static bool IsSupervisedOnWindows(int sessionId)
        {
            return sessionId == 0;
        }

        /// <summary>
        /// systemd sets INVOCATION_ID for a unit's processes, but every
        /// descendant inherits it — including a game server that a control
        /// panel (itself a systemd service) spawned and detached. systemd
        /// restarts the panel in that case, not the game. SYSTEMD_EXEC_PID
        /// (systemd 248+) names the process systemd itself started, so the
        /// game is only supervised when that process is the game or one of
        /// its living ancestors (a wrapper such as startserver.sh).
        /// </summary>
        /// <param name="parentOf">Returns a PID's parent, or 0 if unknown.</param>
        public static bool IsSupervisedOnLinux(
            string systemdInvocationId, string systemdExecPid, int selfPid, Func<int, int> parentOf)
        {
            if (string.IsNullOrEmpty(systemdInvocationId)) return false;

            // Older systemd doesn't say which process it started. Assume the
            // unit is ours rather than risk racing systemd with a second copy.
            if (!int.TryParse(systemdExecPid, out var execPid) || execPid <= 1) return true;

            var pid = selfPid;
            for (int hops = 0; hops < 64 && pid > 1; hops++)
            {
                if (pid == execPid) return true;
                pid = parentOf(pid);
            }
            return false;
        }

        /// <summary>Parent PID from /proc/&lt;pid&gt;/stat, or 0 if it can't be read.</summary>
        private static int ReadParentPid(int pid)
        {
            try
            {
                // "pid (comm) state ppid ..." — comm can contain spaces and
                // parentheses, so parse from the last ')'.
                var stat = File.ReadAllText("/proc/" + pid + "/stat");
                var fields = stat.Substring(stat.LastIndexOf(')') + 1)
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return fields.Length > 1 && int.TryParse(fields[1], out var ppid) ? ppid : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Starts the helper. Call right before issuing the in-game shutdown.
        /// Returns false (with a reason) if the helper could not be started;
        /// the caller decides whether to shut down anyway.
        /// </summary>
        public static bool TryArm(out string detail)
        {
            string dir = null;
            try
            {
                var plan = BuildPlanForCurrentProcess();
                dir = HelperDirectory();
                var psi = PrepareHelper(dir, plan, PlatformHelper.IsWindows);

                using (var helper = Process.Start(psi))
                {
                    if (helper == null)
                    {
                        Disarm(dir);
                        detail = "the helper process did not start";
                        return false;
                    }
                }

                detail = $"helper armed for PID {plan.ProcessId}; log at {Path.Combine(dir, LogFileName)}";
                return true;
            }
            catch (Exception ex)
            {
                if (dir != null) Disarm(dir);
                detail = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Writes the helper script and the "armed" marker into
        /// <paramref name="dir"/> and returns how to start the helper. Split
        /// from <see cref="TryArm"/> so it can be tested without a game.
        /// </summary>
        public static ProcessStartInfo PrepareHelper(string dir, RelaunchPlan plan, bool isWindows)
        {
            Directory.CreateDirectory(dir);

            var armedFile = Path.Combine(dir, ArmedFileName);
            var logFile = Path.Combine(dir, LogFileName);
            TrimLog(logFile);
            File.WriteAllText(armedFile, plan.ProcessId.ToString());

            ProcessStartInfo psi;
            if (isWindows)
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
            return psi;
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
                Disarm(HelperDirectory());
            }
            catch (Exception ex)
            {
                global::Log.Warning($"[KitsuneCommand] Could not disarm the relaunch helper: {ex.Message}");
            }
        }

        /// <summary>Removes the "armed" marker the helper checks before relaunching.</summary>
        public static void Disarm(string dir)
        {
            try
            {
                var armedFile = Path.Combine(dir, ArmedFileName);
                if (File.Exists(armedFile)) File.Delete(armedFile);
            }
            catch (Exception ex)
            {
                global::Log.Warning($"[KitsuneCommand] Could not disarm the relaunch helper: {ex.Message}");
            }
        }

        private static string HelperDirectory()
        {
            return Path.Combine(ConfigManager.ResolveWorldAgnosticDataDir(), HelperDirName);
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
