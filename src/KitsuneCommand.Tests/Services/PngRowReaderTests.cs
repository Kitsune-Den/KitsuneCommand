using System;
using System.IO;
using NUnit.Framework;
using KitsuneCommand.Services;
using SkiaSharp;

namespace KitsuneCommand.Tests.Services
{
    [TestFixture]
    public class PngRowReaderTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), $"kc_png_test_{Guid.NewGuid():N}.png");
        }

        [TearDown]
        public void TearDown()
        {
            try { File.Delete(_path); } catch { }
        }

        [Test]
        public void ReadRow_MatchesSkiaEncodedRgba()
        {
            // Noise plus gradients, so the encoder picks a mix of row filters;
            // alpha 0 with color under it, like the game's splat maps.
            const int w = 97, h = 61;
            var rng = new Random(7);
            var source = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    source[i] = (byte)(x * 2);
                    source[i + 1] = (byte)rng.Next(256);
                    source[i + 2] = (byte)(y * 3 + x);
                    source[i + 3] = (byte)(x % 3 == 0 ? 0 : 255);
                }
            Encode(source, w, h, SKAlphaType.Unpremul);

            using var reader = PngRowReader.Open(_path);
            Assert.That(reader, Is.Not.Null);
            Assert.That(reader.Width, Is.EqualTo(w));
            Assert.That(reader.Height, Is.EqualTo(h));

            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                reader.ReadRow(row);
                Assert.That(row, Is.EqualTo(new ArraySegment<byte>(source, y * w * 4, w * 4)), $"row {y}");
            }
        }

        [Test]
        public void ReadRow_FillsAlphaForOpaqueImages()
        {
            const int w = 16, h = 8;
            var source = new byte[w * h * 4];
            for (int i = 0; i < source.Length; i += 4)
            {
                source[i] = 10; source[i + 1] = 20; source[i + 2] = 30; source[i + 3] = 255;
            }
            Encode(source, w, h, SKAlphaType.Opaque);

            using var reader = PngRowReader.Open(_path);
            var row = new byte[w * 4];
            reader.ReadRow(row);

            Assert.That(row[0], Is.EqualTo(10));
            Assert.That(row[2], Is.EqualTo(30));
            Assert.That(row[3], Is.EqualTo(255));
        }

        [Test]
        public void Open_ReturnsNullForNonPng()
        {
            File.WriteAllText(_path, "not a png");

            Assert.That(PngRowReader.Open(_path), Is.Null);
        }

        private void Encode(byte[] rgba, int w, int h, SKAlphaType alpha)
        {
            var info = new SKImageInfo(w, h, SKColorType.Rgba8888, alpha);
            using var image = SKImage.FromPixelCopy(info, rgba);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(_path, data.ToArray());
        }
    }
}
