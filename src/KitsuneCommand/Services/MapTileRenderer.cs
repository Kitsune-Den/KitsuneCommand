using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using SkiaSharp;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Renders map tiles from the world's files: biomes, hill shading from the
    /// heightmap, water, roads, and POI footprints.
    /// Tiles are 256x256 PNGs served via the map API; the whole world maps onto
    /// Leaflet's L.CRS.Simple 0..256 square at zoom 0.
    ///
    /// What the game writes (read from the V3.3 WorldBuilder and Navezgane):
    ///   - The world is HeightMapSize blocks square (map_info.xml), centered on 0,0.
    ///   - biomes.png is smaller than the world: 1/8 on RWG, 1/2 on Navezgane.
    ///   - dtm.raw is 16-bit heights (block height * 256), world-sized, rows south to north.
    ///   - The PNGs run north to south. splat3: red = highway, green = country road.
    ///     Water is green or blue in splat4_processed.png, blue in an RWG splat4.png.
    ///   - The splats use alpha 0 for data, so they're decoded unpremultiplied.
    /// </summary>
    public class MapTileRenderer
    {
        private const int TileSize = 256;

        // The heightmap and feature masks are held on a grid of at most this many
        // cells a side; a 10k world is read at 1/3 so memory stays bounded.
        private const int MaxGridSize = 4096;

        // Deep zooms of a big world run to thousands of tiles; past this many
        // bytes, new tiles are rendered each time (browsers cache them an hour).
        private const long MaxCacheBytes = 64L * 1024 * 1024;

        private const byte Highway = 1;
        private const byte CountryRoad = 2;
        private const byte Water = 4;

        // Packed little-endian RGBA, the byte order of an Rgba8888 tile.
        private static readonly uint HighwayColor = Pack(52, 52, 56);
        private static readonly uint CountryRoadColor = Pack(176, 150, 104);
        private static readonly uint WaterColor = Pack(59, 104, 170);
        private static readonly uint PoiColor = Pack(120, 110, 100);

        // The game's biome colors (biomes.xml) are flat and saturated; these read
        // as terrain under hill shading. Modded biomes keep their own color.
        private static readonly Dictionary<uint, uint> BiomePalette = new Dictionary<uint, uint>
        {
            [Pack(255, 255, 255)] = Pack(222, 228, 232), // snow
            [Pack(0, 64, 0)] = Pack(74, 102, 62),        // pine forest
            [Pack(255, 228, 119)] = Pack(204, 184, 128), // desert
            [Pack(255, 168, 0)] = Pack(140, 122, 92),    // wasteland
            [Pack(186, 0, 255)] = Pack(92, 78, 74),      // burnt forest
        };

        private int _worldSize;
        private int _maxZoom;
        private bool _isInitialized;

        private uint[] _biomes;
        private int _biomeSize;

        // Grid level 0 covers _gridStep x _gridStep blocks per cell, north-up.
        private int _gridStep;
        private int _gridSize;
        private ushort[] _heights;

        // Feature bits per cell; each level ORs 2x2 cells of the one before, so
        // roads stay a pixel wide at low zoom instead of dropping out.
        private byte[][] _features;
        private int[] _featureSizes;

        private PoiFootprint[] _pois = Array.Empty<PoiFootprint>();

        private readonly ConcurrentDictionary<string, byte[]> _tileCache
            = new ConcurrentDictionary<string, byte[]>();
        private long _cacheBytes;

        public bool IsAvailable => _isInitialized;
        public int WorldSize => _worldSize;
        public int MaxZoom => _maxZoom;

        /// <summary>
        /// Initializes the renderer by loading world data files.
        /// Call after GameStartDone so we can discover the world directory.
        /// </summary>
        public bool Initialize()
        {
            var worldDir = FindWorldDirectory();
            if (worldDir == null)
            {
                Log.Warning("[KitsuneCommand] MapTileRenderer: Could not find world directory.");
                return false;
            }

            Log.Out($"[KitsuneCommand] MapTileRenderer: World directory: {worldDir}");
            return Load(worldDir, ReadPoiFootprints());
        }

        /// <summary>
        /// Loads a world folder. Only biomes.png is required; everything else
        /// adds a layer when it's there.
        /// </summary>
        public bool Load(string worldDir, IEnumerable<PoiFootprint> pois)
        {
            try
            {
                ClearCache();

                var biomesPath = Path.Combine(worldDir, "biomes.png");
                if (!File.Exists(biomesPath))
                {
                    Log.Warning("[KitsuneCommand] MapTileRenderer: biomes.png not found.");
                    return false;
                }

                if (!LoadBiomes(biomesPath))
                {
                    Log.Warning("[KitsuneCommand] MapTileRenderer: Failed to decode biomes.png.");
                    return false;
                }

                _worldSize = DetectWorldSize(worldDir, _biomeSize);
                _maxZoom = Math.Max(0, (int)Math.Ceiling(Math.Log(_worldSize / (double)TileSize, 2)));
                _gridStep = (_worldSize + MaxGridSize - 1) / MaxGridSize;
                _gridSize = (_worldSize + _gridStep - 1) / _gridStep;

                Log.Out($"[KitsuneCommand] MapTileRenderer: World size {_worldSize}x{_worldSize} (biomes {_biomeSize}px), max zoom {_maxZoom}");

                _heights = LoadHeightmap(Path.Combine(worldDir, "dtm.raw"));
                LoadFeatures(worldDir);

                _pois = (pois ?? Enumerable.Empty<PoiFootprint>()).ToArray();
                Log.Out($"[KitsuneCommand] MapTileRenderer: {_pois.Length} POI footprints.");

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
            if (zoom < 0 || zoom > _maxZoom) return null;

            var cacheKey = $"{zoom}/{x}/{y}";
            if (_tileCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var tilesPerSide = 1 << zoom; // 2^zoom
            if (x < 0 || x >= tilesPerSide || y < 0 || y >= tilesPerSide)
                return null;

            try
            {
                var tileBytes = GenerateTile(zoom, x, y, tilesPerSide);
                if (tileBytes != null && Interlocked.Read(ref _cacheBytes) < MaxCacheBytes
                    && _tileCache.TryAdd(cacheKey, tileBytes))
                {
                    Interlocked.Add(ref _cacheBytes, tileBytes.Length);
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

        /// <summary>POI footprints in game coordinates, for the map's POI layer.</summary>
        public IReadOnlyList<PoiFootprint> GetPois() => _pois;

        public void ClearCache()
        {
            _tileCache.Clear();
            Interlocked.Exchange(ref _cacheBytes, 0);
        }

        // --- Tile rendering ---

        private byte[] GenerateTile(int zoom, int x, int y, int tilesPerSide)
        {
            // Blocks per tile pixel; the tile's top-left in north-up block space.
            double scale = _worldSize / (double)(TileSize * tilesPerSide);
            double originX = x * TileSize * scale;
            double originY = y * TileSize * scale;

            // Feature level whose cells are about one tile pixel.
            int level = 0;
            while (level + 1 < _features?.Length && (_worldSize / (double)_featureSizes[level + 1]) <= scale)
                level++;

            // Shade over at least a pixel, so low zooms show ridges, not grid noise.
            double step = Math.Max(scale, _gridStep);
            double exaggeration = Math.Min(6.0, 2.0 * Math.Sqrt(step / _gridStep));

            var pixels = new uint[TileSize * TileSize];
            var features = new byte[TileSize * TileSize];

            for (int ty = 0; ty < TileSize; ty++)
            {
                double wy = originY + (ty + 0.5) * scale;
                for (int tx = 0; tx < TileSize; tx++)
                {
                    double wx = originX + (tx + 0.5) * scale;
                    int i = ty * TileSize + tx;

                    byte f = FeatureAt(level, wx, wy);
                    features[i] = f;
                    if ((f & Water) != 0)
                    {
                        pixels[i] = WaterColor;
                        continue;
                    }

                    uint color = BiomeAt(wx, wy);
                    if (_heights != null)
                        color = Shade(color, HillShade(wx, wy, step, exaggeration));
                    pixels[i] = color;
                }
            }

            DrawPois(pixels, originX, originY, scale);

            for (int i = 0; i < pixels.Length; i++)
            {
                if ((features[i] & Highway) != 0) pixels[i] = HighwayColor;
                else if ((features[i] & CountryRoad) != 0) pixels[i] = CountryRoadColor;
            }

            var info = new SKImageInfo(TileSize, TileSize, SKColorType.Rgba8888, SKAlphaType.Opaque);
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var image = SKImage.FromPixelCopy(info, handle.AddrOfPinnedObject(), TileSize * 4);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                return data.ToArray();
            }
            finally
            {
                handle.Free();
            }
        }

        private uint BiomeAt(double wx, double wy)
        {
            int bx = Clamp((int)(wx * _biomeSize / _worldSize), _biomeSize);
            int by = Clamp((int)(wy * _biomeSize / _worldSize), _biomeSize);
            return _biomes[by * _biomeSize + bx];
        }

        private byte FeatureAt(int level, double wx, double wy)
        {
            if (_features == null) return 0;
            int size = _featureSizes[level];
            int fx = Clamp((int)(wx * size / _worldSize), size);
            int fy = Clamp((int)(wy * size / _worldSize), size);
            return _features[level][fy * size + fx];
        }

        private float HeightAt(double wx, double wy)
        {
            int gx = Clamp((int)(wx / _gridStep), _gridSize);
            int gy = Clamp((int)(wy / _gridStep), _gridSize);
            return _heights[gy * _gridSize + gx] / 256f;
        }

        /// <summary>
        /// Lambert shading with the light in the north-west, scaled so flat
        /// ground keeps its color.
        /// </summary>
        private float HillShade(double wx, double wy, double step, double exaggeration)
        {
            double dx = (HeightAt(wx + step, wy) - HeightAt(wx - step, wy)) / (2 * step);
            double dy = (HeightAt(wx, wy + step) - HeightAt(wx, wy - step)) / (2 * step);

            double nx = -dx * exaggeration, ny = -dy * exaggeration;
            double lightDot = (-nx - ny + Math.Sqrt(2)) / 2; // dot(n, normalize(-1, -1, sqrt2))
            double lambert = lightDot / Math.Sqrt(nx * nx + ny * ny + 1);

            return (float)Math.Max(0.35, Math.Min(1.25, 0.45 + 0.6 * lambert));
        }

        private void DrawPois(uint[] pixels, double originX, double originY, double scale)
        {
            double extent = TileSize * scale;
            double half = _worldSize / 2.0;

            foreach (var poi in _pois)
            {
                // Game coords to north-up block space: x east, y south.
                double left = poi.X + half;
                double top = half - (poi.Z + poi.SizeZ);
                double right = left + poi.SizeX;
                double bottom = top + poi.SizeZ;

                if (right <= originX || left >= originX + extent || bottom <= originY || top >= originY + extent)
                    continue;

                // At least one pixel, so POIs stay visible zoomed out.
                int x0 = Clamp((int)((left - originX) / scale), TileSize);
                int y0 = Clamp((int)((top - originY) / scale), TileSize);
                int x1 = Math.Max(x0 + 1, Math.Min(TileSize, (int)Math.Ceiling((right - originX) / scale)));
                int y1 = Math.Max(y0 + 1, Math.Min(TileSize, (int)Math.Ceiling((bottom - originY) / scale)));

                for (int py = y0; py < y1; py++)
                    for (int px = x0; px < x1; px++)
                        pixels[py * TileSize + px] = Blend(pixels[py * TileSize + px], PoiColor, 0.6f);
            }
        }

        // --- Loading ---

        private bool LoadBiomes(string path)
        {
            var biomes = DecodePng(path, out _biomeSize, out var height);
            if (biomes == null || _biomeSize != height) return false;

            for (int i = 0; i < biomes.Length; i++)
            {
                var opaque = biomes[i] | 0xFF000000;
                biomes[i] = BiomePalette.TryGetValue(opaque, out var mapped) ? mapped : opaque;
            }
            _biomes = biomes;
            return true;
        }

        /// <summary>
        /// The world's size in blocks: map_info.xml's HeightMapSize, else the
        /// square dtm.raw, else biomes.png (only right when biomes is world-sized).
        /// </summary>
        public static int DetectWorldSize(string worldDir, int biomeSize)
        {
            try
            {
                var mapInfo = Path.Combine(worldDir, "map_info.xml");
                if (File.Exists(mapInfo))
                {
                    var value = XDocument.Load(mapInfo).Descendants("property")
                        .FirstOrDefault(p => (string)p.Attribute("name") == "HeightMapSize")
                        ?.Attribute("value")?.Value;
                    if (value != null && int.TryParse(value.Split(',')[0].Trim(), out var size) && size > 0)
                        return size;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] MapTileRenderer: Couldn't read map_info.xml: {ex.Message}");
            }

            var dtm = Path.Combine(worldDir, "dtm.raw");
            if (File.Exists(dtm))
            {
                long cells = new FileInfo(dtm).Length / 2;
                int side = (int)Math.Round(Math.Sqrt(cells));
                if (side > 0 && (long)side * side == cells)
                    return side;
            }

            return biomeSize;
        }

        /// <summary>
        /// Reads dtm.raw onto the grid, averaging each cell and flipping it
        /// north-up. Streams rows so a big world isn't read into memory whole.
        /// </summary>
        private ushort[] LoadHeightmap(string dtmPath)
        {
            if (!File.Exists(dtmPath))
            {
                Log.Out("[KitsuneCommand] MapTileRenderer: dtm.raw not found, height shading disabled.");
                return null;
            }

            try
            {
                int w = _worldSize;
                long expected = (long)w * w * 2;
                long actual = new FileInfo(dtmPath).Length;
                if (actual != expected)
                {
                    Log.Warning($"[KitsuneCommand] MapTileRenderer: dtm.raw is {actual} bytes, expected {expected} for a {w} world. Height shading disabled.");
                    return null;
                }

                var heights = new ushort[_gridSize * _gridSize];
                var sums = new long[_gridSize];
                var counts = new int[_gridSize];
                var row = new byte[w * 2];

                using var stream = new FileStream(dtmPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
                int gridRow = -1;
                for (int fileRow = 0; fileRow < w; fileRow++)
                {
                    ReadFully(stream, row);

                    int gy = (w - 1 - fileRow) / _gridStep; // file runs south to north
                    if (gy != gridRow)
                    {
                        FlushHeightRow(heights, gridRow, sums, counts);
                        gridRow = gy;
                    }

                    for (int x = 0; x < w; x++)
                    {
                        int gx = x / _gridStep;
                        sums[gx] += row[x * 2] | (row[x * 2 + 1] << 8);
                        counts[gx]++;
                    }
                }
                FlushHeightRow(heights, gridRow, sums, counts);

                Log.Out("[KitsuneCommand] MapTileRenderer: Heightmap loaded, hill shading enabled.");
                return heights;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] MapTileRenderer: Failed to load heightmap: {ex.Message}");
                return null;
            }
        }

        private void FlushHeightRow(ushort[] heights, int gy, long[] sums, int[] counts)
        {
            if (gy < 0) return;
            for (int gx = 0; gx < _gridSize; gx++)
            {
                if (counts[gx] > 0)
                    heights[gy * _gridSize + gx] = (ushort)(sums[gx] / counts[gx]);
                sums[gx] = 0;
                counts[gx] = 0;
            }
        }

        private void LoadFeatures(string worldDir)
        {
            var grid = new byte[_gridSize * _gridSize];
            bool any = false;

            var roads = Path.Combine(worldDir, "splat3.png");
            if (File.Exists(roads))
                any |= MarkFeatures(roads, grid, (0, Highway), (1, CountryRoad));
            else
                Log.Out("[KitsuneCommand] MapTileRenderer: splat3.png not found, no roads.");

            // Navezgane ships the processed file; RWG worlds have splat4.png.
            var waterProcessed = Path.Combine(worldDir, "splat4_processed.png");
            var water = Path.Combine(worldDir, "splat4.png");
            if (File.Exists(waterProcessed))
                any |= MarkFeatures(waterProcessed, grid, (1, Water), (2, Water));
            else if (File.Exists(water))
                any |= MarkFeatures(water, grid, (2, Water));
            else
                Log.Out("[KitsuneCommand] MapTileRenderer: splat4 not found, no water.");

            if (!any)
            {
                _features = null;
                _featureSizes = null;
                return;
            }

            var levels = new List<byte[]> { grid };
            var sizes = new List<int> { _gridSize };
            while (sizes[sizes.Count - 1] > 1)
            {
                int src = sizes[sizes.Count - 1];
                int dst = (src + 1) / 2;
                var prev = levels[levels.Count - 1];
                var next = new byte[dst * dst];
                for (int y = 0; y < src; y++)
                    for (int x = 0; x < src; x++)
                        next[(y / 2) * dst + x / 2] |= prev[y * src + x];
                levels.Add(next);
                sizes.Add(dst);
            }
            _features = levels.ToArray();
            _featureSizes = sizes.ToArray();
        }

        /// <summary>
        /// Sets <paramref name="grid"/> bits wherever the given channels of a
        /// square PNG are non-zero.
        /// </summary>
        private bool MarkFeatures(string path, byte[] grid, params (int Channel, byte Bit)[] rules)
        {
            try
            {
                bool read = ForEachPngRow(path, (y, size, row) =>
                {
                    int gy = (int)((long)y * _gridSize / size);
                    for (int x = 0; x < size; x++)
                    {
                        byte bits = 0;
                        foreach (var rule in rules)
                            if (row[x * 4 + rule.Channel] != 0) bits |= rule.Bit;
                        if (bits != 0)
                            grid[gy * _gridSize + (int)((long)x * _gridSize / size)] |= bits;
                    }
                });

                if (!read)
                {
                    Log.Warning($"[KitsuneCommand] MapTileRenderer: Couldn't read {Path.GetFileName(path)}.");
                    return false;
                }

                Log.Out($"[KitsuneCommand] MapTileRenderer: Loaded {Path.GetFileName(path)}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] MapTileRenderer: Failed to load {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Calls <paramref name="onRow"/>(y, size, rgba) for each row of a square
        /// PNG, top to bottom. Streams the game's PNGs; decodes others whole.
        /// </summary>
        private static bool ForEachPngRow(string path, Action<int, int, byte[]> onRow)
        {
            using (var reader = PngRowReader.Open(path))
            {
                if (reader != null)
                {
                    if (reader.Width != reader.Height) return false;
                    var row = new byte[reader.Width * 4];
                    for (int y = 0; y < reader.Height; y++)
                    {
                        reader.ReadRow(row);
                        onRow(y, reader.Width, row);
                    }
                    return true;
                }
            }

            var pixels = DecodePng(path, out var width, out var height);
            if (pixels == null || width != height) return false;
            var bytes = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(pixels, y * width * 4, bytes, 0, bytes.Length);
                onRow(y, width, bytes);
            }
            return true;
        }

        /// <summary>Decodes a PNG to packed RGBA (unpremultiplied).</summary>
        private static uint[] DecodePng(string path, out int width, out int height)
        {
            width = height = 0;
            using var codec = SKCodec.Create(path);
            if (codec == null) return null;

            width = codec.Info.Width;
            height = codec.Info.Height;
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var pixels = new uint[width * height];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                var result = codec.GetPixels(info, handle.AddrOfPinnedObject());
                return result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput ? pixels : null;
            }
            finally
            {
                handle.Free();
            }
        }

        private static void ReadFully(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }
        }

        // --- Game lookups ---

        private static List<PoiFootprint> ReadPoiFootprints()
        {
            try
            {
                return ReadPoiFootprintsFromGame();
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] MapTileRenderer: Couldn't read POIs, map will show none: {ex.Message}");
                return new List<PoiFootprint>();
            }
        }

        // Kept apart so a missing game member fails here, inside the caller's try.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<PoiFootprint> ReadPoiFootprintsFromGame()
        {
            var result = new List<PoiFootprint>();
            var prefabs = GameManager.Instance?.GetDynamicPrefabDecorator()?.allPrefabs;
            if (prefabs == null) return result;

            foreach (var pi in prefabs.ToArray())
            {
                if (pi == null || pi.boundingBoxSize.x < 4 || pi.boundingBoxSize.z < 4)
                    continue;

                string name = pi.name;
                try
                {
                    if (pi.prefab != null) name = pi.prefab.LocalizedName;
                }
                catch
                {
                    // Raw prefab name is fine.
                }

                result.Add(new PoiFootprint
                {
                    Name = name,
                    X = pi.boundingBoxPosition.x,
                    Z = pi.boundingBoxPosition.z,
                    SizeX = pi.boundingBoxSize.x,
                    SizeZ = pi.boundingBoxSize.z,
                });
            }
            return result;
        }

        private static string FindWorldDirectory()
        {
            try
            {
                var worldName = GamePrefs.GetString(GameIntegration.GameCompat.Pref(nameof(EnumGamePrefs.GameWorld)));
                if (string.IsNullOrEmpty(worldName))
                    return null;

                Log.Out($"[KitsuneCommand] MapTileRenderer: Looking for world '{worldName}'");

                // The game's install folder, not the mod's: KC may be in the user-data Mods/.
                var gameDir = GameIntegration.GamePaths.InstallDir;

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

        // --- Color helpers ---

        private static uint Pack(byte r, byte g, byte b) => 0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | r;

        private static uint Shade(uint c, float f)
        {
            uint r = (uint)Math.Min(255f, (c & 0xFF) * f);
            uint g = (uint)Math.Min(255f, ((c >> 8) & 0xFF) * f);
            uint b = (uint)Math.Min(255f, ((c >> 16) & 0xFF) * f);
            return 0xFF000000u | (b << 16) | (g << 8) | r;
        }

        private static uint Blend(uint under, uint over, float alpha)
        {
            uint Mix(int shift) => (uint)(((under >> shift) & 0xFF) * (1 - alpha) + ((over >> shift) & 0xFF) * alpha);
            return 0xFF000000u | (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
        }

        private static int Clamp(int v, int size) => v < 0 ? 0 : v >= size ? size - 1 : v;
    }

    /// <summary>A POI's footprint: its south-west (minimum X/Z) corner and size, in blocks.</summary>
    public class PoiFootprint
    {
        public string Name { get; set; }
        public int X { get; set; }
        public int Z { get; set; }
        public int SizeX { get; set; }
        public int SizeZ { get; set; }
    }
}
