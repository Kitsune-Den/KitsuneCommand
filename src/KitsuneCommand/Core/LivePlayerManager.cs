using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Features.Suspicion;
using KitsuneCommand.Web.Models;
using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Core
{
    /// <summary>
    /// Maintains an in-memory dictionary of currently online players.
    /// Subscribes to game events and reads player data from the game API on the main thread.
    /// </summary>
    public class LivePlayerManager
    {
        private readonly ConcurrentDictionary<int, PlayerInfo> _players
            = new ConcurrentDictionary<int, PlayerInfo>();
        private readonly ConcurrentDictionary<string, PlayerInfo> _pendingLogins
            = new ConcurrentDictionary<string, PlayerInfo>();
        private readonly ModEventBus _eventBus;
        private Timer _positionTimer;

        public LivePlayerManager(ModEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Initialize()
        {
            _eventBus.Subscribe<PlayerLoginEvent>(OnPlayerLogin);
            _eventBus.Subscribe<PlayerDisconnectedEvent>(OnPlayerDisconnected);
            _eventBus.Subscribe<PlayerSpawnedEvent>(OnPlayerSpawned);

            // Scan for any players already online (e.g., if mod reloaded mid-game)
            ScanExistingPlayers();

            // Start periodic position broadcasting (every 3 seconds)
            _positionTimer = new Timer(BroadcastPositions, null, 3000, 3000);

            Log.Out($"[KitsuneCommand] LivePlayerManager initialized. {_players.Count} player(s) online.");
        }

        public IEnumerable<PlayerInfo> GetAllOnline()
        {
            return _players.Values.ToList();
        }

        public PlayerInfo GetByEntityId(int entityId)
        {
            _players.TryGetValue(entityId, out var player);
            return player;
        }

        public int OnlineCount => _players.Count;

        /// <summary>
        /// Reads full player detail (inventory + skills) from the game API on the main thread.
        /// </summary>
        public PlayerDetailInfo GetPlayerDetail(int entityId)
        {
            return RunOnMainThread(() =>
            {
                var ep = GetEntityPlayer(entityId);
                if (ep == null) return null;

                var detail = new PlayerDetailInfo();
                PopulatePlayerInfo(detail, ep);
                PopulateInventory(detail, ep);
                detail.Skills = ReadSkills(ep);
                return detail;
            });
        }

        /// <summary>
        /// Reads player inventory from the game API on the main thread.
        /// </summary>
        public (List<InventorySlot> Bag, List<InventorySlot> Belt) GetPlayerInventory(int entityId)
        {
            return RunOnMainThread(() =>
            {
                var ep = GetEntityPlayer(entityId);
                if (ep == null) return (new List<InventorySlot>(), new List<InventorySlot>());

                var bag = ReadInventorySlots(GameCompat.GetSlots(ep.bag), "bag");
                var belt = ReadInventorySlots(GameCompat.GetSlots(ep.inventory), "belt");
                return (bag, belt);
            });
        }

        /// <summary>
        /// Reads player skills/perks from the game API on the main thread.
        /// </summary>
        public List<PlayerSkillInfo> GetPlayerSkills(int entityId)
        {
            return RunOnMainThread(() =>
            {
                var ep = GetEntityPlayer(entityId);
                if (ep == null) return new List<PlayerSkillInfo>();
                return ReadSkills(ep);
            });
        }

        // --- Event Handlers ---

        private void OnPlayerLogin(PlayerLoginEvent e)
        {
            // At login time, entityId may be -1 (not yet assigned).
            // Store a minimal entry keyed by playerId so we can match it at spawn time.
            _pendingLogins[e.PlayerId] = new PlayerInfo
            {
                PlayerId = e.PlayerId,
                PlayerName = e.PlayerName,
                EntityId = e.EntityId,
                PlatformId = e.PlatformId,
                Ip = e.Ip,
                IsOnline = true
            };
            Log.Out($"[KitsuneCommand] Player login pending: {e.PlayerName} (awaiting spawn)");
        }

        private void OnPlayerDisconnected(PlayerDisconnectedEvent e)
        {
            _pendingLogins.TryRemove(e.PlayerId, out _);

            if (_players.TryRemove(e.EntityId, out var removed))
            {
                Log.Out($"[KitsuneCommand] Player untracked: {removed.PlayerName} (entity {e.EntityId})");
            }
        }

        private void OnPlayerSpawned(PlayerSpawnedEvent e)
        {
            // If already tracked with full data, just update position
            if (_players.TryGetValue(e.EntityId, out var existing) && existing.Level > 0)
            {
                existing.PositionX = e.PositionX;
                existing.PositionY = e.PositionY;
                existing.PositionZ = e.PositionZ;
                return;
            }

            // Try to fully populate the player now that the entity is ready
            var info = RunOnMainThread(() =>
            {
                var ep = GetEntityPlayer(e.EntityId);
                if (ep == null) return (PlayerInfo)null;

                var pi = new PlayerInfo();
                PopulatePlayerInfo(pi, ep);
                return pi;
            });

            if (info != null)
            {
                // Carry over IP from pending login if available
                if (_pendingLogins.TryRemove(e.PlayerId, out var pending))
                {
                    info.Ip = pending.Ip;
                }

                // Remove any stale entry keyed by -1 from the login fallback
                _players.TryRemove(-1, out _);

                _players[e.EntityId] = info;
                Log.Out($"[KitsuneCommand] Player tracked: {info.PlayerName} (entity {e.EntityId})");
            }
            else
            {
                // Entity still not ready — use event data as fallback
                _pendingLogins.TryRemove(e.PlayerId, out var pending);
                var fallback = pending ?? new PlayerInfo
                {
                    PlayerId = e.PlayerId,
                    PlayerName = e.PlayerName,
                    IsOnline = true
                };
                fallback.EntityId = e.EntityId;
                fallback.PositionX = e.PositionX;
                fallback.PositionY = e.PositionY;
                fallback.PositionZ = e.PositionZ;

                _players.TryRemove(-1, out _);
                _players[e.EntityId] = fallback;
                Log.Warning($"[KitsuneCommand] Player {e.PlayerName} tracked with minimal data at spawn (entity {e.EntityId}).");
            }
        }

        // --- Periodic Position Broadcasting ---

        private void BroadcastPositions(object _)
        {
            if (!ModEntry.IsGameStartDone) return;

            try
            {
                var samples = new List<PlayerSample>();
                var updates = RunOnMainThread(() =>
                {
                    var result = new List<PlayerPositionData>();
                    var world = GameManager.Instance?.World;
                    if (world?.Players == null) return result;
                    var now = DateTime.UtcNow;

                    foreach (var kvp in world.Players.dict)
                    {
                        var ep = kvp.Value;
                        if (ep == null) continue;
                        var pos = ep.GetPosition();
                        result.Add(new PlayerPositionData
                        {
                            EntityId = ep.entityId,
                            PlayerName = ep.EntityName,
                            X = pos.x,
                            Y = pos.y,
                            Z = pos.z
                        });
                        // The suspicion log's extra reads must never cost the map its positions.
                        try { samples.Add(BuildSample(ep, world, now)); } catch { /* skip this player's sample */ }

                        // Update cached positions
                        if (_players.TryGetValue(ep.entityId, out var cached))
                        {
                            cached.PositionX = pos.x;
                            cached.PositionY = pos.y;
                            cached.PositionZ = pos.z;
                            cached.Health = ep.Health;
                            cached.Stamina = ep.Stamina;
                        }
                    }
                    return result;
                }, timeoutMs: 2000);

                if (updates != null && updates.Count > 0)
                {
                    _eventBus.Publish(new PlayersPositionUpdateEvent { Players = updates });
                    _eventBus.Publish(new PlayerSamplesEvent { Samples = samples });
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Position broadcast error: {ex.Message}");
            }
        }

        /// <summary>
        /// One player's position, vehicle and progression state, read on the main
        /// thread (inline when already on it, e.g. from a SavePlayerData handler).
        /// </summary>
        public PlayerSample GetPlayerSample(int entityId)
        {
            return RunOnMainThread(() =>
            {
                var ep = GetEntityPlayer(entityId);
                var world = GameManager.Instance?.World;
                return ep == null || world == null ? null : BuildSample(ep, world, DateTime.UtcNow);
            }, timeoutMs: 2000);
        }

        // --- Helpers ---

        private static PlayerSample BuildSample(EntityPlayer ep, World world, DateTime now)
        {
            var pos = ep.GetPosition();
            // GetHeight is the top block of the column; 0 means the chunk isn't loaded.
            var ground = world.GetHeight((int)Math.Floor(pos.x), (int)Math.Floor(pos.z));
            var progression = ep.Progression;
            return new PlayerSample
            {
                EntityId = ep.entityId,
                At = now,
                X = pos.x,
                Y = pos.y,
                Z = pos.z,
                Attached = ep.AttachedToEntity != null,
                GroundHeight = ground > 0 ? ground : (double?)null,
                Level = progression?.Level,
                SkillPoints = progression?.SkillPoints
            };
        }

        private void ScanExistingPlayers()
        {
            RunOnMainThread(() =>
            {
                var world = GameManager.Instance?.World;
                if (world?.Players == null) return 0;

                foreach (var kvp in world.Players.dict)
                {
                    var ep = kvp.Value;
                    if (ep == null) continue;

                    var info = new PlayerInfo();
                    PopulatePlayerInfo(info, ep);
                    _players[ep.entityId] = info;
                }
                return _players.Count;
            });
        }

        private static EntityPlayer GetEntityPlayer(int entityId)
        {
            var world = GameManager.Instance?.World;
            if (world?.Players == null) return null;
            world.Players.dict.TryGetValue(entityId, out var ep);
            return ep;
        }

        private static void PopulatePlayerInfo(PlayerInfo info, EntityPlayer ep)
        {
            var pos = ep.GetPosition();
            var clientInfo = ConnectionManager.Instance?.Clients?.ForEntityId(ep.entityId);

            info.EntityId = ep.entityId;
            info.PlayerName = ep.EntityName;
            info.PlayerId = clientInfo?.CrossplatformId?.CombinedString;
            info.PlatformId = clientInfo?.PlatformId?.CombinedString;
            info.PositionX = pos.x;
            info.PositionY = pos.y;
            info.PositionZ = pos.z;
            info.Level = ep.Progression?.Level ?? 0;
            info.Health = ep.Health;
            info.Stamina = ep.Stamina;
            info.ZombieKills = ep.KilledZombies;
            info.PlayerKills = ep.KilledPlayers;
            info.Deaths = ep.Died;
            info.TotalPlayTime = ep.totalTimePlayed;
            info.Score = ep.Score;
            info.IsOnline = true;
            info.Ip = clientInfo?.ip;
            var permLevel = GameManager.Instance?.adminTools?.Users?.GetUserPermissionLevel(clientInfo?.InternalId) ?? 1000;
            info.IsAdmin = permLevel == 0;
            info.AdminLevel = permLevel;
        }

        /// <summary>
        /// Give an item directly to a player's inventory via the game API.
        /// Returns a result message indicating success or failure.
        /// </summary>
        public GiveItemResult GiveItemToPlayer(int entityId, string itemName, int count, int quality)
        {
            return RunOnMainThread(() =>
            {
                var ep = GetEntityPlayer(entityId);
                if (ep == null)
                    return new GiveItemResult { Success = false, Message = "Player not found or not online." };

                // Look up the item class by internal name
                var itemClass = ItemClass.GetItemClass(itemName, true);
                if (itemClass == null)
                    return new GiveItemResult { Success = false, Message = $"Unknown item: {itemName}" };

                // Create the item value
                var itemValue = new ItemValue(itemClass.Id);
                if (itemClass.HasQuality)
                    GameCompat.SetQuality(itemValue, (ushort)Math.Max(1, Math.Min(quality, 6)));

                // Check bag + belt for available space
                var bagSlots = GameCompat.GetSlots(ep.bag);
                var beltSlots = GameCompat.GetSlots(ep.inventory);
                int maxStack = itemClass.Stacknumber.Value;
                int freeSpace = CountFreeSpace(bagSlots, GameCompat.GetItemType(itemValue), maxStack)
                              + CountFreeSpace(beltSlots, GameCompat.GetItemType(itemValue), maxStack);

                if (freeSpace < count)
                {
                    // Notify the player their inventory is full
                    var fullDisplayName = GameCompat.Localize(itemName);
                    try
                    {
                        GameChat.Whisper(entityId,
                            $"[FF6600]An admin tried to give you {count}x {fullDisplayName}, but your inventory is full![-]");
                    }
                    catch { }

                    return new GiveItemResult
                    {
                        Success = false,
                        Message = $"Not enough inventory space. Available room: {freeSpace}, requested: {count}."
                    };
                }

                var displayName = GameCompat.Localize(itemName);
                var stack = new ItemStack(itemValue, count);

                // Drop item right at the player's position — auto-pickup will collect it
                var pos = ep.GetPosition();
                pos.y += 0.25f; // Slightly above feet so it doesn't clip through ground
                GameManager.Instance.ItemDropServer(stack, pos, UnityEngine.Vector3.zero, entityId, 60f, false);
                Log.Out($"[KitsuneCommand] Dropped {count}x {itemName} at ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}) for {ep.EntityName}");

                // Send DM to player
                try
                {
                    var msg = $"An admin sent you {count}x {displayName} — check your feet!";
                    GameChat.Whisper(entityId, $"[00FF00]{msg}[-]");
                }
                catch { /* Non-critical */ }

                return new GiveItemResult { Success = true, Message = $"Gave {count}x {displayName} to {ep.EntityName}." };
            });
        }

        private static int CountFreeSpace(ItemStack[] slots, int itemType, int maxStack)
        {
            int space = 0;
            if (slots == null) return space;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.IsEmpty())
                {
                    space += maxStack;
                    continue;
                }

                int slotCount = GameCompat.GetCount(slot);
                if (GameCompat.GetItemType(GameCompat.GetItemValue(slot)) == itemType && slotCount < maxStack)
                {
                    space += maxStack - slotCount;
                }
            }
            return space;
        }

        private static void PopulateInventory(PlayerDetailInfo detail, EntityPlayer ep)
        {
            detail.BagItems = ReadInventorySlots(GameCompat.GetSlots(ep.bag), "bag");
            detail.BeltItems = ReadInventorySlots(GameCompat.GetSlots(ep.inventory), "belt");
        }

        private static List<InventorySlot> ReadInventorySlots(ItemStack[] items, string type)
        {
            var slots = new List<InventorySlot>();
            if (items == null) return slots;
            for (int i = 0; i < items.Length; i++)
            {
                var stack = items[i];
                if (stack == null || stack.IsEmpty()) continue;

                var itemValue = GameCompat.GetItemValue(stack);
                var slotItemName = itemValue.ItemClass?.GetItemName() ?? "unknown";
                var slotDisplayName = slotItemName;
                try { slotDisplayName = GameCompat.Localize(slotItemName); } catch { }

                slots.Add(new InventorySlot
                {
                    SlotIndex = i,
                    ItemName = slotItemName,
                    DisplayName = slotDisplayName,
                    Count = GameCompat.GetCount(stack),
                    Quality = GameCompat.GetQuality(itemValue),
                    Durability = GameCompat.GetUseTimes(itemValue),
                    MaxDurability = itemValue.MaxUseTimes,
                    IconName = itemValue.ItemClass?.GetIconName() ?? ""
                });
            }
            return slots;
        }

        private static List<PlayerSkillInfo> ReadSkills(EntityPlayer ep)
        {
            var skills = new List<PlayerSkillInfo>();
            var progression = ep.Progression;
            if (progression == null) return skills;

            foreach (var kvp in progression.ProgressionValues.Dict)
            {
                var pv = kvp.Value;
                if (pv?.ProgressionClass == null) continue;

                skills.Add(new PlayerSkillInfo
                {
                    Name = pv.ProgressionClass.Name,
                    Level = pv.Level,
                    MaxLevel = pv.ProgressionClass.MaxLevel,
                    IsLocked = pv.Level == 0 && pv.ProgressionClass.ParentName != null
                });
            }
            return skills;
        }

        /// <summary>
        /// Executes an action on the main Unity thread and waits for the result.
        /// </summary>
        private T RunOnMainThread<T>(Func<T> action, int timeoutMs = 5000)
        {
            if (SynchronizationContext.Current == ModEntry.MainThreadContext)
            {
                return action();
            }

            T result = default;
            Exception caught = null;
            var waitHandle = new ManualResetEventSlim(false);

            ModEntry.MainThreadContext.Post(_ =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                finally
                {
                    waitHandle.Set();
                }
            }, null);

            if (!waitHandle.Wait(timeoutMs))
            {
                Log.Warning("[KitsuneCommand] Main thread operation timed out.");
                return default;
            }

            if (caught != null)
                throw caught;

            return result;
        }

        public void Shutdown()
        {
            _positionTimer?.Dispose();
            _positionTimer = null;
            _players.Clear();
        }
    }
}
