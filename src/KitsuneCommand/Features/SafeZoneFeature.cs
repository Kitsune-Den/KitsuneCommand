using System.Collections.Concurrent;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Features
{
    /// <summary>
    /// Safe zones: admin-drawn rectangles on the map that zombies can't stay in.
    /// Every few seconds the main thread walks the world's entities once and
    /// despawns any living enemy standing inside an enabled zone, the same way
    /// the game despawns zombies it no longer needs (no loot, no kill credit).
    ///
    /// Despawning rather than blocking spawns covers everything in one place:
    /// wanderers walking in, screamer hordes, POI sleepers waking up, and blood
    /// moon hordes (unless a zone lets those through).
    /// </summary>
    public class SafeZoneFeature : FeatureBase<SafeZoneSettings>
    {
        private const string SettingsKey = "SafeZones";
        public const int MaxZones = 100;
        public const int MaxCoordinate = 20000;
        public const int MaxNameLength = 64;

        private readonly ISettingsRepository _settingsRepo;
        private readonly ConcurrentDictionary<string, int> _removedByZone = new ConcurrentDictionary<string, int>();
        private Timer _sweepTimer;
        private int _sweepQueued;

        public SafeZoneFeature(ModEventBus eventBus, ConfigManager config, ISettingsRepository settingsRepo)
            : base(eventBus, config)
        {
            _settingsRepo = settingsRepo;
        }

        protected override void OnEnable()
        {
            LoadPersistedSettings();
            RestartTimer();
            Log.Out($"[KitsuneCommand] Safe zones enabled={Settings.Enabled}, {Settings.Zones.Count} zone(s), " +
                    $"sweep every {Settings.SweepIntervalSeconds}s.");
        }

        protected override void OnDisable()
        {
            _sweepTimer?.Dispose();
            _sweepTimer = null;
        }

        public void UpdateSettings(SafeZoneSettings newSettings)
        {
            Settings = Normalize(newSettings);
            _settingsRepo.Set(SettingsKey, JsonConvert.SerializeObject(Settings));
            if (IsRunning) RestartTimer();

            // Forget counts for zones that were deleted.
            var ids = new HashSet<string>(Settings.Zones.Select(z => z.Id));
            foreach (var id in _removedByZone.Keys.Where(k => !ids.Contains(k)).ToList())
                _removedByZone.TryRemove(id, out _);

            Log.Out($"[KitsuneCommand] Safe zones updated: enabled={Settings.Enabled}, {Settings.Zones.Count} zone(s).");
        }

        /// <summary>How many entities each zone has removed since the server started, by zone id.</summary>
        public IReadOnlyDictionary<string, int> RemovedCounts() => new Dictionary<string, int>(_removedByZone);

        /// <summary>Fills gaps, clamps numbers, orders each zone's corners and gives every zone a unique id.</summary>
        public static SafeZoneSettings Normalize(SafeZoneSettings s)
        {
            s = s ?? new SafeZoneSettings();
            s.SweepIntervalSeconds = Math.Max(1, Math.Min(30, s.SweepIntervalSeconds));

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var zones = new List<SafeZone>();
            foreach (var z in s.Zones ?? new List<SafeZone>())
            {
                if (z == null) continue;
                if (zones.Count >= MaxZones) break;

                var id = (z.Id ?? "").Trim();
                if (id.Length == 0 || id.Length > 64 || !seen.Add(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    seen.Add(id);
                }
                z.Id = id;

                var name = (z.Name ?? "").Trim();
                if (name.Length == 0) name = "Safe zone";
                if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
                z.Name = name;

                int x1 = ClampCoord(z.MinX), x2 = ClampCoord(z.MaxX);
                int z1 = ClampCoord(z.MinZ), z2 = ClampCoord(z.MaxZ);
                z.MinX = Math.Min(x1, x2);
                z.MaxX = Math.Max(x1, x2);
                z.MinZ = Math.Min(z1, z2);
                z.MaxZ = Math.Max(z1, z2);

                zones.Add(z);
            }
            s.Zones = zones;
            return s;
        }

        /// <summary>
        /// What the sweep does with one living entity: the first enabled zone it's
        /// in decides. Enemies go unless the zone lets blood moon hordes through and
        /// this one is from the horde; passive animals go only if the zone asks.
        /// </summary>
        public static SafeZone ZoneToRemoveFrom(SafeZoneSettings settings, SafeZoneTarget kind, bool isBloodMoon, float x, float z)
        {
            if (settings == null || !settings.Enabled || kind == SafeZoneTarget.None) return null;

            foreach (var zone in settings.Zones)
            {
                if (!zone.Enabled || !zone.Contains(x, z)) continue;

                if (kind == SafeZoneTarget.Enemy)
                    return isBloodMoon && zone.AllowBloodMoon ? null : zone;

                return zone.RemoveAnimals ? zone : null;
            }
            return null;
        }

        private static int ClampCoord(int v) => Math.Max(-MaxCoordinate, Math.Min(MaxCoordinate, v));

        private void RestartTimer()
        {
            _sweepTimer?.Dispose();
            var period = TimeSpan.FromSeconds(Settings.SweepIntervalSeconds);
            _sweepTimer = new Timer(_ => QueueSweep(), null, period, period);
        }

        /// <summary>
        /// Posts one sweep to the main thread, never stacking them: if the last
        /// one hasn't run yet (a long frame or a save), this tick is skipped.
        /// </summary>
        private void QueueSweep()
        {
            var settings = Settings;
            if (!settings.Enabled || !settings.Zones.Any(z => z.Enabled)) return;

            var main = ModEntry.MainThreadContext;
            if (main == null) return;
            if (Interlocked.Exchange(ref _sweepQueued, 1) == 1) return;

            main.Post(_ =>
            {
                try { Sweep(Settings); }
                catch (Exception ex) { Log.Warning($"[KitsuneCommand] Safe zone sweep failed: {ex.Message}"); }
                finally { Interlocked.Exchange(ref _sweepQueued, 0); }
            }, null);
        }

        /// <summary>Main thread only.</summary>
        private void Sweep(SafeZoneSettings settings)
        {
            var world = GameManager.Instance?.World;
            if (world?.Entities?.list == null) return;

            // Collect first: removing an entity edits the list we're walking.
            List<KeyValuePair<int, string>> doomed = null;
            foreach (var entity in world.Entities.list)
            {
                if (!(entity is EntityAlive alive) || alive.IsDead() || alive.IsDespawned) continue;

                var kind = alive is EntityEnemy ? SafeZoneTarget.Enemy
                    : alive is EntityAnimal ? SafeZoneTarget.Animal
                    : SafeZoneTarget.None;

                var zone = ZoneToRemoveFrom(settings, kind, alive.IsBloodMoon, alive.position.x, alive.position.z);
                if (zone == null) continue;

                (doomed ??= new List<KeyValuePair<int, string>>()).Add(new KeyValuePair<int, string>(alive.entityId, zone.Id));
            }

            if (doomed == null) return;
            foreach (var pair in doomed)
            {
                world.RemoveEntity(pair.Key, EnumRemoveEntityReason.Despawned);
                _removedByZone.AddOrUpdate(pair.Value, 1, (_, n) => n + 1);
            }
        }

        private void LoadPersistedSettings()
        {
            try
            {
                var json = _settingsRepo.Get(SettingsKey);
                if (!string.IsNullOrEmpty(json))
                {
                    Settings = Normalize(JsonConvert.DeserializeObject<SafeZoneSettings>(json));
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] Failed to load safe zone settings, using defaults: {ex.Message}");
            }
            Settings = Normalize(Settings);
        }
    }

    public enum SafeZoneTarget
    {
        None,
        Enemy,
        Animal,
    }
}
