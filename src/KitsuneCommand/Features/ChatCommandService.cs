using System.Collections.Concurrent;
using System.Globalization;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Processes in-game chat commands. Runs on the game thread (called from OnChatMessage),
    /// so SdtdConsole.Instance.ExecuteSync is safe to call directly.
    /// </summary>
    public class ChatCommandService
    {
        private readonly IHomeLocationRepository _homeRepo;
        private readonly ICityLocationRepository _cityRepo;
        private readonly IPointsRepository _pointsRepo;
        private readonly IGoodsRepository _goodsRepo;
        private readonly IPurchaseHistoryRepository _purchaseRepo;
        private readonly ITeleRecordRepository _teleRecordRepo;
        private readonly LivePlayerManager _playerManager;
        private readonly ModEventBus _eventBus;

        // Cooldown tracking: playerId -> { commandGroup -> lastUsedUtc }
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DateTime>> _cooldowns
            = new ConcurrentDictionary<string, ConcurrentDictionary<string, DateTime>>();

        public ChatCommandService(
            IHomeLocationRepository homeRepo,
            ICityLocationRepository cityRepo,
            IPointsRepository pointsRepo,
            IGoodsRepository goodsRepo,
            IPurchaseHistoryRepository purchaseRepo,
            ITeleRecordRepository teleRecordRepo,
            LivePlayerManager playerManager,
            ModEventBus eventBus)
        {
            _homeRepo = homeRepo;
            _cityRepo = cityRepo;
            _pointsRepo = pointsRepo;
            _goodsRepo = goodsRepo;
            _purchaseRepo = purchaseRepo;
            _teleRecordRepo = teleRecordRepo;
            _playerManager = playerManager;
            _eventBus = eventBus;
        }

        /// <summary>
        /// Attempts to handle a chat message as a command.
        /// Returns true if the message was a recognized command, false otherwise.
        /// </summary>
        public bool TryHandleCommand(string playerId, int entityId, string playerName, string message, ChatCommandSettings settings)
        {
            if (string.IsNullOrEmpty(message) || !message.StartsWith(settings.Prefix))
                return false;

            // Strip prefix and split into command + args
            var content = message.Substring(settings.Prefix.Length).Trim();
            if (string.IsNullOrEmpty(content))
                return false;

            var parts = content.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLowerInvariant();
            var args = parts.Length > 1 ? parts[1].Trim() : "";

            try
            {
                switch (command)
                {
                    // ── Home Commands ──────────────────────────────────
                    case "home":
                        if (!settings.HomeEnabled) { Reply(entityId, "Home commands are disabled."); return true; }
                        HandleHome(playerId, entityId, playerName, args, settings);
                        return true;

                    case "sethome":
                        if (!settings.HomeEnabled) { Reply(entityId, "Home commands are disabled."); return true; }
                        HandleSetHome(playerId, entityId, playerName, args, settings);
                        return true;

                    case "delhome":
                        if (!settings.HomeEnabled) { Reply(entityId, "Home commands are disabled."); return true; }
                        HandleDelHome(playerId, entityId, args);
                        return true;

                    case "homes":
                        if (!settings.HomeEnabled) { Reply(entityId, "Home commands are disabled."); return true; }
                        HandleListHomes(playerId, entityId);
                        return true;

                    // ── Teleport Commands ──────────────────────────────
                    case "tp":
                        if (!settings.TeleportEnabled) { Reply(entityId, "Teleport commands are disabled."); return true; }
                        HandleTeleportToCity(playerId, entityId, playerName, args, settings);
                        return true;

                    case "cities":
                        if (!settings.TeleportEnabled) { Reply(entityId, "Teleport commands are disabled."); return true; }
                        HandleListCities(entityId);
                        return true;

                    // ── Points Commands ────────────────────────────────
                    case "points":
                        if (!settings.PointsEnabled) { Reply(entityId, "Points commands are disabled."); return true; }
                        HandlePoints(playerId, entityId);
                        return true;

                    case "signin":
                        if (!settings.PointsEnabled) { Reply(entityId, "Points commands are disabled."); return true; }
                        HandleSignIn(playerId, entityId, playerName);
                        return true;

                    // ── Store Commands ─────────────────────────────────
                    case "shop":
                        if (!settings.StoreEnabled) { Reply(entityId, "Store commands are disabled."); return true; }
                        HandleShop(entityId);
                        return true;

                    case "buy":
                        if (!settings.StoreEnabled) { Reply(entityId, "Store commands are disabled."); return true; }
                        HandleBuy(playerId, entityId, playerName, args, settings);
                        return true;

                    default:
                        return false; // Not a recognized command
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] ChatCommand error ({command}): {ex.Message}");
                Reply(entityId, "An error occurred processing your command.");
                return true;
            }
        }

        // ─── Home Commands ─────────────────────────────────────────────

        private void HandleHome(string playerId, int entityId, string playerName, string args, ChatCommandSettings settings)
        {
            var homeName = string.IsNullOrWhiteSpace(args) ? "home" : args.Trim();

            var home = _homeRepo.GetByPlayerIdAndName(playerId, homeName);
            if (home == null)
            {
                Reply(entityId, $"Home '{homeName}' not found. Use /homes to see your homes.");
                return;
            }

            if (!CheckCooldown(playerId, entityId, "home", settings.HomeCooldownSeconds))
                return;

            if (!TryParsePosition(home.Position, out var x, out var y, out var z))
            {
                Reply(entityId, "Home has an invalid position.");
                return;
            }

            // Get player's current position for teleport record
            var player = _playerManager.GetByEntityId(entityId);
            var originPos = player != null
                ? $"{player.PositionX.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionY.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionZ.ToString("F1", CultureInfo.InvariantCulture)}"
                : "0 0 0";

            // Execute teleport
            var cmd = $"teleportplayer {entityId} {x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)} {z.ToString(CultureInfo.InvariantCulture)}";
            SdtdConsole.Instance.ExecuteSync(cmd, null);

            // Record teleport
            _teleRecordRepo.Insert(new TeleRecord
            {
                PlayerId = playerId,
                PlayerName = playerName,
                TargetType = 1, // Home
                TargetName = home.HomeName,
                OriginPosition = originPos,
                TargetPosition = home.Position
            });

            Reply(entityId, $"Teleported to home '{home.HomeName}'.");
        }

        private void HandleSetHome(string playerId, int entityId, string playerName, string args, ChatCommandSettings settings)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Reply(entityId, "Usage: /sethome <name>");
                return;
            }

            var homeName = args.Trim();

            // Check if home already exists (update) or new (insert with limit check)
            var existing = _homeRepo.GetByPlayerIdAndName(playerId, homeName);
            var player = _playerManager.GetByEntityId(entityId);
            if (player == null)
            {
                Reply(entityId, "Could not determine your position.");
                return;
            }

            var position = $"{player.PositionX.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionY.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionZ.ToString("F1", CultureInfo.InvariantCulture)}";

            if (existing != null)
            {
                // Update existing home position
                existing.Position = position;
                existing.PlayerName = playerName;
                _homeRepo.Update(existing);
                Reply(entityId, $"Home '{homeName}' updated to your current position.");
            }
            else
            {
                // Check home limit
                var count = _homeRepo.GetCountByPlayerId(playerId);
                if (count >= settings.MaxHomesPerPlayer)
                {
                    Reply(entityId, $"You have reached the maximum of {settings.MaxHomesPerPlayer} homes. Delete one first with /delhome <name>.");
                    return;
                }

                _homeRepo.Insert(new HomeLocation
                {
                    PlayerId = playerId,
                    PlayerName = playerName,
                    HomeName = homeName,
                    Position = position
                });
                Reply(entityId, $"Home '{homeName}' saved at {position}.");
            }
        }

        private void HandleDelHome(string playerId, int entityId, string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Reply(entityId, "Usage: /delhome <name>");
                return;
            }

            var homeName = args.Trim();
            var home = _homeRepo.GetByPlayerIdAndName(playerId, homeName);
            if (home == null)
            {
                Reply(entityId, $"Home '{homeName}' not found.");
                return;
            }

            _homeRepo.Delete(home.Id);
            Reply(entityId, $"Home '{homeName}' deleted.");
        }

        private void HandleListHomes(string playerId, int entityId)
        {
            var homes = _homeRepo.GetByPlayerId(playerId).ToList();
            if (homes.Count == 0)
            {
                Reply(entityId, "You have no saved homes. Use /sethome <name> to create one.");
                return;
            }

            var list = string.Join(", ", homes.Select(h => h.HomeName));
            Reply(entityId, $"Your homes ({homes.Count}): {list}");
        }

        // ─── Teleport Commands ─────────────────────────────────────────

        private void HandleTeleportToCity(string playerId, int entityId, string playerName, string args, ChatCommandSettings settings)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Reply(entityId, "Usage: /tp <city name>. Use /cities to see available locations.");
                return;
            }

            var cityName = args.Trim();
            var city = _cityRepo.GetByName(cityName);
            if (city == null)
            {
                Reply(entityId, $"City '{cityName}' not found. Use /cities to see available locations.");
                return;
            }

            if (!CheckCooldown(playerId, entityId, "teleport", settings.TeleportCooldownSeconds))
                return;

            // Check & deduct points
            if (city.PointsRequired > 0)
            {
                var pointsInfo = _pointsRepo.GetByPlayerId(playerId);
                if (pointsInfo == null || pointsInfo.Points < city.PointsRequired)
                {
                    Reply(entityId, $"Not enough points. {city.CityName} costs {city.PointsRequired} pts, you have {pointsInfo?.Points ?? 0}.");
                    return;
                }

                var newBalance = _pointsRepo.AdjustPoints(playerId, -city.PointsRequired);
                _eventBus.Publish(new PointsUpdateEvent
                {
                    PlayerId = playerId,
                    PlayerName = playerName,
                    Points = newBalance,
                    Change = -city.PointsRequired,
                    Reason = $"Teleport to {city.CityName}"
                });
            }

            if (!TryParsePosition(city.Position, out var cx, out var cy, out var cz))
            {
                Reply(entityId, "City has an invalid position.");
                return;
            }

            // Get origin position
            var player = _playerManager.GetByEntityId(entityId);
            var originPos = player != null
                ? $"{player.PositionX.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionY.ToString("F1", CultureInfo.InvariantCulture)} {player.PositionZ.ToString("F1", CultureInfo.InvariantCulture)}"
                : "0 0 0";

            // Execute teleport
            var cmd = $"teleportplayer {entityId} {cx.ToString(CultureInfo.InvariantCulture)} {cy.ToString(CultureInfo.InvariantCulture)} {cz.ToString(CultureInfo.InvariantCulture)}";
            SdtdConsole.Instance.ExecuteSync(cmd, null);

            // Record teleport
            _teleRecordRepo.Insert(new TeleRecord
            {
                PlayerId = playerId,
                PlayerName = playerName,
                TargetType = 0, // City
                TargetName = city.CityName,
                OriginPosition = originPos,
                TargetPosition = city.Position
            });

            var costMsg = city.PointsRequired > 0 ? $" (-{city.PointsRequired} pts)" : "";
            Reply(entityId, $"Teleported to {city.CityName}.{costMsg}");
        }

        private void HandleListCities(int entityId)
        {
            var cities = _cityRepo.GetAll(0, 20).ToList();
            if (cities.Count == 0)
            {
                Reply(entityId, "No cities available.");
                return;
            }

            var lines = cities.Select(c =>
                c.PointsRequired > 0
                    ? $"{c.CityName} ({c.PointsRequired} pts)"
                    : c.CityName);
            Reply(entityId, $"Cities: {string.Join(", ", lines)}");
        }

        // ─── Points Commands ───────────────────────────────────────────

        private void HandlePoints(string playerId, int entityId)
        {
            var info = _pointsRepo.GetByPlayerId(playerId);
            var pts = info?.Points ?? 0;
            Reply(entityId, $"Your points: {pts.ToString("N0")}");
        }

        private void HandleSignIn(string playerId, int entityId, string playerName)
        {
            // Use the PointsFeature's sign-in bonus value (default 100)
            // We read from the points feature settings indirectly via the repository
            _pointsRepo.UpsertPlayer(playerId, playerName);

            // Attempt sign-in with the default bonus (100 pts)
            // The PointsFeature's Settings.SignInBonus controls the actual value,
            // but from here we use a reasonable default since we can't easily access PointsFeature
            const int signInBonus = 100;
            if (_pointsRepo.TrySignIn(playerId, signInBonus))
            {
                var info = _pointsRepo.GetByPlayerId(playerId);
                _eventBus.Publish(new PointsUpdateEvent
                {
                    PlayerId = playerId,
                    PlayerName = playerName,
                    Points = info?.Points ?? signInBonus,
                    Change = signInBonus,
                    Reason = "Daily sign-in bonus"
                });
                Reply(entityId, $"Daily sign-in bonus: +{signInBonus} pts! Balance: {info?.Points ?? signInBonus}");
            }
            else
            {
                Reply(entityId, "You've already signed in today. Come back tomorrow!");
            }
        }

        // ─── Store Commands ────────────────────────────────────────────

        private void HandleShop(int entityId)
        {
            var goods = _goodsRepo.GetAll(0, 20).ToList();
            if (goods.Count == 0)
            {
                Reply(entityId, "The shop is empty.");
                return;
            }

            var lines = goods.Select(g => $"{g.Name} ({g.Price} pts)");
            Reply(entityId, $"Shop: {string.Join(", ", lines)}");
        }

        private void HandleBuy(string playerId, int entityId, string playerName, string args, ChatCommandSettings settings)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Reply(entityId, "Usage: /buy <item name>. Use /shop to see available items.");
                return;
            }

            var itemName = args.Trim();
            var goods = _goodsRepo.GetByName(itemName);
            if (goods == null)
            {
                Reply(entityId, $"Item '{itemName}' not found. Use /shop to see available items.");
                return;
            }

            // Check points
            var pointsInfo = _pointsRepo.GetByPlayerId(playerId);
            if (pointsInfo == null || pointsInfo.Points < goods.Price)
            {
                Reply(entityId, $"Not enough points. '{goods.Name}' costs {goods.Price} pts, you have {pointsInfo?.Points ?? 0}.");
                return;
            }

            // Deduct points
            var newBalance = _pointsRepo.AdjustPoints(playerId, -goods.Price);

            // Give items
            var items = _goodsRepo.GetItemsForGoods(goods.Id).ToList();
            foreach (var item in items)
            {
                var cmd = $"give {entityId} {item.ItemName} {item.Count} {item.Quality}";
                SdtdConsole.Instance.ExecuteSync(cmd, null);
            }

            // Execute commands
            var commands = _goodsRepo.GetCommandsForGoods(goods.Id).ToList();
            foreach (var cmdDef in commands)
            {
                var cmd = cmdDef.Command
                    .Replace("{entityId}", entityId.ToString())
                    .Replace("{playerId}", playerId)
                    .Replace("{playerName}", playerName);
                SdtdConsole.Instance.ExecuteSync(cmd, null);
            }

            // Record purchase
            _purchaseRepo.Insert(new PurchaseRecord
            {
                PlayerId = playerId,
                PlayerName = playerName,
                GoodsId = goods.Id,
                GoodsName = goods.Name,
                Price = goods.Price
            });

            // Broadcast points update
            _eventBus.Publish(new PointsUpdateEvent
            {
                PlayerId = playerId,
                PlayerName = playerName,
                Points = newBalance,
                Change = -goods.Price,
                Reason = $"Purchased {goods.Name}"
            });

            Reply(entityId, $"Purchased '{goods.Name}' for {goods.Price} pts. Balance: {newBalance}");
        }

        // ─── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Sends a private message to the player via the game console.
        /// </summary>
        private void Reply(int entityId, string message)
        {
            try
            {
                // Escape double quotes in the message
                var safeMessage = message.Replace("\"", "'");
                SdtdConsole.Instance.ExecuteSync($"pm {entityId} \"{safeMessage}\"", null);
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to send chat reply: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the player is on cooldown for a command group.
        /// Returns true if the player can proceed, false if on cooldown (reply sent).
        /// </summary>
        private bool CheckCooldown(string playerId, int entityId, string group, int cooldownSeconds)
        {
            if (cooldownSeconds <= 0) return true;

            var playerCooldowns = _cooldowns.GetOrAdd(playerId,
                _ => new ConcurrentDictionary<string, DateTime>());

            if (playerCooldowns.TryGetValue(group, out var lastUsed))
            {
                var elapsed = (DateTime.UtcNow - lastUsed).TotalSeconds;
                if (elapsed < cooldownSeconds)
                {
                    var remaining = (int)Math.Ceiling(cooldownSeconds - elapsed);
                    Reply(entityId, $"Please wait {remaining}s before using this command again.");
                    return false;
                }
            }

            playerCooldowns[group] = DateTime.UtcNow;
            return true;
        }

        /// <summary>
        /// Parses a position string in "x y z" format.
        /// </summary>
        private static bool TryParsePosition(string position, out float x, out float y, out float z)
        {
            x = y = z = 0;
            if (string.IsNullOrWhiteSpace(position)) return false;
            var parts = position.Trim().Split(' ');
            return parts.Length == 3
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
        }
    }
}
