using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Web.Models;

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

                var bag = ReadInventorySlots(ep.bag.GetSlots(), "bag");
                var belt = ReadInventorySlots(ep.inventory.GetSlots(), "belt");
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
            // The EntityPlayer may not be fully initialized yet, retry a few times
            Task.Run(async () =>
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    await Task.Delay(500 * (attempt + 1));

                    var info = RunOnMainThread(() =>
                    {
                        var ep = GetEntityPlayer(e.EntityId);
                        if (ep == null) return null;

                        var pi = new PlayerInfo();
                        PopulatePlayerInfo(pi, ep);
                        pi.Ip = e.Ip;
                        return pi;
                    });

                    if (info != null)
                    {
                        _players[e.EntityId] = info;
                        Log.Out($"[KitsuneCommand] Player tracked: {info.PlayerName} (entity {e.EntityId})");
                        return;
                    }
                }

                // Fallback: create a minimal entry from the event data
                _players[e.EntityId] = new PlayerInfo
                {
                    PlayerId = e.PlayerId,
                    PlayerName = e.PlayerName,
                    EntityId = e.EntityId,
                    PlatformId = e.PlatformId,
                    Ip = e.Ip,
                    IsOnline = true
                };
                Log.Warning($"[KitsuneCommand] Player {e.PlayerName} tracked with minimal data (entity not ready).");
            });
        }

        private void OnPlayerDisconnected(PlayerDisconnectedEvent e)
        {
            if (_players.TryRemove(e.EntityId, out var removed))
            {
                Log.Out($"[KitsuneCommand] Player untracked: {removed.PlayerName} (entity {e.EntityId})");
            }
        }

        private void OnPlayerSpawned(PlayerSpawnedEvent e)
        {
            if (_players.TryGetValue(e.EntityId, out var player))
            {
                player.PositionX = e.PositionX;
                player.PositionY = e.PositionY;
                player.PositionZ = e.PositionZ;
            }
        }

        // --- Periodic Position Broadcasting ---

        private void BroadcastPositions(object _)
        {
            if (!ModEntry.IsGameStartDone) return;

            try
            {
                var updates = RunOnMainThread(() =>
                {
                    var result = new List<PlayerPositionData>();
                    var world = GameManager.Instance?.World;
                    if (world?.Players == null) return result;

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
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Position broadcast error: {ex.Message}");
            }
        }

        // --- Helpers ---

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
            info.IsAdmin = GameManager.Instance?.adminTools?.Users?.GetUserPermissionLevel(clientInfo?.InternalId) == 0;
        }

        private static void PopulateInventory(PlayerDetailInfo detail, EntityPlayer ep)
        {
            detail.BagItems = ReadInventorySlots(ep.bag.GetSlots(), "bag");
            detail.BeltItems = ReadInventorySlots(ep.inventory.GetSlots(), "belt");
        }

        private static List<InventorySlot> ReadInventorySlots(ItemStack[] items, string type)
        {
            var slots = new List<InventorySlot>();
            if (items == null) return slots;
            for (int i = 0; i < items.Length; i++)
            {
                var stack = items[i];
                if (stack.IsEmpty()) continue;

                slots.Add(new InventorySlot
                {
                    SlotIndex = i,
                    ItemName = stack.itemValue.ItemClass?.GetItemName() ?? "unknown",
                    Count = stack.count,
                    Quality = (int)stack.itemValue.Quality,
                    Durability = stack.itemValue.UseTimes,
                    MaxDurability = stack.itemValue.MaxUseTimes,
                    IconName = stack.itemValue.ItemClass?.GetIconName() ?? ""
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
