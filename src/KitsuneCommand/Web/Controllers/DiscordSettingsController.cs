using System.Web.Http;
using KitsuneCommand.Features;
using KitsuneCommand.Services;
using KitsuneCommand.Web.Auth;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Web.Controllers
{
    /// <summary>
    /// API controller for managing Discord bot settings.
    /// Admin-only access.
    /// </summary>
    [RoutePrefix("api/settings/discord")]
    public class DiscordSettingsController : ApiController
    {
        private readonly DiscordBotFeature _feature;
        private readonly DiscordBotService _botService;

        public DiscordSettingsController(DiscordBotFeature feature, DiscordBotService botService)
        {
            _feature = feature;
            _botService = botService;
        }

        /// <summary>
        /// Get current Discord bot settings. Bot token is masked for security.
        /// </summary>
        [HttpGet]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetSettings()
        {
            var settings = _feature.Settings;

            // Return a copy with the token masked
            var masked = new
            {
                settings.Enabled,
                BotToken = MaskToken(settings.BotToken),
                settings.ChatBridgeEnabled,
                ChatBridgeChannelId = settings.ChatBridgeChannelId.ToString(),
                settings.EventNotificationsEnabled,
                EventChannelId = settings.EventChannelId.ToString(),
                settings.NotifyPlayerJoin,
                settings.NotifyPlayerLeave,
                settings.NotifyServerStart,
                settings.NotifyServerStop,
                settings.NotifyBloodMoon,
                settings.SlashCommandsEnabled,
                settings.ServerName,
                settings.ShowPlayerCountInStatus
            };

            return Ok(ApiResponse.Ok(masked));
        }

        /// <summary>
        /// Update Discord bot settings.
        /// </summary>
        [HttpPut]
        [Route("")]
        [RoleAuthorize("admin")]
        public IHttpActionResult UpdateSettings([FromBody] DiscordSettingsRequest request)
        {
            if (request == null)
                return BadRequest("Settings body is required.");

            var settings = _feature.Settings;

            settings.Enabled = request.Enabled;
            settings.ChatBridgeEnabled = request.ChatBridgeEnabled;
            settings.EventNotificationsEnabled = request.EventNotificationsEnabled;
            settings.NotifyPlayerJoin = request.NotifyPlayerJoin;
            settings.NotifyPlayerLeave = request.NotifyPlayerLeave;
            settings.NotifyServerStart = request.NotifyServerStart;
            settings.NotifyServerStop = request.NotifyServerStop;
            settings.NotifyBloodMoon = request.NotifyBloodMoon;
            settings.SlashCommandsEnabled = request.SlashCommandsEnabled;
            settings.ServerName = request.ServerName ?? settings.ServerName;
            settings.ShowPlayerCountInStatus = request.ShowPlayerCountInStatus;

            // Only update token if a new one was provided (not masked)
            if (!string.IsNullOrWhiteSpace(request.BotToken) && !request.BotToken.Contains("••••"))
            {
                settings.BotToken = request.BotToken;
            }

            // Parse channel IDs (sent as strings from JS since it can't handle uint64)
            if (ulong.TryParse(request.ChatBridgeChannelId, out var chatId))
                settings.ChatBridgeChannelId = chatId;
            if (ulong.TryParse(request.EventChannelId, out var eventId))
                settings.EventChannelId = eventId;

            _feature.UpdateSettings(settings);
            return Ok(ApiResponse.Ok("Discord settings updated."));
        }

        /// <summary>
        /// Get Discord bot connection status.
        /// </summary>
        [HttpGet]
        [Route("status")]
        [RoleAuthorize("admin")]
        public IHttpActionResult GetStatus()
        {
            var status = new
            {
                IsConnected = _botService.IsConnected,
                BotUsername = _botService.BotUsername ?? "",
                LatencyMs = _botService.Latency
            };

            return Ok(ApiResponse.Ok(status));
        }

        /// <summary>
        /// Send a test embed to the event channel.
        /// </summary>
        [HttpPost]
        [Route("test")]
        [RoleAuthorize("admin")]
        public IHttpActionResult TestConnection()
        {
            if (!_botService.IsConnected)
                return Ok(ApiResponse.Error(400, "Bot is not connected."));

            var channelId = _feature.Settings.EventChannelId;
            if (channelId == 0)
                return Ok(ApiResponse.Error(400, "Event channel not configured."));

            var embed = new Discord.EmbedBuilder()
                .WithColor(Discord.Color.Blue)
                .WithTitle("Test Notification")
                .WithDescription("KitsuneCommand Discord integration is working!")
                .WithFooter(_feature.Settings.ServerName)
                .WithCurrentTimestamp();

            _ = _botService.SendEmbedAsync(channelId, embed);
            return Ok(ApiResponse.Ok("Test embed sent."));
        }

        private static string MaskToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return "";
            if (token.Length <= 8) return "••••";
            return token.Substring(0, 4) + "••••" + token.Substring(token.Length - 4);
        }
    }

    /// <summary>
    /// Request model for Discord settings update.
    /// Channel IDs are strings because JavaScript cannot handle uint64.
    /// </summary>
    public class DiscordSettingsRequest
    {
        public bool Enabled { get; set; }
        public string BotToken { get; set; }
        public bool ChatBridgeEnabled { get; set; }
        public string ChatBridgeChannelId { get; set; }
        public bool EventNotificationsEnabled { get; set; }
        public string EventChannelId { get; set; }
        public bool NotifyPlayerJoin { get; set; }
        public bool NotifyPlayerLeave { get; set; }
        public bool NotifyServerStart { get; set; }
        public bool NotifyServerStop { get; set; }
        public bool NotifyBloodMoon { get; set; }
        public bool SlashCommandsEnabled { get; set; }
        public string ServerName { get; set; }
        public bool ShowPlayerCountInStatus { get; set; }
    }
}
