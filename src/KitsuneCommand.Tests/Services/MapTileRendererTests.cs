using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using KitsuneCommand.Services;
using SkiaSharp;

namespace KitsuneCommand.Tests.Services
{
    /// <summary>
    /// Renders a small synthetic world laid out the way the game writes one:
    /// biomes.png at 1/8 size, dtm.raw south to north, splat data in alpha-0 pixels.
    /// </summary>
    [TestFixture]
    public class MapTileRendererTests
    {
        private const int World = 1024;
        private const int Biomes = World / 8;

        private static readonly SKColor Pine = new SKColor(0, 64, 0);
        private static readonly SKColor Highway = new SKColor(52, 52, 56);
        private static readonly SKColor Water = new SKColor(59, 104, 170);

        // Not SKColors.Transparent, which is white under alpha 0.
        private static readonly SKColor Empty = new SKColor(0, 0, 0, 0);

        private string _worldDir;

        [SetUp]
        public void SetUp()
        {
            _worldDir = Path.Combine(Path.GetTempPath(), $"kc_map_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_worldDir);
            WritePng("biomes.png", Biomes, (x, y) => Pine);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_worldDir))
                    Directory.Delete(_worldDir, true);
            }
            catch { }
        }

        [Test]
        public void DetectWorldSize_PrefersMapInfo()
        {
            WriteMapInfo(World);
            WriteDtm(512, (x, row) => 0);

            Assert.That(MapTileRenderer.DetectWorldSize(_worldDir, Biomes), Is.EqualTo(World));
        }

        [Test]
        public void DetectWorldSize_FallsBackToDtm()
        {
            WriteDtm(512, (x, row) => 0);

            Assert.That(MapTileRenderer.DetectWorldSize(_worldDir, Biomes), Is.EqualTo(512));
        }

        [Test]
        public void DetectWorldSize_FallsBackToBiomes()
        {
            Assert.That(MapTileRenderer.DetectWorldSize(_worldDir, Biomes), Is.EqualTo(Biomes));
        }

        [Test]
        public void Load_ReportsWorldSizeNotBiomeSize()
        {
            WriteMapInfo(World);
            var renderer = Load();

            Assert.That(renderer.WorldSize, Is.EqualTo(World));
            Assert.That(renderer.MaxZoom, Is.EqualTo(2)); // 1024 / 256 = 4 tiles a side
        }

        [Test]
        public void Load_FailsWithoutBiomes()
        {
            File.Delete(Path.Combine(_worldDir, "biomes.png"));

            Assert.That(new MapTileRenderer().Load(_worldDir, null), Is.False);
        }

        [Test]
        public void RenderTile_ReadsHeightmapSouthToNorth()
        {
            WriteMapInfo(World);
            // Flat in the south half; in the north half the ground climbs
            // northward, so that slope faces south, away from the NW light.
            WriteDtm(World, (x, row) => row < World / 2 ? 0 : (row - World / 2) * 256);
            var tile = Render(Load(), 0, 0, 0);

            var north = tile.GetPixel(128, 64);
            var south = tile.GetPixel(128, 192);
            Assert.That(north.Green, Is.LessThan(south.Green - 10), "the slope should shade the north half");
        }

        [Test]
        public void RenderTile_DrawsRoadsAndWaterWhereThePngsPutThem()
        {
            WriteMapInfo(World);
            // A highway across the top (north) of the world, water in the
            // south-east corner. The game stores both with alpha 0.
            WritePng("splat3.png", World, (x, y) => y < 16 ? new SKColor(255, 0, 0, 0) : Empty);
            WritePng("splat4_processed.png", World, (x, y) => x >= 768 && y >= 768 ? new SKColor(0, 255, 0, 0) : Empty);
            var tile = Render(Load(), 0, 0, 0);

            Assert.That(tile.GetPixel(128, 1), Is.EqualTo(Highway));
            Assert.That(tile.GetPixel(230, 230), Is.EqualTo(Water));
            Assert.That(tile.GetPixel(128, 128), Is.Not.EqualTo(Highway).And.Not.EqualTo(Water));
        }

        [Test]
        public void RenderTile_RwgWaterIsBlueChannel()
        {
            WriteMapInfo(World);
            WritePng("splat4.png", World, (x, y) => x < 256 ? new SKColor(0, 0, 40, 0) : Empty);
            var tile = Render(Load(), 0, 0, 0);

            Assert.That(tile.GetPixel(10, 128), Is.EqualTo(Water));
            Assert.That(tile.GetPixel(200, 128), Is.Not.EqualTo(Water));
        }

        [Test]
        public void RenderTile_DrawsPoiFootprintsInGameCoordinates()
        {
            WriteMapInfo(World);
            // South-west corner (-112, -112), 64 blocks: blocks 400..464 east
            // of the west edge and 560..624 south of the north edge.
            var poi = new PoiFootprint { Name = "Shotgun Messiah", X = -112, Z = -112, SizeX = 64, SizeZ = 64 };
            var renderer = Load(poi);
            var tile = Render(renderer, 0, 0, 0);

            var inside = tile.GetPixel(108, 148);
            var outside = tile.GetPixel(108, 100);
            Assert.That(inside, Is.Not.EqualTo(outside));
            Assert.That(renderer.GetPois().Single().Name, Is.EqualTo("Shotgun Messiah"));
        }

        [Test]
        public void RenderTile_RejectsTilesOutsideTheWorld()
        {
            WriteMapInfo(World);
            var renderer = Load();

            Assert.That(renderer.RenderTile(0, 1, 0), Is.Null);
            Assert.That(renderer.RenderTile(3, 0, 0), Is.Null);
            Assert.That(renderer.RenderTile(2, 3, 3), Is.Not.Null);
        }

        // --- Helpers ---

        private MapTileRenderer Load(params PoiFootprint[] pois)
        {
            var renderer = new MapTileRenderer();
            Assert.That(renderer.Load(_worldDir, pois), Is.True);
            return renderer;
        }

        private static SKBitmap Render(MapTileRenderer renderer, int z, int x, int y)
        {
            var png = renderer.RenderTile(z, x, y);
            Assert.That(png, Is.Not.Null);
            return SKBitmap.Decode(png);
        }

        private void WriteMapInfo(int size)
        {
            File.WriteAllText(Path.Combine(_worldDir, "map_info.xml"),
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<MapInfo>\n" +
                $"  <property name=\"HeightMapSize\" value=\"{size},{size}\" />\n</MapInfo>\n");
        }

        // Rows in file order: row 0 is the south edge.
        private void WriteDtm(int size, Func<int, int, int> height)
        {
            using var writer = new BinaryWriter(File.Create(Path.Combine(_worldDir, "dtm.raw")));
            for (int row = 0; row < size; row++)
                for (int x = 0; x < size; x++)
                    writer.Write((ushort)height(x, row));
        }

        // Unpremultiplied, so alpha-0 pixels keep their color like the game's splats.
        private void WritePng(string name, int size, Func<int, int, SKColor> color)
        {
            var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bytes = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var c = color(x, y);
                    int i = (y * size + x) * 4;
                    bytes[i] = c.Red;
                    bytes[i + 1] = c.Green;
                    bytes[i + 2] = c.Blue;
                    bytes[i + 3] = c.Alpha;
                }

            using var image = SKImage.FromPixelCopy(info, bytes);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(_worldDir, name), data.ToArray());
        }
    }
}
