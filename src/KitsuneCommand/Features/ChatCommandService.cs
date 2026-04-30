using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Services;
using Newtonsoft.Json;

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
        private readonly IVipGiftRepository _vipGiftRepo;
        private readonly ISettingsRepository _settingsRepo;
        private readonly ITicketRepository _ticketRepo;
        private readonly LivePlayerManager _playerManager;
        private readonly ModEventBus _eventBus;
        private readonly BloodMoonVoteFeature _bloodMoonVoteFeature;
        private readonly VoteRewardsFeature _voteRewardsFeature;
        private readonly DiscordWebhookService _discordService;

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
            IVipGiftRepository vipGiftRepo,
            ITicketRepository ticketRepo,
            ISettingsRepository settingsRepo,
            LivePlayerManager playerManager,
            ModEventBus eventBus,
            BloodMoonVoteFeature bloodMoonVoteFeature,
            VoteRewardsFeature voteRewardsFeature,
            DiscordWebhookService discordService)
        {
            _homeRepo = homeRepo;
            _cityRepo = cityRepo;
            _pointsRepo = pointsRepo;
            _goodsRepo = goodsRepo;
            _purchaseRepo = purchaseRepo;
            _teleRecordRepo = teleRecordRepo;
            _vipGiftRepo = vipGiftRepo;
            _ticketRepo = ticketRepo;
            _settingsRepo = settingsRepo;
            _playerManager = playerManager;
            _eventBus = eventBus;
            _bloodMoonVoteFeature = bloodMoonVoteFeature;
            _voteRewardsFeature = voteRewardsFeature;
            _discordService = discordService;
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

                    // ── VIP Commands ─────────────────────────────────────
                    case "vip":
                        if (!settings.VipEnabled) { Reply(entityId, "VIP commands are disabled."); return true; }
                        HandleVipClaim(playerId, entityId, playerName);
                        return true;

                    // ── Ticket Commands ────────────────────────────────────
                    case "ticket":
                        if (!settings.TicketEnabled) { Reply(entityId, "Ticket commands are disabled."); return true; }
                        HandleTicket(playerId, entityId, playerName, args, settings);
                        return true;

                    case "tickets":
                        if (!settings.TicketEnabled) { Reply(entityId, "Ticket commands are disabled."); return true; }
                        HandleTicketList(playerId, entityId);
                        return true;

                    // ── Blood Moon Vote Commands ─────────────────────────
                    case "skipbm":
                    case "voteskip":
                        HandleBloodMoonVote(playerId, entityId, playerName);
                        return true;

                    // ── Vote-Reward Commands ─────────────────────────────
                    case "vote":
                        if (!settings.VoteEnabled) { Reply(entityId, "Vote-reward command is disabled."); return true; }
                        HandleVoteReward(playerId, entityId, playerName, settings);
                        return true;

                    // ── Help / discovery ─────────────────────────────────
                    // Always available regardless of feature toggles. If a server
                    // disables every group, /help still works and just reports
                    // back that nothing's enabled — which is itself useful info.
                    case "help":
                    case "commands":
                    case "?":
                        HandleHelp(entityId, settings);
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
            _pointsRepo.UpsertPlayer(playerId, playerName);

            var signInBonus = GetPointsSettings().SignInBonus;
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

        // ─── VIP Commands ───────────────────────────────────────────────

        private void HandleVipClaim(string playerId, int entityId, string playerName)
        {
            var gifts = _vipGiftRepo.GetPendingForPlayer(playerId).ToList();
            if (gifts.Count == 0)
            {
                Reply(entityId, "You have no pending VIP gifts.");
                return;
            }

            var claimed = 0;
            foreach (var gift in gifts)
            {
                // Give items
                var items = _vipGiftRepo.GetItemsForGift(gift.Id).ToList();
                foreach (var item in items)
                {
                    var cmd = $"give {entityId} {item.ItemName} {item.Count} {item.Quality}";
                    SdtdConsole.Instance.ExecuteSync(cmd, null);
                }

                // Execute commands
                var commands = _vipGiftRepo.GetCommandsForGift(gift.Id).ToList();
                foreach (var cmdDef in commands)
                {
                    var cmd = cmdDef.Command
                        .Replace("{entityId}", entityId.ToString())
                        .Replace("{playerId}", playerId)
                        .Replace("{playerName}", playerName);
                    SdtdConsole.Instance.ExecuteSync(cmd, null);
                }

                // Mark as claimed
                _vipGiftRepo.MarkAsClaimed(gift.Id);
                claimed++;
                Reply(entityId, $"Claimed VIP gift: {gift.Name}");
            }

            if (claimed > 1)
                Reply(entityId, $"All {claimed} VIP gifts claimed!");
        }

        // ─── Ticket Commands ──────────────────────────────────────────

        private void HandleTicket(string playerId, int entityId, string playerName, string args, ChatCommandSettings settings)
        {
            // /ticket <id> — view a specific ticket
            if (int.TryParse(args.Trim(), out var ticketId))
            {
                HandleTicketView(playerId, entityId, ticketId);
                return;
            }

            // /ticket <message> — create a new ticket
            if (string.IsNullOrWhiteSpace(args))
            {
                Reply(entityId, "Usage: /ticket <message> to create a ticket, or /ticket <id> to view one.");
                return;
            }

            if (!CheckCooldown(playerId, entityId, "ticket", settings.TicketCooldownSeconds))
                return;

            var ticketSettings = GetTicketSettings();
            var openCount = _ticketRepo.GetOpenCountByPlayerId(playerId);
            if (openCount >= ticketSettings.MaxOpenTicketsPerPlayer)
            {
                Reply(entityId, $"You already have {openCount} open ticket(s). Max is {ticketSettings.MaxOpenTicketsPerPlayer}.");
                return;
            }

            var subject = args.Length > 80 ? args.Substring(0, 80) : args;
            var ticket = new Ticket
            {
                PlayerId = playerId,
                PlayerName = playerName,
                Subject = subject,
                Status = "open",
                Priority = 1
            };

            var newId = _ticketRepo.Create(ticket);
            ticket.Id = newId;

            // Add the full message as the first ticket message
            var message = new TicketMessage
            {
                TicketId = newId,
                SenderType = "player",
                SenderId = playerId,
                SenderName = playerName,
                Message = args,
                Delivered = 1
            };
            _ticketRepo.AddMessage(message);

            _eventBus.Publish(new TicketCreatedEvent
            {
                TicketId = newId,
                PlayerId = playerId,
                PlayerName = playerName,
                Subject = subject
            });

            // Discord notification
            if (ticketSettings.DiscordNotifyOnCreate && !string.IsNullOrWhiteSpace(ticketSettings.DiscordWebhookUrl))
            {
                _discordService.SendTicketCreated(ticketSettings.DiscordWebhookUrl, ticket, args);
            }

            Reply(entityId, $"Ticket #{newId} created. An admin will respond soon.");
        }

        private void HandleTicketList(string playerId, int entityId)
        {
            var tickets = _ticketRepo.GetByPlayerId(playerId).ToList();
            if (tickets.Count == 0)
            {
                Reply(entityId, "You have no tickets. Use /ticket <message> to create one.");
                return;
            }

            var openTickets = tickets.Where(t => t.Status != "closed").ToList();
            if (openTickets.Count == 0)
            {
                Reply(entityId, $"You have {tickets.Count} ticket(s), all closed.");
                return;
            }

            var lines = openTickets.Select(t => $"#{t.Id} [{t.Status}] {t.Subject}");
            Reply(entityId, $"Your tickets: {string.Join(" | ", lines)}");
        }

        private void HandleTicketView(string playerId, int entityId, int ticketId)
        {
            var ticket = _ticketRepo.GetById(ticketId);
            if (ticket == null || ticket.PlayerId != playerId)
            {
                Reply(entityId, $"Ticket #{ticketId} not found.");
                return;
            }

            Reply(entityId, $"Ticket #{ticket.Id} [{ticket.Status}]: {ticket.Subject}");

            var messages = _ticketRepo.GetMessages(ticketId).ToList();
            var recent = messages.Skip(System.Math.Max(0, messages.Count - 3)).ToList();
            foreach (var msg in recent)
            {
                Reply(entityId, $"  [{msg.SenderType}] {msg.SenderName}: {msg.Message}");
            }

            if (messages.Count > 3)
                Reply(entityId, $"  ({messages.Count - 3} earlier message(s) not shown)");
        }

        private TicketSettings GetTicketSettings()
        {
            try
            {
                var json = _settingsRepo.Get("Ticket");
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<TicketSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch { /* fall through to defaults */ }

            return new TicketSettings();
        }

        // ─── Blood Moon Vote Commands ─────────────────────────────────

        private void HandleBloodMoonVote(string playerId, int entityId, string playerName)
        {
            var result = _bloodMoonVoteFeature.CastVote(playerId, playerName, entityId);
            var bmSettings = _bloodMoonVoteFeature.Settings;
            var status = _bloodMoonVoteFeature.GetVoteStatus();

            switch (result)
            {
                case VoteResult.Registered:
                    Reply(entityId, bmSettings.VoteRegisteredMessage
                        .Replace("{current}", status.CurrentVotes.ToString())
                        .Replace("{required}", status.RequiredVotes.ToString()));
                    break;
                case VoteResult.AlreadyVoted:
                    Reply(entityId, bmSettings.AlreadyVotedMessage);
                    break;
                case VoteResult.NotActive:
                    Reply(entityId, bmSettings.VoteNotActiveMessage);
                    break;
                case VoteResult.Passed:
                    Reply(entityId, bmSettings.VoteSuccessMessage);
                    break;
                case VoteResult.Disabled:
                    Reply(entityId, bmSettings.FeatureDisabledMessage);
                    break;
                case VoteResult.OnCooldown:
                    Reply(entityId, bmSettings.OnCooldownMessage);
                    break;
            }
        }

        // ─── Vote-Reward Commands ─────────────────────────────────────

        /// <summary>
        /// Player-initiated vote reward claim. Player typed "/vote" — we look
        /// up their Steam ID, run the claim against every enabled provider in
        /// the background, and post the result lines back to chat as each
        /// provider answers.
        ///
        /// We do NOT block the game thread on the network round-trip. The chat
        /// handler returns immediately; the background task marshals each reply
        /// back to the game thread via SdtdConsole.
        /// </summary>
        private void HandleVoteReward(string playerId, int entityId, string playerName, ChatCommandSettings settings)
        {
            if (!CheckCooldown(playerId, entityId, "vote", settings.VoteCooldownSeconds))
                return;

            // The listing-site APIs are keyed by raw SteamID64 (e.g. "76561198..."),
            // not the game's CrossplatformId form ("Steam_76561198..."). Pull the
            // raw value from the live player record's PlatformId, which carries
            // the Steam_-prefixed combined string.
            var online = _playerManager.GetByEntityId(entityId);
            var rawSteam = ExtractSteamId64(online?.PlatformId) ?? ExtractSteamId64(playerId);
            if (string.IsNullOrEmpty(rawSteam))
            {
                Reply(entityId, "Couldn't determine your Steam ID — vote rewards require a Steam-platform connection.");
                return;
            }

            Reply(entityId, "Checking for unclaimed votes...");

            // Fire-and-forget: do the network work off the game thread, then
            // marshal back to send replies. ConfigureAwait(false) is fine here —
            // we explicitly Post back to the main thread before any game API call.
            _ = Task.Run(async () =>
            {
                try
                {
                    var results = await _voteRewardsFeature
                        .TryClaimForPlayerAsync(rawSteam, playerName)
                        .ConfigureAwait(false);

                    ModEntry.MainThreadContext.Post(_ =>
                    {
                        if (results == null || results.Count == 0)
                        {
                            Reply(entityId, "No vote-reward providers are configured.");
                            return;
                        }

                        var grantedAny = false;
                        foreach (var r in results)
                        {
                            if (r.Outcome == VoteRewardsFeature.ClaimOutcome.Granted) grantedAny = true;
                            Reply(entityId, r.Message);
                        }

                        if (!grantedAny)
                        {
                            Reply(entityId, "No new rewards to grant. Try again after voting at the listed site(s).");
                        }
                    }, null);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KitsuneCommand] /vote handler error: {ex.Message}");
                    ModEntry.MainThreadContext.Post(_ => Reply(entityId, "Vote check failed — try again in a minute."), null);
                }
            });
        }

        /// <summary>
        /// Extracts the raw 76-digit SteamID64 from a CrossplatformId-formatted
        /// string like "Steam_76561198XXXXXXXX". Returns null for non-Steam
        /// identities (Epic-only voters aren't supported by the v1 providers).
        /// </summary>
        private static string ExtractSteamId64(string crossplatformId)
        {
            if (string.IsNullOrEmpty(crossplatformId)) return null;
            const string prefix = "Steam_";
            if (crossplatformId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && crossplatformId.Length > prefix.Length)
            {
                return crossplatformId.Substring(prefix.Length);
            }
            // Some game versions hand us the bare 17-digit ID. Accept it as-is.
            if (crossplatformId.Length == 17 && long.TryParse(crossplatformId, out _))
                return crossplatformId;
            return null;
        }

        // ─── Help ──────────────────────────────────────────────────────

        /// <summary>
        /// Lists the commands available to the calling player. Only enabled
        /// feature groups are shown — running `/help` on a server with the
        /// store turned off shouldn't tell the player to try `/buy`.
        ///
        /// Each Reply() is a separate `pm` to the player's chat, so we can
        /// safely emit one line per group without worrying about message
        /// length truncation.
        /// </summary>
        private void HandleHelp(int entityId, ChatCommandSettings settings)
        {
            var p = settings.Prefix;
            Reply(entityId, "Available commands:");

            var anyEnabled = false;

            if (settings.HomeEnabled)
            {
                Reply(entityId, $"  HOME: {p}home [name], {p}sethome <name>, {p}delhome <name>, {p}homes");
                anyEnabled = true;
            }
            if (settings.TeleportEnabled)
            {
                Reply(entityId, $"  TELEPORT: {p}tp <city>, {p}cities");
                anyEnabled = true;
            }
            if (settings.PointsEnabled)
            {
                Reply(entityId, $"  POINTS: {p}points, {p}signin");
                anyEnabled = true;
            }
            if (settings.StoreEnabled)
            {
                Reply(entityId, $"  STORE: {p}shop, {p}buy <item>");
                anyEnabled = true;
            }
            if (settings.VipEnabled)
            {
                Reply(entityId, $"  VIP: {p}vip");
                anyEnabled = true;
            }
            if (settings.TicketEnabled)
            {
                Reply(entityId, $"  TICKETS: {p}ticket <message>, {p}ticket <id>, {p}tickets");
                anyEnabled = true;
            }

            // Blood Moon Vote has its own enable check inside the feature
            // (rather than a settings.BloodMoonVoteEnabled flag here), so we
            // surface it unconditionally; the feature itself replies with a
            // "disabled" message if a player tries to vote while it's off.
            Reply(entityId, $"  BLOOD MOON: {p}skipbm (alias: {p}voteskip)");

            if (settings.VoteEnabled)
            {
                Reply(entityId, $"  VOTE REWARDS: {p}vote");
                anyEnabled = true;
            }

            if (!anyEnabled)
            {
                Reply(entityId, "(All optional feature groups are currently disabled on this server.)");
            }
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
        /// Reads the persisted PointsSettings from the database.
        /// Falls back to defaults if not found or on error.
        /// </summary>
        private PointsSettings GetPointsSettings()
        {
            try
            {
                var json = _settingsRepo.Get("Points");
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<PointsSettings>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch { /* fall through to defaults */ }

            return new PointsSettings();
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
