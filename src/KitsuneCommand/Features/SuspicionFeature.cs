using System.Text.RegularExpressions;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features.Suspicion;
using KitsuneCommand.GameIntegration;
using KitsuneCommand.Services;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Suspicion log: watches server-side signals that a modified client can't
    /// hide (inventory contents, movement, progression, kill rate) and records
    /// anything out of bounds for admins to review, with optional Discord
    /// alerts. It never kicks, bans or otherwise touches the player.
    ///
    /// Main-thread cost: movement and progression ride on LivePlayerManager's
    /// existing 3-second position sweep; the inventory scan is one short
    /// main-thread read per online player per scan interval.
    /// </summary>
    public class SuspicionFeature : FeatureBase<SuspicionSettings>
    {
        private const string SettingsKey = "Suspicion";

        // "Executing command 'teleportplayer Ada 10 60 20' by ..." / "'tele 171 ...' from client ..."
        private static readonly Regex ConsoleTeleport = new Regex(
            @"Executing command '(?:teleportplayer|tele)\s+(""[^""]+""|[^\s']+)",RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ISuspicionEventRepository _repo;
        private readonly ISettingsRepository _settingsRepo;
        private readonly LivePlayerManager _playerManager;
        private readonly GameItemCatalog _itemCatalog;
        private readonly SuspicionRecorder _recorder;

        private readonly InventoryDetector _inventory = new InventoryDetector();
        private readonly MovementDetector _movement = new MovementDetector();
        private readonly ProgressionDetector _progression = new ProgressionDetector();
        private readonly KillRateDetector _killRate = new KillRateDetector();

        private Timer _inventoryTimer;
        private Timer _pruneTimer;
        private int _scanning;

        public SuspicionFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ISuspicionEventRepository repo,
            ISettingsRepository settingsRepo,
            LivePlayerManager playerManager,
            GameItemCatalog itemCatalog,
            DiscordWebhookService discordService)
            : base(eventBus, config)
        {
            _repo = repo;
            _settingsRepo = settingsRepo;
            _playerManager = playerManager;
            _itemCatalog = itemCatalog;
            _recorder = new SuspicionRecorder(repo, discordService.SendSuspicionAlert);
        }

        protected override void OnEnable()
        {
            LoadPersistedSettings();

            EventBus.Subscribe<PlayerSpawnedEvent>(OnPlayerSpawned);
            EventBus.Subscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected);
            EventBus.Subscribe<PlayerTeleportedEvent>(OnPlayerTeleported);
            EventBus.Subscribe<PlayerSamplesEvent>(OnPlayerSamples);
            EventBus.Subscribe<SavePlayerDataEvent>(OnSavePlayerData);
            EventBus.Subscribe<EntityKilledEvent>(OnEntityKilled);
            EventBus.Subscribe<LogCallbackEvent>(OnLog);

            RestartInventoryTimer();
            _pruneTimer = new Timer(_ => Prune(), null, TimeSpan.FromMinutes(5), TimeSpan.FromHours(1));

            Log.Out($"[KitsuneCommand] Suspicion log enabled={Settings.Enabled} (log-only). Inventory={Settings.Inventory.Enabled}, " +
                    $"Movement={Settings.Movement.Enabled}, Progression={Settings.Progression.Enabled}, KillRate={Settings.KillRate.Enabled}, " +
                    $"Discord={!string.IsNullOrWhiteSpace(Settings.DiscordWebhookUrl)}");
        }

        protected override void OnDisable()
        {
            EventBus.Unsubscribe<PlayerSpawnedEvent>(OnPlayerSpawned);
            EventBus.Unsubscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected);
            EventBus.Unsubscribe<PlayerTeleportedEvent>(OnPlayerTeleported);
            EventBus.Unsubscribe<PlayerSamplesEvent>(OnPlayerSamples);
            EventBus.Unsubscribe<SavePlayerDataEvent>(OnSavePlayerData);
            EventBus.Unsubscribe<EntityKilledEvent>(OnEntityKilled);
            EventBus.Unsubscribe<LogCallbackEvent>(OnLog);

            _inventoryTimer?.Dispose();
            _inventoryTimer = null;
            _pruneTimer?.Dispose();
            _pruneTimer = null;
        }

        public void UpdateSettings(SuspicionSettings newSettings)
        {
            Settings = Normalize(newSettings);
            _settingsRepo.Set(SettingsKey, JsonConvert.SerializeObject(Settings));
            if (IsRunning) RestartInventoryTimer();
            Log.Out("[KitsuneCommand] Suspicion log settings updated.");
        }

        /// <summary>Fills missing sections and clamps every number to a sane range.</summary>
        public static SuspicionSettings Normalize(SuspicionSettings s)
        {
            s = s ?? new SuspicionSettings();
            s.ExemptPlayerIds = CleanList(s.ExemptPlayerIds);
            s.RepeatCooldownMinutes = Clamp(s.RepeatCooldownMinutes, 0, 1440);
            s.RetentionDays = Clamp(s.RetentionDays, 0, 3650);
            s.DiscordWebhookUrl = s.DiscordWebhookUrl?.Trim() ?? "";
            s.DiscordCooldownMinutes = Clamp(s.DiscordCooldownMinutes, 0, 1440);
            var sev = (s.DiscordMinSeverity ?? "").Trim().ToLowerInvariant();
            s.DiscordMinSeverity = sev == SuspicionSeverity.Low || sev == SuspicionSeverity.High ? sev : SuspicionSeverity.Medium;

            var inv = s.Inventory ??= new InventoryDetectorSettings();
            inv.ScanIntervalSeconds = Clamp(inv.ScanIntervalSeconds, 15, 3600);
            inv.MaxQuality = Clamp(inv.MaxQuality, 0, 100);
            inv.CountJumpThreshold = Clamp(inv.CountJumpThreshold, 0, 1000000);
            inv.AllowList = CleanList(inv.AllowList);

            var mv = s.Movement ??= new MovementDetectorSettings();
            mv.MaxHorizontalSpeed = Clamp(mv.MaxHorizontalSpeed, 0, 1000);
            mv.SpeedSamplesRequired = Clamp(mv.SpeedSamplesRequired, 1, 20);
            mv.JumpDistance = Clamp(mv.JumpDistance, 0, 100000);
            mv.FlyHeight = Clamp(mv.FlyHeight, 0, 1000);
            mv.FlySamplesRequired = Clamp(mv.FlySamplesRequired, 1, 100);
            mv.GraceSeconds = Clamp(mv.GraceSeconds, 0, 300);

            var pr = s.Progression ??= new ProgressionDetectorSettings();
            pr.WindowMinutes = Clamp(pr.WindowMinutes, 1, 1440);
            pr.MaxLevelGain = Clamp(pr.MaxLevelGain, 0, 1000);
            pr.MaxUnexplainedSkillPoints = Clamp(pr.MaxUnexplainedSkillPoints, 0, 1000);
            pr.WarmupSeconds = Clamp(pr.WarmupSeconds, 0, 3600);

            var kr = s.KillRate ??= new KillRateDetectorSettings();
            kr.MaxZombieKillsPerMinute = Clamp(kr.MaxZombieKillsPerMinute, 0, 10000);
            return s;
        }

        /// <summary>
        /// Reads the target out of a console teleport log line ("teleportplayer"
        /// or its "tele" alias): an entity id, a player name or a platform id.
        /// Null if the line isn't a console teleport.
        /// </summary>
        public static string ParseConsoleTeleportTarget(string logLine)
        {
            if (string.IsNullOrEmpty(logLine) || logLine.IndexOf("Executing command", StringComparison.Ordinal) < 0)
                return null;
            var m = ConsoleTeleport.Match(logLine);
            return m.Success ? m.Groups[1].Value.Trim('"') : null;
        }

        // --- Events ---

        private void OnPlayerSpawned(PlayerSpawnedEvent e)
        {
            var now = DateTime.UtcNow;
            _movement.Grace(e.EntityId, now.AddSeconds(Settings.Movement.GraceSeconds));
            _progression.Restart(e.EntityId, now, TimeSpan.FromSeconds(Settings.Progression.WarmupSeconds));
        }

        private void OnPlayerTeleported(PlayerTeleportedEvent e)
        {
            _movement.Grace(e.EntityId, DateTime.UtcNow.AddSeconds(Settings.Movement.GraceSeconds));
        }

        private void OnPlayerDisconnected(PlayerDisconnectedEvent e)
        {
            _inventory.Forget(e.EntityId);
            _movement.Forget(e.EntityId);
            _progression.Forget(e.EntityId);
            _killRate.Forget(e.EntityId);
        }

        private void OnLog(LogCallbackEvent e)
        {
            var target = ParseConsoleTeleportTarget(e.Message);
            if (target == null) return;

            var player = int.TryParse(target, out var entityId)
                ? _playerManager.GetByEntityId(entityId)
                : _playerManager.GetAllOnline().FirstOrDefault(p =>
                    string.Equals(p.PlayerName, target, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.PlayerId, target, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.PlatformId, target, StringComparison.OrdinalIgnoreCase));
            if (player != null)
                _movement.Grace(player.EntityId, DateTime.UtcNow.AddSeconds(Settings.Movement.GraceSeconds));
        }

        private void OnPlayerSamples(PlayerSamplesEvent e)
        {
            var s = Settings;
            if (!s.Enabled || e?.Samples == null) return;
            if (!s.Movement.Enabled && !s.Progression.Enabled) return;

            var skillPointsPerLevel = SkillPointsPerLevel();
            foreach (var sample in e.Samples)
            {
                var player = Suspect(sample.EntityId);
                if (player == null || SuspicionRecorder.IsExempt(player, s)) continue;

                if (s.Movement.Enabled)
                    foreach (var flag in _movement.Observe(sample, s.Movement))
                        Record(player, flag, s);

                if (s.Progression.Enabled)
                    foreach (var flag in _progression.Observe(sample, s.Progression, skillPointsPerLevel))
                        Record(player, flag, s, sample);
            }
        }

        /// <summary>
        /// The client just pushed fresh player data, progression included, so
        /// check it now rather than at the next sweep. Runs on the main thread,
        /// where GetPlayerSample reads inline.
        /// </summary>
        private void OnSavePlayerData(SavePlayerDataEvent e)
        {
            var s = Settings;
            if (!s.Enabled || !s.Progression.Enabled) return;

            var player = Suspect(e.EntityId);
            if (player == null || SuspicionRecorder.IsExempt(player, s)) return;

            try
            {
                var sample = _playerManager.GetPlayerSample(e.EntityId);
                if (sample == null) return;
                foreach (var flag in _progression.Observe(sample, s.Progression, SkillPointsPerLevel()))
                    Record(player, flag, s, sample);
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Suspicion: progression check failed for {e.PlayerName}: {ex.Message}");
            }
        }

        private void OnEntityKilled(EntityKilledEvent e)
        {
            var s = Settings;
            if (!s.Enabled || !s.KillRate.Enabled) return;
            if (!string.Equals(e.DeadEntityType, "Zombie", StringComparison.OrdinalIgnoreCase)) return;

            var player = Suspect(e.KillerEntityId);
            if (player == null || SuspicionRecorder.IsExempt(player, s)) return;

            var flag = _killRate.Observe(e.KillerEntityId, DateTime.UtcNow, s.KillRate);
            if (flag != null) Record(player, flag, s);
        }

        // --- Inventory scan ---

        private void RestartInventoryTimer()
        {
            _inventoryTimer?.Dispose();
            var interval = TimeSpan.FromSeconds(Math.Max(15, Settings.Inventory.ScanIntervalSeconds));
            _inventoryTimer = new Timer(_ => ScanInventories(), null, interval, interval);
        }

        private void ScanInventories()
        {
            if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
            try
            {
                var s = Settings;
                if (!s.Enabled || !s.Inventory.Enabled || !ModEntry.IsGameStartDone) return;

                var gameMaxQuality = 0;
                try { gameMaxQuality = GameCompat.GetMaxQualityTier(); } catch { /* older build: default cap */ }

                foreach (var online in _playerManager.GetAllOnline())
                {
                    var player = ToSuspect(online);
                    if (SuspicionRecorder.IsExempt(player, s)) continue;

                    // One short main-thread read per player; the checks run here on the timer thread.
                    var (bag, belt) = _playerManager.GetPlayerInventory(online.EntityId);
                    if (bag == null && belt == null) continue;

                    var items = (bag ?? new List<Web.Models.InventorySlot>())
                        .Concat(belt ?? new List<Web.Models.InventorySlot>())
                        .Select(slot => new InventoryItemSample { ItemName = slot.ItemName, Count = slot.Count, Quality = slot.Quality });

                    foreach (var flag in _inventory.Check(online.EntityId, items, _itemCatalog.GetItemMeta, gameMaxQuality, s.Inventory))
                    {
                        flag.X = online.PositionX;
                        flag.Y = online.PositionY;
                        flag.Z = online.PositionZ;
                        Record(player, flag, s);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Suspicion: inventory scan failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _scanning, 0);
            }
        }

        // --- Helpers ---

        private void Record(SuspectPlayer player, SuspicionFlag flag, SuspicionSettings s, PlayerSample at = null)
        {
            if (at != null && !flag.X.HasValue)
            {
                flag.X = at.X;
                flag.Y = at.Y;
                flag.Z = at.Z;
            }
            else if (!flag.X.HasValue)
            {
                var online = _playerManager.GetByEntityId(player.EntityId);
                if (online != null)
                {
                    flag.X = online.PositionX;
                    flag.Y = online.PositionY;
                    flag.Z = online.PositionZ;
                }
            }

            try
            {
                var evt = _recorder.Record(player, flag, s);
                if (evt != null)
                    Log.Out($"[KitsuneCommand] Suspicion [{evt.Severity}] {player.PlayerName}: {evt.Detector}/{evt.Rule} - {evt.Summary}");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Suspicion: failed to record {flag.Detector}/{flag.Rule}: {ex.Message}");
            }
        }

        private SuspectPlayer Suspect(int entityId)
        {
            var p = _playerManager.GetByEntityId(entityId);
            return p == null ? null : ToSuspect(p);
        }

        private static SuspectPlayer ToSuspect(PlayerInfo p) => new SuspectPlayer
        {
            PlayerId = p.PlayerId,
            PlatformId = p.PlatformId,
            PlayerName = p.PlayerName,
            EntityId = p.EntityId,
            IsAdmin = p.IsAdmin,
        };

        private static int SkillPointsPerLevel()
        {
            var spl = global::Progression.SkillPointsPerLevel;
            return spl > 0 ? spl : 1;
        }

        private void Prune()
        {
            try
            {
                var removed = _repo.DeleteOlderThan(Settings.RetentionDays);
                if (removed > 0)
                    Log.Out($"[KitsuneCommand] Suspicion log: pruned {removed} row(s) older than {Settings.RetentionDays} days.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Suspicion log prune failed: {ex.Message}");
            }
        }

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<SuspicionSettings>(json);
                    if (loaded != null) Settings = loaded;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load suspicion settings, using defaults: {ex.Message}");
            }
            Settings = Normalize(Settings);
        }

        private static List<string> CleanList(List<string> list) =>
            (list ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        private static double Clamp(double v, double min, double max) => double.IsNaN(v) ? min : v < min ? min : v > max ? max : v;
    }
}
