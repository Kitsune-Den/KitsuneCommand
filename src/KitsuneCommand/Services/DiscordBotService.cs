using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using KitsuneCommand.Features;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Manages the Discord bot gateway connection lifecycle.
    /// Thread-safe — all methods can be called from any thread.
    /// </summary>
    public class DiscordBotService
    {
        private DiscordSocketClient _client;
        private DiscordBotSettings _settings;
        private readonly object _lock = new object();
        private bool _running;

        /// <summary>Fires when a user sends a message in the chat bridge channel.</summary>
        public event Action<string, string> OnChatBridgeMessage; // (username, message)

        public bool IsConnected => _client?.ConnectionState == ConnectionState.Connected;
        public string BotUsername => _client?.CurrentUser?.ToString();
        public int Latency => _client?.Latency ?? -1;

        public async Task StartAsync(DiscordBotSettings settings)
        {
            lock (_lock)
            {
                if (_running) return;
                _running = true;
            }

            _settings = settings;

            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
                               | GatewayIntents.GuildMessages
                               | GatewayIntents.MessageContent,
                LogLevel = LogSeverity.Warning,
                ConnectionTimeout = 15000
            };

            _client = new DiscordSocketClient(config);
            _client.Log += OnLog;
            _client.MessageReceived += OnMessageReceived;
            _client.Ready += OnReady;
            _client.Disconnected += OnDisconnected;

            try
            {
                await _client.LoginAsync(TokenType.Bot, settings.BotToken);
                await _client.StartAsync();
                Log.Out("[KitsuneCommand] Discord bot connecting...");
            }
            catch (HttpException ex)
            {
                Log.Error($"[KitsuneCommand] Discord bot login failed: {ex.Message}");
                lock (_lock) { _running = false; }
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] Discord bot start failed: {ex.Message}");
                lock (_lock) { _running = false; }
            }
        }

        public async Task StopAsync()
        {
            lock (_lock)
            {
                if (!_running) return;
                _running = false;
            }

            if (_client != null)
            {
                try
                {
                    await _client.StopAsync();
                    await _client.LogoutAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KitsuneCommand] Discord bot stop error: {ex.Message}");
                }
                finally
                {
                    _client.Log -= OnLog;
                    _client.MessageReceived -= OnMessageReceived;
                    _client.Ready -= OnReady;
                    _client.Disconnected -= OnDisconnected;
                    _client.Dispose();
                    _client = null;
                }
            }

            Log.Out("[KitsuneCommand] Discord bot stopped.");
        }

        public async Task SendMessageAsync(ulong channelId, string text)
        {
            if (!IsConnected || channelId == 0) return;

            try
            {
                var channel = _client.GetChannel(channelId) as IMessageChannel;
                if (channel != null)
                {
                    await channel.SendMessageAsync(text);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Discord send message failed: {ex.Message}");
            }
        }

        public async Task SendEmbedAsync(ulong channelId, EmbedBuilder embed)
        {
            if (!IsConnected || channelId == 0) return;

            try
            {
                var channel = _client.GetChannel(channelId) as IMessageChannel;
                if (channel != null)
                {
                    await channel.SendMessageAsync(embed: embed.Build());
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Discord send embed failed: {ex.Message}");
            }
        }

        public async Task RegisterSlashCommandsAsync()
        {
            if (_client == null || !IsConnected) return;

            try
            {
                var statusCmd = new SlashCommandBuilder()
                    .WithName("status")
                    .WithDescription("Show server status");

                var playersCmd = new SlashCommandBuilder()
                    .WithName("players")
                    .WithDescription("List online players");

                var timeCmd = new SlashCommandBuilder()
                    .WithName("time")
                    .WithDescription("Show in-game time");

                await _client.BulkOverwriteGlobalApplicationCommandsAsync(new[]
                {
                    statusCmd.Build(),
                    playersCmd.Build(),
                    timeCmd.Build()
                });

                _client.SlashCommandExecuted += OnSlashCommandExecuted;
                Log.Out("[KitsuneCommand] Discord slash commands registered.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to register slash commands: {ex.Message}");
            }
        }

        /// <summary>Fires when a slash command is executed. Feature wires this up.</summary>
        public event Func<SocketSlashCommand, Task> SlashCommandReceived;

        public async Task SetStatusAsync(string status)
        {
            if (_client == null || !IsConnected) return;

            try
            {
                await _client.SetGameAsync(status);
            }
            catch { /* non-critical */ }
        }

        // ---- Private handlers ----

        private Task OnReady()
        {
            Log.Out($"[KitsuneCommand] Discord bot connected as {_client.CurrentUser}");

            if (_settings?.SlashCommandsEnabled == true)
            {
                // Fire and forget — don't block the gateway
                _ = RegisterSlashCommandsAsync();
            }

            return Task.CompletedTask;
        }

        private Task OnDisconnected(Exception ex)
        {
            // Discord.Net handles automatic reconnection
            Log.Warning($"[KitsuneCommand] Discord bot disconnected: {ex?.Message ?? "unknown"}");
            return Task.CompletedTask;
        }

        private Task OnMessageReceived(SocketMessage msg)
        {
            // Ignore bot messages (including our own)
            if (msg.Author.IsBot) return Task.CompletedTask;

            // Only relay messages from the chat bridge channel
            if (_settings?.ChatBridgeEnabled == true &&
                msg.Channel.Id == _settings.ChatBridgeChannelId)
            {
                var username = (msg.Author as SocketGuildUser)?.DisplayName ?? msg.Author.Username;
                OnChatBridgeMessage?.Invoke(username, msg.Content);
            }

            return Task.CompletedTask;
        }

        private Task OnSlashCommandExecuted(SocketSlashCommand command)
        {
            SlashCommandReceived?.Invoke(command);
            return Task.CompletedTask;
        }

        private static Task OnLog(LogMessage log)
        {
            if (log.Severity <= LogSeverity.Warning)
            {
                Log.Warning($"[KitsuneCommand] Discord.Net: {log.Message}");
            }

            return Task.CompletedTask;
        }
    }
}
