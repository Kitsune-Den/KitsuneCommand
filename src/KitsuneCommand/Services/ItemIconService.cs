using System.Collections.Concurrent;
using SkiaSharp;

namespace KitsuneCommand.Services
{
    /// <summary>
    /// Serves item icon images from the game's Data/ItemIcons directory.
    /// Provides optional resizing with in-memory caching for thumbnails.
    /// </summary>
    public class ItemIconService
    {
        private string _iconDir;
        private bool _isAvailable;

        private readonly ConcurrentDictionary<string, byte[]> _thumbnailCache
            = new ConcurrentDictionary<string, byte[]>();

        private const int MaxSize = 160;

        public bool IsAvailable => _isAvailable;

        /// <summary>
        /// Discovers the ItemIcons directory from the game installation.
        /// Call after GameStartDone.
        /// </summary>
        public void Initialize()
        {
            try
            {
                // The game's install folder, not the mod's: KC may be in the user-data Mods/.
                var gameDir = GameIntegration.GamePaths.InstallDir;
                var iconsPath = Path.Combine(gameDir, "Data", "ItemIcons");

                if (Directory.Exists(iconsPath))
                {
                    _iconDir = iconsPath;
                    _isAvailable = true;
                    var count = Directory.GetFiles(iconsPath, "*.png").Length;
                    Log.Out($"[KitsuneCommand] ItemIconService: Found {count} icons at {iconsPath}");
                }
                else
                {
                    Log.Warning($"[KitsuneCommand] ItemIconService: Data/ItemIcons not found at {iconsPath}");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] ItemIconService init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns PNG bytes for the given icon name at the requested size.
        /// Returns null if the icon is not found.
        /// </summary>
        public byte[] GetIcon(string iconName, int size = 0)
        {
            if (!_isAvailable || string.IsNullOrEmpty(iconName))
                return null;

            // Sanitize input to prevent path traversal
            iconName = Path.GetFileNameWithoutExtension(iconName);
            if (iconName.Contains("..") || iconName.Contains("/") || iconName.Contains("\\"))
                return null;

            if (size <= 0 || size > MaxSize)
                size = MaxSize;

            var cacheKey = $"{iconName}_{size}";
            if (_thumbnailCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var filePath = Path.Combine(_iconDir, iconName + ".png");
            if (!File.Exists(filePath))
                return null;

            try
            {
                var fileBytes = File.ReadAllBytes(filePath);

                if (size == MaxSize)
                {
                    _thumbnailCache[cacheKey] = fileBytes;
                    return fileBytes;
                }

                // Resize using SkiaSharp (decode from memory to avoid Mono file handle issues)
                using var original = SKBitmap.Decode(fileBytes);
                if (original == null)
                {
                    // SkiaSharp decode failed — return original file as fallback
                    _thumbnailCache[cacheKey] = fileBytes;
                    return fileBytes;
                }

                using var resized = original.Resize(
                    new SKImageInfo(size, size), SKFilterQuality.Medium);
                if (resized == null)
                {
                    _thumbnailCache[cacheKey] = fileBytes;
                    return fileBytes;
                }

                using var image = SKImage.FromBitmap(resized);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                var result = data.ToArray();

                _thumbnailCache[cacheKey] = result;
                return result;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KitsuneCommand] ItemIconService: Failed to resize {iconName}: {ex.Message}");

                // Fallback: serve original file even if resize fails
                try
                {
                    var fallback = File.ReadAllBytes(filePath);
                    _thumbnailCache[cacheKey] = fallback;
                    return fallback;
                }
                catch { return null; }
            }
        }
    }
}
