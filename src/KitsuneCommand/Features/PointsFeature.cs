using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Points economy feature: awards points for kills, daily sign-in, and playtime.
    /// Broadcasts PointsUpdateEvent via ModEventBus for real-time WebSocket updates.
    /// </summary>
    public class PointsFeature : FeatureBase<PointsSettings>
    {
        private readonly IPointsRepository _pointsRepo;
        private readonly LivePlayerManager _playerManager;
        private Timer _playtimeTimer;

        public PointsFeature(
            ModEventBus eventBus,
            ConfigManager config,
            IPointsRepository pointsRepo,
            LivePlayerManager playerManager)
            : base(eventBus, config)
        {
            _pointsRepo = pointsRepo;
            _playerManager = playerManager;
        }

        protected override void OnEnable()
        {
            EventBus.Subscribe<EntityKilledEvent>(OnEntityKilled);
            EventBus.Subscribe<PlayerLoginEvent>(OnPlayerLogin);

            // Start playtime timer
            var intervalMs = Settings.PlaytimeIntervalMinutes * 60 * 1000;
            _playtimeTimer = new Timer(OnPlaytimeTick, null, intervalMs, intervalMs);

            Log.Out($"[KitsuneCommand] Points feature enabled. ZombieKill={Settings.ZombieKillPoints}, " +
                    $"PlayerKill={Settings.PlayerKillPoints}, SignIn={Settings.SignInBonus}, " +
                    $"Playtime={Settings.PlaytimePointsPerHour}/hr every {Settings.PlaytimeIntervalMinutes}min");
        }

        protected override void OnDisable()
        {
            EventBus.Unsubscribe<EntityKilledEvent>(OnEntityKilled);
            EventBus.Unsubscribe<PlayerLoginEvent>(OnPlayerLogin);

            _playtimeTimer?.Dispose();
            _playtimeTimer = null;
        }

        private void OnEntityKilled(EntityKilledEvent e)
        {
            try
            {
                // Check if the killer is an online player
                var killer = _playerManager.GetByEntityId(e.KillerEntityId);
                if (killer == null) return; // Killer is not a tracked player, skip

                // Determine if it's a zombie kill or a player kill
                var dead = _playerManager.GetByEntityId(e.DeadEntityId);
                int points;
                string reason;

                if (dead != null)
                {
                    // PvP kill — dead entity is also a player
                    points = Settings.PlayerKillPoints;
                    reason = $"Killed player {e.DeadEntityName}";
                }
                else
                {
                    // Zombie / NPC kill
                    points = Settings.ZombieKillPoints;
                    reason = $"Killed {e.DeadEntityName}";
                }

                if (points <= 0) return;

                _pointsRepo.UpsertPlayer(killer.PlayerId, killer.PlayerName);
                var newTotal = _pointsRepo.AdjustPoints(killer.PlayerId, points);

                EventBus.Publish(new PointsUpdateEvent
                {
                    PlayerId = killer.PlayerId,
                    PlayerName = killer.PlayerName,
                    Points = newTotal,
                    Change = points,
                    Reason = reason
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Points: Error processing kill event: {ex.Message}");
            }
        }

        private void OnPlayerLogin(PlayerLoginEvent e)
        {
            try
            {
                // Ensure the player has a points row
                _pointsRepo.UpsertPlayer(e.PlayerId, e.PlayerName);

                // Attempt daily sign-in bonus
                if (Settings.SignInBonus > 0 && _pointsRepo.TrySignIn(e.PlayerId, Settings.SignInBonus))
                {
                    var info = _pointsRepo.GetByPlayerId(e.PlayerId);
                    EventBus.Publish(new PointsUpdateEvent
                    {
                        PlayerId = e.PlayerId,
                        PlayerName = e.PlayerName,
                        Points = info?.Points ?? Settings.SignInBonus,
                        Change = Settings.SignInBonus,
                        Reason = "Daily sign-in bonus"
                    });

                    Log.Out($"[KitsuneCommand] Points: Sign-in bonus of {Settings.SignInBonus} awarded to {e.PlayerName}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Points: Error processing login event: {ex.Message}");
            }
        }

        private void OnPlaytimeTick(object _)
        {
            try
            {
                var onlinePlayers = _playerManager.GetAllOnline();
                if (!onlinePlayers.Any()) return;

                // Calculate points per tick: e.g., 20/hr at 10-min intervals = ~3 pts per tick
                var pointsPerTick = (int)Math.Max(1,
                    Math.Round((double)Settings.PlaytimePointsPerHour * Settings.PlaytimeIntervalMinutes / 60));

                foreach (var player in onlinePlayers)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(player.PlayerId)) continue;

                        _pointsRepo.UpsertPlayer(player.PlayerId, player.PlayerName);
                        var newTotal = _pointsRepo.AdjustPoints(player.PlayerId, pointsPerTick);

                        EventBus.Publish(new PointsUpdateEvent
                        {
                            PlayerId = player.PlayerId,
                            PlayerName = player.PlayerName,
                            Points = newTotal,
                            Change = pointsPerTick,
                            Reason = "Playtime bonus"
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[KitsuneCommand] Points: Playtime tick error for {player.PlayerName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Points: Playtime tick error: {ex.Message}");
            }
        }
    }
}
