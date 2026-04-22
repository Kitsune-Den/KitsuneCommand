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
            var combined = stdout + "\n" + stderr;

            if (combined.IndexOf("Waiting for user info...OK", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Logged in OK", StringComparison.OrdinalIgnoreCase) >= 0)
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
            if (combined.IndexOf("Login Failure", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("InvalidPassword", StringComparison.OrdinalIgnoreCase) >= 0
                || combined.IndexOf("Account Logon Denied", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Success = false;
                result.Message = "Login failed. Check username and password.";
                return result;
            }

            // Unknown - dump a short tail of the output for debugging
            var snippet = combined.Length > 400 ? combined.Substring(combined.Length - 400) : combined;
            result.Success = false;
            result.Message = "steamcmd returned unexpected output. Tail: " + snippet.Replace("\r", " ").Replace("\n", " | ");
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
