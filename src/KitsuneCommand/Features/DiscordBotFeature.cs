using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Services;
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Discord bot integration: bidirectional chat bridge, event notifications, and slash commands.
    /// </summary>
    public class DiscordBotFeature : FeatureBase<DiscordBotSettings>
    {
        private readonly ISettingsRepository _settingsRepo;
        private readonly DiscordBotService _botService;
        private readonly LivePlayerManager _playerManager;

        private const string DiscordPrefix = "[Discord]";
        private const string SettingsKey = "DiscordBot";

        public DiscordBotFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ISettingsRepository settingsRepo,
            DiscordBotService botService,
            LivePlayerManager playerManager)
            : base(eventBus, config)
        {
            _settingsRepo = settingsRepo;
            _botService = botService;
            _playerManager = playerManager;
        }

        protected override void OnEnable()
        {
            LoadPersistedSettings();

            // Subscribe to game events
            EventBus.Subscribe<ChatMessageEvent>(OnChatMessage);
            EventBus.Subscribe<PlayerLoginEvent>(OnPlayerLogin);
            EventBus.Subscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected);
            EventBus.Subscribe<GameStartDoneEvent>(OnGameStartDone);
            EventBus.Subscribe<GameShutdownEvent>(OnGameShutdown);
            EventBus.Subscribe<SkyChangedEvent>(OnSkyChanged);

            // Wire up Discord -> game chat bridge
            _botService.OnChatBridgeMessage += OnDiscordChatMessage;
            _botService.SlashCommandReceived += OnSlashCommand;

            // Start the bot if configured
            if (Settings.Enabled && !string.IsNullOrWhiteSpace(Settings.BotToken))
            {
                _ = _botService.StartAsync(Settings);
            }

            Log.Out($"[KitsuneCommand] Discord bot feature enabled. Enabled={Settings.Enabled}");
        }

        protected override void OnDisable()
        {
            EventBus.Unsubscribe<ChatMessageEvent>(OnChatMessage);
            EventBus.Unsubscribe<PlayerLoginEvent>(OnPlayerLogin);
            EventBus.Unsubscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected);
            EventBus.Unsubscribe<GameStartDoneEvent>(OnGameStartDone);
            EventBus.Unsubscribe<GameShutdownEvent>(OnGameShutdown);
            EventBus.Unsubscribe<SkyChangedEvent>(OnSkyChanged);

            _botService.OnChatBridgeMessage -= OnDiscordChatMessage;
            _botService.SlashCommandReceived -= OnSlashCommand;

            _ = _botService.StopAsync();
        }

        // ---- Chat Bridge: Game -> Discord ----

        private void OnChatMessage(ChatMessageEvent e)
        {
            if (!Settings.Enabled || !Settings.ChatBridgeEnabled) return;
            if (Settings.ChatBridgeChannelId == 0) return;

            // Loop prevention: skip messages injected from Discord
            if (e.SenderName != null && e.SenderName.StartsWith(DiscordPrefix)) return;

            // Only bridge global chat
            if (e.ChatType != ChatType.Global) return;

            var text = $"**{EscapeMarkdown(e.SenderName)}**: {EscapeMarkdown(e.Message)}";
            _ = _botService.SendMessageAsync(Settings.ChatBridgeChannelId, text);
        }

        // ---- Chat Bridge: Discord -> Game ----

        private void OnDiscordChatMessage(string username, string message)
        {
            if (!Settings.Enabled || !Settings.ChatBridgeEnabled) return;
            if (string.IsNullOrWhiteSpace(message)) return;

            // Sanitize for console command injection
            var safeName = SanitizeForConsole(username);
            var safeMsg = SanitizeForConsole(message);

            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    SdtdConsole.Instance.ExecuteSync(
                        $"say \"{DiscordPrefix} {safeName}: {safeMsg}\"", null);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KitsuneCommand] Discord->game chat failed: {ex.Message}");
                }
            }, null);
        }

        // ---- Event Notifications ----

        private void OnPlayerLogin(PlayerLoginEvent e)
        {
            if (!Settings.Enabled || !Settings.EventNotificationsEnabled || !Settings.NotifyPlayerJoin) return;
            if (Settings.EventChannelId == 0) return;

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("Player Joined")
                .WithDescription($"**{EscapeMarkdown(e.PlayerName)}** connected")
                .WithFooter(Settings.ServerName)
                .WithCurrentTimestamp();

            _ = _botService.SendEmbedAsync(Settings.EventChannelId, embed);
            UpdatePlayerCountStatus();
        }

        private void OnPlayerDisconnected(PlayerDisconnectedEvent e)
        {
            if (!Settings.Enabled || !Settings.EventNotificationsEnabled || !Settings.NotifyPlayerLeave) return;
            if (Settings.EventChannelId == 0) return;

            var embed = new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("Player Left")
                .WithDescription($"**{EscapeMarkdown(e.PlayerName)}** disconnected")
                .WithFooter(Settings.ServerName)
                .WithCurrentTimestamp();

            _ = _botService.SendEmbedAsync(Settings.EventChannelId, embed);
            UpdatePlayerCountStatus();
        }

        private void OnGameStartDone(GameStartDoneEvent e)
        {
            if (!Settings.Enabled || !Settings.EventNotificationsEnabled || !Settings.NotifyServerStart) return;
            if (Settings.EventChannelId == 0) return;

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("Server Online")
                .WithDescription($"**{EscapeMarkdown(Settings.ServerName)}** is now running")
                .WithCurrentTimestamp();

            _ = _botService.SendEmbedAsync(Settings.EventChannelId, embed);
        }

        private void OnGameShutdown(GameShutdownEvent e)
        {
            if (!Settings.Enabled || !Settings.EventNotificationsEnabled || !Settings.NotifyServerStop) return;
            if (Settings.EventChannelId == 0) return;

            var embed = new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("Server Offline")
                .WithDescription($"**{EscapeMarkdown(Settings.ServerName)}** is shutting down")
                .WithCurrentTimestamp();

            _ = _botService.SendEmbedAsync(Settings.EventChannelId, embed);
        }

        private bool _lastBloodMoonState = false;

        private void OnSkyChanged(SkyChangedEvent e)
        {
            if (!Settings.Enabled || !Settings.EventNotificationsEnabled || !Settings.NotifyBloodMoon) return;
            if (Settings.EventChannelId == 0) return;

            // Only notify on blood moon start, not every sky tick
            if (e.IsBloodMoon && !_lastBloodMoonState)
            {
                var embed = new EmbedBuilder()
                    .WithColor(new Color(0xFF, 0x44, 0x00)) // orange-red
                    .WithTitle("\ud83c\udf11 Blood Moon Rising!")
                    .WithDescription($"Day {e.Day} — The horde is coming...")
                    .WithFooter(Settings.ServerName)
                    .WithCurrentTimestamp();

                _ = _botService.SendEmbedAsync(Settings.EventChannelId, embed);
            }

            _lastBloodMoonState = e.IsBloodMoon;
        }

        // ---- Slash Commands ----

        private Task OnSlashCommand(SocketSlashCommand command)
        {
            if (!Settings.Enabled || !Settings.SlashCommandsEnabled)
                return Task.CompletedTask;

            switch (command.Data.Name)
            {
                case "status":
                    return HandleStatusCommand(command);
                case "players":
                    return HandlePlayersCommand(command);
                case "time":
                    return HandleTimeCommand(command);
                default:
                    return Task.CompletedTask;
            }
        }

        private async Task HandleStatusCommand(SocketSlashCommand command)
        {
            try
            {
                var onlineCount = _playerManager.OnlineCount;
                var embed = new EmbedBuilder()
                    .WithColor(Color.Blue)
                    .WithTitle(Settings.ServerName)
                    .AddField("Status", _botService.IsConnected ? "Online" : "Unknown", true)
                    .AddField("Players", onlineCount.ToString(), true)
                    .WithCurrentTimestamp();

                await command.RespondAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] /status command failed: {ex.Message}");
            }
        }

        private async Task HandlePlayersCommand(SocketSlashCommand command)
        {
            try
            {
                var players = _playerManager.GetAllOnline().ToList();
                if (players.Count == 0)
                {
                    await command.RespondAsync("No players online.");
                    return;
                }

                var list = string.Join("\n", players.Select(p => $"\u2022 {p.PlayerName}"));
                if (list.Length > 1900) list = list.Substring(0, 1900) + "\n...";

                var embed = new EmbedBuilder()
                    .WithColor(Color.Blue)
                    .WithTitle($"Online Players ({players.Count})")
                    .WithDescription(list)
                    .WithFooter(Settings.ServerName)
                    .WithCurrentTimestamp();

                await command.RespondAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] /players command failed: {ex.Message}");
            }
        }

        private async Task HandleTimeCommand(SocketSlashCommand command)
        {
            try
            {
                var embed = new EmbedBuilder()
                    .WithColor(Color.Blue)
                    .WithTitle("In-Game Time")
                    .AddField("Info", "Use /status for server details", false)
                    .WithFooter(Settings.ServerName)
                    .WithCurrentTimestamp();

                await command.RespondAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] /time command failed: {ex.Message}");
            }
        }

        // ---- Settings ----

        public void UpdateSettings(DiscordBotSettings newSettings)
        {
            var oldToken = Settings.BotToken;
            var oldEnabled = Settings.Enabled;

            Settings = newSettings;
            var json = JsonConvert.SerializeObject(newSettings);
            _settingsRepo.Set(SettingsKey, json);

            // Restart bot if token changed or enabled state toggled
            if (oldToken != newSettings.BotToken || oldEnabled != newSettings.Enabled)
            {
                _ = RestartBotAsync();
            }

            // Update status display
            if (newSettings.ShowPlayerCountInStatus)
            {
                UpdatePlayerCountStatus();
            }
        }

        private async Task RestartBotAsync()
        {
            await _botService.StopAsync();

            if (Settings.Enabled && !string.IsNullOrWhiteSpace(Settings.BotToken))
            {
                await _botService.StartAsync(Settings);
            }
        }

        private void UpdatePlayerCountStatus()
        {
            if (!Settings.Enabled || !Settings.ShowPlayerCountInStatus) return;

            var count = _playerManager.OnlineCount;
            _ = _botService.SetStatusAsync($"{count} player{(count != 1 ? "s" : "")} online");
        }

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<DiscordBotSettings>(json);
                    if (loaded != null) Settings = loaded;
                }
            }
            catch { /* use defaults */ }
        }

        // ---- Helpers ----

        private static string EscapeMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("\\", "\\\\")
                .Replace("*", "\\*")
                .Replace("_", "\\_")
                .Replace("~", "\\~")
                .Replace("`", "\\`")
                .Replace("|", "\\|");
        }

        private static string SanitizeForConsole(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            // Remove quotes and limit length to prevent abuse
            var sanitized = text.Replace("\"", "'").Replace("\\", "");
            return sanitized.Length > 200 ? sanitized.Substring(0, 200) : sanitized;
        }
    }
}
