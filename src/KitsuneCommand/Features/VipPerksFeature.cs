using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using Newtonsoft.Json;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// VIP perks driven off player spawn (board #233, #234):
    ///
    ///   • #233 — first-time-login pack. The first time a player ever spawns, the
    ///     configured VIP-gift template is delivered straight into their inventory
    ///     (items dropped at their feet, linked commands run). One-shot, guarded by
    ///     the first_login_grants table so it never repeats.
    ///
    ///   • #234 — recurring tier gifts. Players carry an admin-assigned VIP tier
    ///     (player_metadata.vip_tier). On spawn, each tier-gift rule for that tier
    ///     is materialized once as a repeatable vip_gifts row; the existing /vip
    ///     claim flow then drives the daily/weekly cadence. "Plebs" (no tier) get
    ///     nothing.
    ///
    /// Spawn-driven rather than timer-driven: a player must be online to claim a
    /// gift or receive items anyway, so doing the work on connect avoids a
    /// background sweep entirely while staying idempotent across reconnects.
    /// </summary>
    public class VipPerksFeature : FeatureBase<VipPerksSettings>
    {
        private readonly ISettingsRepository _settingsRepo;
        private readonly IVipGiftRepository _vipGiftRepo;
        private readonly IFirstLoginGrantRepository _firstLoginRepo;
        private readonly IPlayerMetadataRepository _metadataRepo;
        private readonly LivePlayerManager _playerManager;
        private const string SettingsKey = "VipPerks";

        // Items are dropped via the game API which needs the player entity to be
        // fully spawned. PlayerSpawnedEvent can fire a beat before the EntityPlayer
        // is ready, so we wait briefly before delivering the first-login pack.
        private const int FirstLoginDeliveryDelayMs = 3000;

        private static readonly HashSet<string> ValidPeriods =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "daily", "weekly", "monthly" };

        public VipPerksFeature(
            ModEventBus eventBus,
            ConfigManager config,
            ISettingsRepository settingsRepo,
            IVipGiftRepository vipGiftRepo,
            IFirstLoginGrantRepository firstLoginRepo,
            IPlayerMetadataRepository metadataRepo,
            LivePlayerManager playerManager)
            : base(eventBus, config)
        {
            _settingsRepo = settingsRepo;
            _vipGiftRepo = vipGiftRepo;
            _firstLoginRepo = firstLoginRepo;
            _metadataRepo = metadataRepo;
            _playerManager = playerManager;
        }

        protected override void OnEnable()
        {
            LoadPersistedSettings();
            EventBus.Subscribe<PlayerSpawnedEvent>(OnPlayerSpawned);
            Log.Out($"[KitsuneCommand] VipPerks feature enabled. Master={Settings.Enabled}, "
                  + $"tiers={Settings.Tiers.Count}, firstLoginPack={Settings.FirstLoginPackEnabled}, "
                  + $"tierGiftRules={Settings.TierGifts.Count}.");
        }

        protected override void OnDisable()
        {
            EventBus.Unsubscribe<PlayerSpawnedEvent>(OnPlayerSpawned);
            Log.Out("[KitsuneCommand] VipPerks feature disabled.");
        }

        /// <summary>
        /// Replaces settings in memory and persists them. No restart needed —
        /// the next spawn sees the new config.
        /// </summary>
        public void UpdateSettings(VipPerksSettings newSettings)
        {
            Settings = newSettings ?? new VipPerksSettings();
            if (Settings.Tiers == null) Settings.Tiers = new List<string>();
            if (Settings.TierGifts == null) Settings.TierGifts = new List<TierGiftRule>();

            try
            {
                _settingsRepo.Set(SettingsKey, JsonConvert.SerializeObject(Settings));
                Log.Out($"[KitsuneCommand] VipPerks settings updated. Master={Settings.Enabled}.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to persist VipPerks settings: {ex.Message}");
            }
        }

        // ─── Spawn handler ────────────────────────────────────────────────

        private void OnPlayerSpawned(PlayerSpawnedEvent e)
        {
            if (!Settings.Enabled) return;
            if (string.IsNullOrEmpty(e.PlayerId)) return;

            // Only act on "joined the server" spawns. Death and teleport republish
            // PlayerSpawnedEvent too, and we don't want to re-run perk logic every
            // time a VIP dies. NewGame / LoadedGame / Enter / JoinMultiplayer all
            // represent a (re)connect; Died and Teleport do not.
            // Fully qualified: the game also defines a global `RespawnType`, so the
            // bare name would bind to the wrong enum and fail to compare with the
            // Abstractions one carried on the event.
            if (e.RespawnType == Abstractions.Models.RespawnType.Died
             || e.RespawnType == Abstractions.Models.RespawnType.Teleport)
                return;

            // Offload off the publishing thread. DB work runs immediately; item
            // delivery waits for the entity to settle. Errors are swallowed per
            // concern so one failure can't starve the other.
            _ = Task.Run(async () =>
            {
                try { AssignTierGifts(e.PlayerId, e.PlayerName); }
                catch (Exception ex) { Log.Warning($"[KitsuneCommand] VipPerks: tier-gift assign failed for {e.PlayerName}: {ex.Message}"); }

                try { await MaybeDeliverFirstLoginPackAsync(e).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning($"[KitsuneCommand] VipPerks: first-login pack failed for {e.PlayerName}: {ex.Message}"); }
            });
        }

        // ─── #233 first-login pack ────────────────────────────────────────

        private async Task MaybeDeliverFirstLoginPackAsync(PlayerSpawnedEvent e)
        {
            if (!Settings.FirstLoginPackEnabled) return;
            if (string.IsNullOrWhiteSpace(Settings.FirstLoginTemplateName)) return;

            // Cheap pre-check so we don't resolve the template for the 99% case of
            // a returning player who already has their pack.
            if (_firstLoginRepo.HasGrant(e.PlayerId)) return;

            // Resolve the template BEFORE claiming the once-ever slot. If the admin
            // typo'd the template name, we must not burn the player's first-login
            // grant on an empty delivery — leave it unclaimed so a fixed config
            // still works on their next connect.
            var template = _vipGiftRepo.GetTemplateByName(Settings.FirstLoginTemplateName);
            if (template == null)
            {
                Log.Warning($"[KitsuneCommand] VipPerks: first-login template '{Settings.FirstLoginTemplateName}' "
                          + $"not found (player_id = '{VipGiftSentinels.TemplatePlayerId}'). Skipping pack for {e.PlayerName}.");
                return;
            }

            // Atomically claim the slot. If another spawn beat us to it, stop.
            if (!_firstLoginRepo.TryClaim(e.PlayerId, e.PlayerName))
                return;

            var items = _vipGiftRepo.GetItemsForGift(template.Id).ToList();
            var commands = _vipGiftRepo.GetCommandsForGift(template.Id).ToList();

            // Give the entity a moment to finish spawning before dropping items.
            await Task.Delay(FirstLoginDeliveryDelayMs).ConfigureAwait(false);

            foreach (var item in items)
            {
                try
                {
                    var result = _playerManager.GiveItemToPlayer(e.EntityId, item.ItemName, item.Count, item.Quality);
                    Log.Out($"[KitsuneCommand] VipPerks: first-login item {item.ItemName} x{item.Count} → {e.PlayerName}: {result?.Message}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KitsuneCommand] VipPerks: failed to give {item.ItemName} to {e.PlayerName}: {ex.Message}");
                }
            }

            foreach (var cmdDef in commands)
            {
                var cmd = cmdDef.Command
                    .Replace("{entityId}", e.EntityId.ToString())
                    .Replace("{playerId}", e.PlayerId)
                    .Replace("{playerName}", e.PlayerName ?? "");
                ExecuteConsoleCommand(cmd);
            }

            if (!string.IsNullOrWhiteSpace(Settings.FirstLoginMessage) && !string.IsNullOrEmpty(e.PlayerName))
            {
                var msg = Settings.FirstLoginMessage.Replace("{player}", e.PlayerName).Replace("\"", "'");
                ExecuteConsoleCommand($"pm \"{e.PlayerName}\" \"{msg}\"");
            }

            Log.Out($"[KitsuneCommand] VipPerks: delivered first-login pack '{template.Name}' "
                  + $"({items.Count} item(s), {commands.Count} command(s)) to {e.PlayerName}.");
        }

        // ─── #234 recurring tier gifts ────────────────────────────────────

        private void AssignTierGifts(string playerId, string playerName)
        {
            if (Settings.TierGifts == null || Settings.TierGifts.Count == 0) return;

            var tier = _metadataRepo.GetByPlayerId(playerId)?.VipTier;
            if (string.IsNullOrWhiteSpace(tier)) return; // pleb — no tier gifts

            // Ignore a tier that's no longer in the configured list (e.g. admin
            // renamed/removed it but a stale assignment lingers on a player).
            if (!Settings.Tiers.Any(t => string.Equals(t, tier, StringComparison.OrdinalIgnoreCase)))
                return;

            foreach (var rule in Settings.TierGifts)
            {
                if (!string.Equals(rule.Tier, tier, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrWhiteSpace(rule.TemplateName)) continue;

                var period = (rule.Period ?? "").Trim().ToLowerInvariant();
                if (!ValidPeriods.Contains(period))
                {
                    Log.Warning($"[KitsuneCommand] VipPerks: tier '{rule.Tier}' rule has invalid period '{rule.Period}' — skipping.");
                    continue;
                }

                var template = _vipGiftRepo.GetTemplateByName(rule.TemplateName);
                if (template == null)
                {
                    Log.Warning($"[KitsuneCommand] VipPerks: tier-gift template '{rule.TemplateName}' not found — skipping.");
                    continue;
                }

                // Assign once. After that, GetPendingForPlayer + /vip drive re-claims.
                if (_vipGiftRepo.HasGiftByNameAndPeriod(playerId, template.Name, period))
                    continue;

                CloneTemplateForPlayer(template, playerId, playerName, period);
                Log.Out($"[KitsuneCommand] VipPerks: assigned {period} gift '{template.Name}' to {playerName} (tier {tier}).");
            }
        }

        /// <summary>
        /// Clones a template's items + commands into a new repeatable vip_gifts row
        /// for the player. Mirrors VoteRewardsFeature's VIP-gift clone path.
        /// </summary>
        private void CloneTemplateForPlayer(VipGift template, string playerId, string playerName, string period)
        {
            var templateItems = _vipGiftRepo.GetItemsForGift(template.Id).Select(i => i.Id).ToList();
            var templateCommands = _vipGiftRepo.GetCommandsForGift(template.Id).Select(c => c.Id).ToList();

            var gift = new VipGift
            {
                PlayerId = playerId,
                PlayerName = playerName,
                Name = template.Name,
                Description = string.IsNullOrWhiteSpace(template.Description)
                    ? $"VIP {period} gift"
                    : template.Description,
                ClaimPeriod = period
            };
            var newId = _vipGiftRepo.Insert(gift);
            _vipGiftRepo.SetGiftItems(newId, templateItems);
            _vipGiftRepo.SetGiftCommands(newId, templateCommands);
        }

        // ─── Bootstrap / helpers ──────────────────────────────────────────

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    var loaded = JsonConvert.DeserializeObject<VipPerksSettings>(json);
                    if (loaded != null) Settings = loaded;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load VipPerks settings, using defaults: {ex.Message}");
            }

            if (Settings.Tiers == null) Settings.Tiers = new List<string>();
            if (Settings.TierGifts == null) Settings.TierGifts = new List<TierGiftRule>();
        }

        /// <summary>
        /// Executes a console command on the main thread. Fire-and-forget from a
        /// background thread; same marshaling pattern as VipGiftController / TaskScheduleFeature.
        /// </summary>
        private static void ExecuteConsoleCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            var waitHandle = new ManualResetEventSlim(false);
            ModEntry.MainThreadContext.Post(_ =>
            {
                try { SdtdConsole.Instance.ExecuteSync(command, null); }
                catch (Exception ex) { Log.Warning($"[KitsuneCommand] VipPerks: command '{command}' failed: {ex.Message}"); }
                finally { waitHandle.Set(); }
            }, null);
            waitHandle.Wait(TimeSpan.FromSeconds(10));
        }
    }
}
