using System.Linq;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Services;
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// In-game ticket system. Players create tickets via chat; admins manage via web panel.
    /// Delivers queued admin replies when players log in.
    /// </summary>
    public class TicketFeature : FeatureBase<TicketSettings>
    {
        private readonly ITicketRepository _ticketRepo;
        private readonly ISettingsRepository _settingsRepo;
        private readonly LivePlayerManager _playerManager;
        private readonly DiscordWebhookService _discordService;

        public TicketFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ITicketRepository ticketRepo,
            ISettingsRepository settingsRepo,
            LivePlayerManager playerManager,
            DiscordWebhookService discordService)
            : base(eventBus, config)
        {
            _ticketRepo = ticketRepo;
            _settingsRepo = settingsRepo;
            _playerManager = playerManager;
            _discordService = discordService;
        }

        protected override void OnEnable()
        {
            LoadPersistedSettings();
            EventBus.Subscribe<PlayerLoginEvent>(OnPlayerLogin);
            Log.Out($"[KitsuneCommand] Ticket feature enabled. MaxOpen={Settings.MaxOpenTicketsPerPlayer}, Discord={!string.IsNullOrWhiteSpace(Settings.DiscordWebhookUrl)}");
        }

        protected override void OnDisable()
        {
            EventBus.Unsubscribe<PlayerLoginEvent>(OnPlayerLogin);
        }

        /// <summary>
        /// When a player logs in, deliver any queued admin replies.
        /// </summary>
        private void OnPlayerLogin(PlayerLoginEvent e)
        {
            if (!Settings.Enabled) return;

            try
            {
                var undelivered = _ticketRepo.GetUndeliveredMessages(e.PlayerId).ToList();
                if (undelivered.Count == 0) return;

                foreach (var msg in undelivered)
                {
                    var reply = $"[Ticket] {msg.SenderName ?? "Admin"}: {msg.Message}";
                    var safeReply = reply.Replace("\"", "'");
                    SdtdConsole.Instance.ExecuteSync($"pm {e.EntityId} \"{safeReply}\"", null);
                }

                _ticketRepo.MarkMessagesDelivered(undelivered.Select(m => m.Id));
                Log.Out($"[KitsuneCommand] Delivered {undelivered.Count} ticket reply(s) to {e.PlayerName}");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to deliver ticket replies to {e.PlayerName}: {ex.Message}");
            }
        }

        public void UpdateSettings(TicketSettings newSettings)
        {
            Settings = newSettings;
            var json = JsonConvert.SerializeObject(newSettings);
            _settingsRepo.Set(SettingsKey, json);
        }

        private string SettingsKey => "Ticket";

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<TicketSettings>(json);
                    if (loaded != null) Settings = loaded;
                }
            }
            catch { /* use defaults */ }
        }
    }
}
