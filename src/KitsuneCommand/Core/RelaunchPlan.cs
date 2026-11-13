using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KitsuneCommand.Core
{
    /// <summary>
    /// Everything the relaunch helper script needs to start the server again
    /// the way it was started this time: same executable, same working
    /// directory, same arguments (with a fresh log file name).
    ///
    /// Pure data + pure script rendering, so it can be unit-tested without a
    /// running game. <see cref="ServerRelauncher"/> does the I/O.
    /// </summary>
    public sealed class RelaunchPlan
    {
        /// <summary>How long the helper waits for the old process to exit.</summary>
        public const int ExitTimeoutSeconds = 300;

        /// <summary>
        /// Pause between the old process exiting and the relaunch. Gives a
        /// supervisor we failed to detect (a looping launcher script, a
        /// container entrypoint) time to bring the server back first, so the
        /// helper can see it and stand down instead of starting a second copy.
        /// </summary>
        public const int GraceSeconds = 10;

        private static readonly Regex LogTimestamp =
            new Regex(@"\d{4}-\d{2}-\d{2}__\d{2}-\d{2}-\d{2}", RegexOptions.Compiled);

        public int ProcessId { get; }
        public string ExecutablePath { get; }
        public string WorkingDirectory { get; }
        public IReadOnlyList<string> Arguments { get; }

        /// <summary>
        /// The argument that tells two servers sharing one install apart
        /// (<c>-configfile=...</c>), or empty when there isn't one.
        /// </summary>
        public string InstanceMarker { get; }

        public RelaunchPlan(int processId, string executablePath, string workingDirectory, IEnumerable<string> arguments)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("Executable path is required.", nameof(executablePath));
            if (string.IsNullOrWhiteSpace(workingDirectory))
                throw new ArgumentException("Working directory is required.", nameof(workingDirectory));

            ProcessId = processId;
            ExecutablePath = executablePath;
            WorkingDirectory = workingDirectory;
            Arguments = (arguments ?? Enumerable.Empty<string>()).ToList();
            InstanceMarker = Arguments.FirstOrDefault(a =>
                a.StartsWith("-configfile=", StringComparison.OrdinalIgnoreCase)) ?? "";
        }

        /// <summary>
        /// Builds a plan from a raw command line (<c>argv[0]</c> is the program
        /// and is dropped). The stock launch scripts stamp the start time into
        /// the <c>-logfile</c> name; that stamp is refreshed so the relaunched
        /// server gets its own log instead of overwriting the previous one.
        /// </summary>
        public static RelaunchPlan FromCommandLine(
            int processId, string executablePath, string workingDirectory, string[] commandLine, DateTime now)
        {
            var args = (commandLine ?? new string[0]).Skip(1).ToList();
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (string.Equals(args[i], "-logfile", StringComparison.OrdinalIgnoreCase))
                {
                    args[i + 1] = RefreshLogTimestamp(args[i + 1], now);
                    break;
                }
            }
            return new RelaunchPlan(processId, executablePath, workingDirectory, args);
        }

        public static string RefreshLogTimestamp(string logPath, DateTime now)
        {
            if (string.IsNullOrEmpty(logPath)) return logPath;
            var stamp = now.ToString("yyyy-MM-dd__HH-mm-ss", CultureInfo.InvariantCulture);
            return LogTimestamp.Replace(logPath, stamp, 1);
        }

        // ─── Windows (PowerShell) ─────────────────────────────────────────

        public string RenderWindowsScript(string armedFile, string logFile)
        {
            var argLine = string.Join(" ", Arguments.Select(QuoteWindowsArg));
            var sb = new StringBuilder();
            sb.AppendLine("# Written by KitsuneCommand. Waits for the 7DTD server to exit, then starts it again.");
            sb.AppendLine("$oldPid = " + ProcessId.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("$exe = " + QuotePowerShell(ExecutablePath));
            sb.AppendLine("$workDir = " + QuotePowerShell(WorkingDirectory));
            sb.AppendLine("$argLine = " + QuotePowerShell(argLine));
            sb.AppendLine("$marker = " + QuotePowerShell(InstanceMarker));
            sb.AppendLine("$armed = " + QuotePowerShell(armedFile));
            sb.AppendLine("$log = " + QuotePowerShell(logFile));
            sb.AppendLine("$exitTimeout = " + ExitTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("$grace = " + GraceSeconds.ToString(CultureInfo.InvariantCulture));
            sb.Append(WindowsBody);
            return sb.ToString();
        }

        private const string WindowsBody = @"
function Write-Log($message) {
    try { Add-Content -Path $log -Value ('{0} {1}' -f (Get-Date -Format s), $message) } catch { }
}

try {
    $old = Get-Process -Id $oldPid -ErrorAction SilentlyContinue
    if (-not $old) { Write-Log ""PID $oldPid is not running; nothing to wait for.""; exit 1 }
    $started = $old.StartTime
    Write-Log ""Armed. Waiting for PID $oldPid to exit.""

    $deadline = (Get-Date).AddSeconds($exitTimeout)
    while ($true) {
        $p = Get-Process -Id $oldPid -ErrorAction SilentlyContinue
        # A different start time means the PID was reused by something else.
        if (-not $p -or $p.StartTime -ne $started) { break }
        if ((Get-Date) -gt $deadline) { Write-Log 'Server did not exit in time; giving up.'; exit 1 }
        Start-Sleep -Seconds 1
    }

    Start-Sleep -Seconds $grace

    if (-not (Test-Path -LiteralPath $armed)) { Write-Log 'Disarmed; not relaunching.'; exit 0 }

    $running = Get-CimInstance Win32_Process | Where-Object {
        $_.ProcessId -ne $oldPid -and $_.ExecutablePath -eq $exe -and
        ($marker -eq '' -or ($_.CommandLine -and $_.CommandLine.Contains($marker)))
    }
    if ($running) {
        Write-Log 'Server is already running again (something else restarted it); standing down.'
        Remove-Item -LiteralPath $armed -ErrorAction SilentlyContinue
        exit 0
    }

    Remove-Item -LiteralPath $armed -ErrorAction SilentlyContinue
    if ($argLine -eq '') {
        $new = Start-Process -FilePath $exe -WorkingDirectory $workDir -PassThru
    } else {
        $new = Start-Process -FilePath $exe -ArgumentList $argLine -WorkingDirectory $workDir -PassThru
    }
    Write-Log ""Relaunched as PID $($new.Id).""
} catch {
    Write-Log ""Relaunch failed: $_""
    exit 1
}
";

        /// <summary>PowerShell single-quoted literal.</summary>
        public static string QuotePowerShell(string value)
        {
            return "'" + (value ?? "").Replace("'", "''") + "'";
        }

        /// <summary>
        /// Quotes one argument the way the Windows C runtime parses a command
        /// line (backslashes before a quote are doubled, quotes are escaped).
        /// </summary>
        public static string QuoteWindowsArg(string arg)
        {
            if (arg == null) arg = "";
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;

            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(c);
                }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        // ─── Linux (POSIX sh) ─────────────────────────────────────────────

        public string RenderLinuxScript(string armedFile, string logFile)
        {
            var sb = new StringBuilder();
            sb.Append("#!/bin/sh\n");
            sb.Append("# Written by KitsuneCommand. Waits for the 7DTD server to exit, then starts it again.\n");
            sb.Append("OLD_PID=" + ProcessId.ToString(CultureInfo.InvariantCulture) + "\n");
            sb.Append("EXE=" + QuoteShell(ExecutablePath) + "\n");
            sb.Append("WORKDIR=" + QuoteShell(WorkingDirectory) + "\n");
            sb.Append("MARKER=" + QuoteShell(InstanceMarker) + "\n");
            sb.Append("ARMED=" + QuoteShell(armedFile) + "\n");
            sb.Append("LOG=" + QuoteShell(logFile) + "\n");
            sb.Append("EXIT_TIMEOUT=" + ExitTimeoutSeconds.ToString(CultureInfo.InvariantCulture) + "\n");
            sb.Append("GRACE=" + GraceSeconds.ToString(CultureInfo.InvariantCulture) + "\n");
            sb.Append(LinuxBody.Replace("\r\n", "\n"));
            sb.Append("nohup \"$EXE\"");
            foreach (var arg in Arguments) sb.Append(' ').Append(QuoteShell(arg));
            sb.Append(" </dev/null >/dev/null 2>&1 &\n");
            sb.Append("log \"Relaunched as PID $!.\"\n");
            return sb.ToString();
        }

        private const string LinuxBody = @"
# Survive the terminal or SSH session that started the server going away.
trap '' HUP
exec </dev/null >/dev/null 2>&1

log() { echo ""$(date '+%Y-%m-%dT%H:%M:%S') $*"" >> ""$LOG""; }

# Use the kernel's view of the running binary; it doubles as the liveness
# check (it stops resolving once the process is gone, and changes if the PID
# is reused by something else).
REAL_EXE=$(readlink ""/proc/$OLD_PID/exe"" 2>/dev/null)
if [ -z ""$REAL_EXE"" ]; then
    log ""PID $OLD_PID is not running; nothing to wait for.""
    exit 1
fi
case ""$REAL_EXE"" in
    *' (deleted)') ;;
    *) EXE=$REAL_EXE ;;
