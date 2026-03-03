using SkiaSharp;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Renders map tiles from world data (biomes.png + dtm.raw heightmap).
    /// Tiles are 256x256 PNGs served via the map API.
    /// Uses L.CRS.Simple coordinate system for Leaflet integration.
    /// </summary>
    public class MapTileRenderer
    {
        private SKBitmap _biomeBitmap;
        private ushort[] _heightmap;
        private int _worldSize;
        private int _maxZoom;
        private bool _isInitialized;
        private string _worldDir;

        private readonly ConcurrentDictionary<string, byte[]> _tileCache
            = new ConcurrentDictionary<string, byte[]>();

        private const int TileSize = 256;

        public bool IsAvailable => _isInitialized;
        public int WorldSize => _worldSize;
        public int MaxZoom => _maxZoom;

        /// <summary>
        /// Initializes the renderer by loading world data files.
        /// Call after GameStartDone so we can discover the world directory.
        /// </summary>
        public bool Initialize()
        {
            try
            {
                _worldDir = FindWorldDirectory();
                if (_worldDir == null)
                {
                    Log.Warning("[KitsuneCommand] MapTileRenderer: Could not find world directory.");
                    return false;
                }

                Log.Out($"[KitsuneCommand] MapTileRenderer: World directory: {_worldDir}");

                // Load biomes.png
                var biomesPath = Path.Combine(_worldDir, "biomes.png");
                if (!File.Exists(biomesPath))
                {
                    Log.Warning("[KitsuneCommand] MapTileRenderer: biomes.png not found.");
                    return false;
                }

                _biomeBitmap = SKBitmap.Decode(biomesPath);
                if (_biomeBitmap == null)
                {
                    Log.Warning("[KitsuneCommand] MapTileRenderer: Failed to decode biomes.png.");
                    return false;
                }

                _worldSize = _biomeBitmap.Width; // Should equal height (square world)
                _maxZoom = (int)Math.Ceiling(Math.Log(_worldSize / (double)TileSize, 2));

                Log.Out($"[KitsuneCommand] MapTileRenderer: World size {_worldSize}x{_worldSize}, max zoom {_maxZoom}");

                // Load heightmap (optional, for shading)
                LoadHeightmap();

                _isInitialized = true;
                Log.Out("[KitsuneCommand] MapTileRenderer initialized successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[KitsuneCommand] MapTileRenderer initialization failed: {ex.Message}");
                Log.Exception(ex);
                return false;
            }
        }

        /// <summary>
        /// Renders a tile at the given zoom/x/y coordinates.
        /// Returns PNG bytes, or null if the tile is out of bounds.
        /// </summary>
        public byte[] RenderTile(int zoom, int x, int y)
        {
            if (!_isInitialized) return null;

            var cacheKey = $"{zoom}/{x}/{y}";
            if (_tileCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var tilesPerSide = 1 << zoom; // 2^zoom
            if (x < 0 || x >= tilesPerSide || y < 0 || y >= tilesPerSide)
                return null;

            try
            {
                var tileBytes = GenerateTile(zoom, x, y, tilesPerSide);
                if (tileBytes != null)
                {
                    _tileCache[cacheKey] = tileBytes;
                }
                return tileBytes;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] MapTileRenderer: Failed to render tile {cacheKey}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns map metadata for the frontend.
        /// </summary>
        public object GetMapInfo()
        {
            return new
            {
                worldSize = _worldSize,
                maxZoom = _maxZoom,
                tileSize = TileSize,
                isAvailable = _isInitialized,
                // World bounds in game coordinates (centered at 0,0)
                bounds = new
                {
                    minX = -_worldSize / 2,
                    minZ = -_worldSize / 2,
                    maxX = _worldSize / 2,
                    maxZ = _worldSize / 2
                }
            };
        }

        public void ClearCache()
        {
            _tileCache.Clear();
        }

        // --- Private Implementation ---

        private byte[] GenerateTile(int zoom, int x, int y, int tilesPerSide)
        {
            // Calculate source region in the biome bitmap
            float srcTileSize = _worldSize / (float)tilesPerSide;
            int srcX = (int)(x * srcTileSize);
            int srcY = (int)(y * srcTileSize);
            int srcW = (int)Math.Ceiling(srcTileSize);
            int srcH = (int)Math.Ceiling(srcTileSize);

            // Clamp to bitmap bounds
            srcW = Math.Min(srcW, _worldSize - srcX);
            srcH = Math.Min(srcH, _worldSize - srcY);

            if (srcW <= 0 || srcH <= 0) return null;

            // Extract the source region and resize to tile size
            var srcRect = new SKRectI(srcX, srcY, srcX + srcW, srcY + srcH);
            using var subset = new SKBitmap(srcW, srcH);
            _biomeBitmap.ExtractSubset(subset, srcRect);

            using var tile = subset.Resize(new SKImageInfo(TileSize, TileSize), SKSamplingOptions.Default);
            if (tile == null) return null;

            // Apply height shading if heightmap is available
            if (_heightmap != null)
            {
                ApplyHeightShading(tile, srcX, srcY, srcW, srcH);
            }

            // Encode to PNG
            using var image = SKImage.FromBitmap(tile);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            return data.ToArray();
        }

        private void ApplyHeightShading(SKBitmap tile, int srcX, int srcY, int srcW, int srcH)
        {
            float scaleX = srcW / (float)TileSize;
            float scaleY = srcH / (float)TileSize;

            for (int ty = 0; ty < TileSize; ty++)
            {
                for (int tx = 0; tx < TileSize; tx++)
                {
                    int hmX = srcX + (int)(tx * scaleX);
                    int hmY = srcY + (int)(ty * scaleY);

                    if (hmX < 0 || hmX >= _worldSize || hmY < 0 || hmY >= _worldSize)
                        continue;

                    int idx = hmY * _worldSize + hmX;

                    // Simple hill shading: compare with neighbor to the east and south
                    float shade = 0.5f;
                    if (hmX + 1 < _worldSize && hmY + 1 < _worldSize)
                    {
                        float height = _heightmap[idx] / 255f;
                        float heightE = _heightmap[hmY * _worldSize + hmX + 1] / 255f;
                        float heightS = _heightmap[(hmY + 1) * _worldSize + hmX] / 255f;
                        float dx = height - heightE;
                        float dy = height - heightS;
                        shade = 0.5f + (dx + dy) * 2f;
                        shade = Math.Max(0.2f, Math.Min(1.0f, shade));
                    }

                    var c = tile.GetPixel(tx, ty);
                    tile.SetPixel(tx, ty, new SKColor(
                        (byte)Math.Min(255, c.Red * shade),
                        (byte)Math.Min(255, c.Green * shade),
                        (byte)Math.Min(255, c.Blue * shade),
                        c.Alpha
                    ));
                }
            }
        }

        private void LoadHeightmap()
        {
            var dtmPath = Path.Combine(_worldDir, "dtm.raw");
            if (!File.Exists(dtmPath))
            {
                Log.Out("[KitsuneCommand] MapTileRenderer: dtm.raw not found, height shading disabled.");
                return;
            }

            try
            {
                var rawBytes = File.ReadAllBytes(dtmPath);
                int pixelCount = _worldSize * _worldSize;
                int expected1x = pixelCount * 2; // 16-bit, same resolution as biomes.png
                int expected2x = pixelCount * 2 * 4; // 16-bit, 2x resolution (common in V2.5+)

                _heightmap = new ushort[pixelCount];

                if (rawBytes.Length == expected1x)
                {
                    // 16-bit heightmap at same resolution as biomes
                    Buffer.BlockCopy(rawBytes, 0, _heightmap, 0, rawBytes.Length);
                    Log.Out("[KitsuneCommand] MapTileRenderer: Heightmap loaded (1x), hill shading enabled.");
                }
                else if (rawBytes.Length == expected2x)
                {
                    // 16-bit heightmap at 2x resolution — subsample to match biome size
                    int hiResSize = _worldSize * 2;
                    for (int y = 0; y < _worldSize; y++)
                    {
                        for (int x = 0; x < _worldSize; x++)
                        {
                            int srcIdx = (y * 2) * hiResSize + (x * 2);
                            _heightmap[y * _worldSize + x] = BitConverter.ToUInt16(rawBytes, srcIdx * 2);
                        }
                    }
                    Log.Out("[KitsuneCommand] MapTileRenderer: Heightmap loaded (2x downsampled), hill shading enabled.");
                }
                else
                {
                    Log.Warning($"[KitsuneCommand] MapTileRenderer: dtm.raw size {rawBytes.Length} bytes, cannot match world size {_worldSize}. Height shading disabled.");
                    _heightmap = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] MapTileRenderer: Failed to load heightmap: {ex.Message}");
                _heightmap = null;
            }
        }

        private static string FindWorldDirectory()
        {
            try
            {
                var worldName = GamePrefs.GetString(EnumGamePrefs.GameWorld);
                if (string.IsNullOrEmpty(worldName))
                    return null;

                Log.Out($"[KitsuneCommand] MapTileRenderer: Looking for world '{worldName}'");

                // Use the game's install path (AppModManager.GamePath or derive from ModEntry.ModPath)
                // Assembly.GetExecutingAssembly().Location returns empty on Mono/Unity,
                // so we use the mod path and navigate up to the game root.
                var gameDir = Path.GetFullPath(Path.Combine(Core.ModEntry.ModPath, "..", ".."));

                Log.Out($"[KitsuneCommand] MapTileRenderer: Game directory: {gameDir}");

                // Try shipped worlds first (Navezgane)
                var shipped = Path.Combine(gameDir, "Data", "Worlds", worldName);
                if (Directory.Exists(shipped) && File.Exists(Path.Combine(shipped, "biomes.png")))
                    return shipped;

                // Try generated worlds (RWG)
                var userDataDir = GameIO.GetUserGameDataDir();
                var generated = Path.Combine(userDataDir, "GeneratedWorlds", worldName);
                if (Directory.Exists(generated) && File.Exists(Path.Combine(generated, "biomes.png")))
                    return generated;

                // Fallback: try the save game dir
                var saveDir = GameIO.GetSaveGameDir();
                if (!string.IsNullOrEmpty(saveDir))
                {
                    var saveWorld = Path.Combine(Path.GetDirectoryName(saveDir), worldName);
                    if (Directory.Exists(saveWorld) && File.Exists(Path.Combine(saveWorld, "biomes.png")))
                        return saveWorld;
                }

                Log.Warning($"[KitsuneCommand] MapTileRenderer: World '{worldName}' not found in any standard location.");
                Log.Warning($"[KitsuneCommand] MapTileRenderer: Checked: {shipped}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] MapTileRenderer: Error finding world directory: {ex.Message}");
                return null;
            }
        }
    }
}
