using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Linux server update + sticky-config feature. Writes settings to a shell-readable
    /// conf file that kitsune-pre-start.sh (installed via scripts/install-linux-updater.sh)
    /// sources on every systemd ExecStartPre.
    ///
    /// KC itself never runs steamcmd — that has to happen between systemd stop and start,
    /// not inside the game process.
    /// </summary>
    public class ServerUpdateFeature : FeatureBase<ServerUpdateSettings>
    {
        private readonly ISettingsRepository _settingsRepo;
        private const string SettingsKey = "ServerUpdate";

        // Paths resolved relative to the server install dir (two levels up from the mod's
        // own install path). Follows the same pattern as BackupService, ServerConfigService,
        // MapTileRenderer, etc. Using ModEntry.ModPath instead of Directory.GetCurrentDirectory()
        // avoids the CWD-shift issue where Unity changes the process working directory after
        // startup - relative reads from Web API threads would otherwise silently miss the files.
        private string _confPath;
        private string _serverConfigBakPath;

        public ServerUpdateFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ISettingsRepository settingsRepo)
            : base(eventBus, config)
        {
            _settingsRepo = settingsRepo;
        }

        protected override void OnEnable()
        {
            var serverDir = Path.GetFullPath(Path.Combine(ModEntry.ModPath, "..", ".."));
            _confPath = Path.Combine(serverDir, "kitsune-update.conf");
            _serverConfigBakPath = Path.Combine(serverDir, "serverconfig.xml.bak");

            LoadPersistedSettings();
            WriteConfFile();
            Log.Out($"[KitsuneCommand] ServerUpdate feature enabled. AutoUpdate={Settings.AutoUpdate}, Branch={Settings.Branch}, LogRetention={Settings.LogRetention}, configBak exists={File.Exists(_serverConfigBakPath)}.");
        }

        protected override void OnDisable()
        {
            // Leave the conf file on disk — feature disabled just means KC isn't managing
            // it anymore, not that we should nuke pre-start behavior.
        }

        public void UpdateSettings(ServerUpdateSettings newSettings)
        {
            Settings = newSettings;
            WriteConfFile();

            try
            {
                var json = JsonConvert.SerializeObject(newSettings);
                _settingsRepo.Set(SettingsKey, json);
                Log.Out($"[KitsuneCommand] ServerUpdate settings saved. AutoUpdate={newSettings.AutoUpdate}, Branch={newSettings.Branch}.");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to persist ServerUpdate settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Read the sticky-config file (serverconfig.xml.bak) contents for editing.
        /// Returns null if the file doesn't exist yet.
        /// </summary>
        public string GetServerConfigBak()
        {
            try
            {
                if (string.IsNullOrEmpty(_serverConfigBakPath))
                {
                    Log.Warning("[KitsuneCommand] ServerUpdate path not initialized yet - was OnEnable called?");
                    return null;
                }
                if (!File.Exists(_serverConfigBakPath))
                {
                    Log.Out($"[KitsuneCommand] serverconfig.xml.bak not found at '{_serverConfigBakPath}'.");
                    return null;
                }
                // File.ReadAllText without explicit encoding uses UTF-8 with BOM detection.
                return File.ReadAllText(_serverConfigBakPath);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to read {_serverConfigBakPath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Write the sticky-config file. This is what gets restored over serverconfig.xml
        /// on every server start (after steamcmd's validate clobbers the live copy).
        /// </summary>
        public bool SetServerConfigBak(string xml)
        {
            try
            {
                File.WriteAllText(_serverConfigBakPath, xml, new UTF8Encoding(false));
                Log.Out($"[KitsuneCommand] Wrote {_serverConfigBakPath} ({xml.Length} chars). Will be restored on next server start.");
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"[KitsuneCommand] Failed to write {_serverConfigBakPath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Run `steamcmd +login <user> +quit` as a subprocess with the given password and
        /// (optional) Steam Guard code piped to stdin. On success, steamcmd caches the session
        /// in ~/.steam, and future +login calls from the pre-start script use the cache
        /// without prompting.
        ///
        /// Password and guardCode are passed via stdin (not command-line args) so they don't
        /// show up in `ps aux`. They are NOT stored by KC - this method never persists them.
        /// Only steamcmd's own cache file holds anything long-lived after this call.
        /// </summary>
        public SteamAuthResult AuthenticateSteam(string password, string guardCode)
        {
            var result = new SteamAuthResult();
            var username = Settings?.SteamUsername;

            if (string.IsNullOrWhiteSpace(username))
            {
                result.Success = false;
                result.Message = "Steam username not set. Save a username in Server Update settings first.";
                return result;
            }

            // Tight allowlist on the username we pass to the shell. Steam usernames are
            // alphanumerics plus _ . - (no spaces). Reject anything else to avoid injection
            // even though we're not using a shell here.
            if (!Regex.IsMatch(username, @"^[a-zA-Z0-9._-]+$"))
            {
                result.Success = false;
                result.Message = "Invalid Steam username format.";
                return result;
            }

            if (password == null) password = "";

            string steamcmdPath = File.Exists("/usr/games/steamcmd") ? "/usr/games/steamcmd" : "steamcmd";

            var psi = new ProcessStartInfo
            {
                FileName = steamcmdPath,
                Arguments = $"+login {username} +quit",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var outputBuffer = new StringBuilder();
            var errorBuffer = new StringBuilder();

            try
            {
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuffer.AppendLine(e.Data); };
                    p.ErrorDataReceived  += (s, e) => { if (e.Data != null) errorBuffer.AppendLine(e.Data); };

                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    // Feed password, then Guard code if provided. steamcmd reads them as it hits
                    // the prompts. If a prompt never arrives (e.g. cached creds still valid), the
                    // extra input is harmless - stdin closes and steamcmd ignores it.
                    p.StandardInput.WriteLine(password);
                    if (!string.IsNullOrWhiteSpace(guardCode))
                        p.StandardInput.WriteLine(guardCode);
                    p.StandardInput.Close();

                    // 60s should be plenty - interactive login usually finishes in under 15s.
                    if (!p.WaitForExit(60000))
                    {
                        try { p.Kill(); } catch { }
                        result.Success = false;
                        result.Message = "steamcmd timed out (60s). Session may be stuck waiting for input.";
                        return result;
                    }
                }
            }
            catch (System.Exception ex)
            {
                result.Success = false;
                result.Message = $"Failed to run steamcmd: {ex.Message}";
                return result;
            }

            var stdout = outputBuffer.ToString();
            var stderr = errorBuffer.ToString();
            // steamcmd emits ANSI color codes (e.g. "\x1b[0m", "\x1b[1m") that can splice
            // into the middle of our marker strings - e.g. "Waiting for user info...\x1b[0mOK"
            // never matches a "Waiting for user info...OK" search. Strip them before matching.
            var combined = Regex.Replace(stdout + "\n" + stderr, @"\x1b\[[0-9;]*[a-zA-Z]", "");

            // Positive signals. Any of these indicates the login got far enough that
            // steamcmd successfully established a user session.
            bool loggedIn =
                combined.IndexOf("Waiting for user info", StringComparison.OrdinalIgnoreCase) >= 0
                && combined.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0
                && combined.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) < 0;

            // Additional success marker: mobile-confirmation flow completed.
            bool confirmedViaApp = combined.IndexOf("Waiting for confirmation...OK", StringComparison.OrdinalIgnoreCase) >= 0;

            // Direct "Logged in OK" - older/different steamcmd versions.
            bool explicitOk = combined.IndexOf("Logged in OK", StringComparison.OrdinalIgnoreCase) >= 0;

            if (loggedIn || confirmedViaApp || explicitOk)
            {
                result.Success = true;
                result.Message = "Logged in successfully. Credentials cached.";
                return result;
            }

            // Specific failure signals
            if (combined.IndexOf("Steam Guard", StringComparison.OrdinalIgnoreCase) >= 0
                && combined.IndexOf("OK", StringComparison.OrdinalIgnoreCase) < 0)
            {
                result.Success = false;
                result.Message = "Steam Guard code required or incorrect.";
                result.NeedsGuardCode = true;
                return result;
            }
            if (combined.IndexOf("Invalid Password", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("InvalidPassword", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Success = false;
                result.Message = "Invalid password. Double-check the password for your Steam account and try again. Note: Steam rate-limits after several wrong attempts - wait 30+ min if you get locked out.";
                return result;
            }
            if (combined.IndexOf("Login Failure", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Account Logon Denied", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("RateLimitExceeded", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Rate Limit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Success = false;
                result.Message = "Login failed. Wrong username, rate-limited by Steam (wait 30+ min), or account restriction. See server log for the raw steamcmd output.";
                return result;
            }

            // Unknown - log the raw output for post-mortem, but return a short user-friendly message.
            Log.Warning($"[KitsuneCommand] SteamAuth: unexpected steamcmd output. Raw:\n{combined}");
            result.Success = false;
            result.Message = "steamcmd returned an unexpected response. Check the server log for the raw output.";
            return result;
        }

        public class SteamAuthResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public bool NeedsGuardCode { get; set; }
        }

        private void WriteConfFile()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Managed by KitsuneCommand ServerUpdateFeature - do not edit directly.");
                sb.AppendLine("# Edit via the web UI; changes here are overwritten on next settings save.");
                sb.AppendLine($"AutoUpdate={(Settings.AutoUpdate ? "true" : "false")}");
                sb.AppendLine($"Branch={Settings.Branch ?? ""}");
                sb.AppendLine($"BranchPassword={Settings.BranchPassword ?? ""}");
                sb.AppendLine($"LogRetention={Settings.LogRetention}");
                sb.AppendLine($"SteamAppId={Settings.SteamAppId}");
                sb.AppendLine($"SteamUsername={Settings.SteamUsername ?? ""}");
                File.WriteAllText(_confPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to write {_confPath}: {ex.Message}");
            }
        }

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<ServerUpdateSettings>(json);
                    if (loaded != null)
                    {
                        Settings = loaded;
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load ServerUpdate settings, using defaults: {ex.Message}");
            }
        }
    }
}