esac
log ""Armed. Waiting for PID $OLD_PID to exit.""

waited=0
while [ ""$(readlink ""/proc/$OLD_PID/exe"" 2>/dev/null)"" = ""$REAL_EXE"" ]; do
    if [ ""$waited"" -ge ""$EXIT_TIMEOUT"" ]; then
        log 'Server did not exit in time; giving up.'
        exit 1
    fi
    sleep 1
    waited=$((waited + 1))
done

sleep ""$GRACE""

if [ ! -f ""$ARMED"" ]; then
    log 'Disarmed; not relaunching.'
    exit 0
fi

for proc in /proc/[0-9]*; do
    pid=${proc#/proc/}
    [ ""$pid"" = ""$OLD_PID"" ] && continue
    [ ""$(readlink ""$proc/exe"" 2>/dev/null)"" = ""$EXE"" ] || continue
    if [ -z ""$MARKER"" ] || tr '\0' '\n' < ""$proc/cmdline"" 2>/dev/null | grep -qxF -e ""$MARKER""; then
        log ""Server is already running again as PID $pid (something else restarted it); standing down.""
        rm -f ""$ARMED""
        exit 0
    fi
done

rm -f ""$ARMED""
cd ""$WORKDIR"" || { log ""Cannot cd to $WORKDIR; not relaunching.""; exit 1; }
";

        /// <summary>POSIX shell single-quoted literal.</summary>
        public static string QuoteShell(string value)
        {
            return "'" + (value ?? "").Replace("'", "'\\''") + "'";
        }
    }
}
