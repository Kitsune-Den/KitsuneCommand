using KitsuneCommand.Features;
using NUnit.Framework;

namespace KitsuneCommand.Tests.Features
{
    [TestFixture]
    public class TeleportCommandTests
    {
        // The game's teleportplayer rejects any coordinate int.TryParse can't read,
        // so a saved "123.4 61.0 -55.2" must never reach it with a decimal point.
        [TestCase(123.4f, 61.0f, -55.2f, "teleportplayer 171 123 61 -56")]
        [TestCase(-0.5f, 61.3f, 0.5f, "teleportplayer 171 -1 62 0")]
        [TestCase(10f, 60f, 20f, "teleportplayer 171 10 60 20")]
        public void Build_SendsWholeNumberCoordinates(float x, float y, float z, string expected)
        {
            Assert.That(TeleportCommand.Build(171, x, y, z), Is.EqualTo(expected));
        }

        [Test]
        public void Build_EveryCoordinateParsesAsInt()
        {
            var parts = TeleportCommand.Build(171, 1234.5f, 61.9f, -987.1f).Split(' ');

            Assert.That(parts, Has.Length.EqualTo(5));
            for (var i = 1; i < parts.Length; i++)
                Assert.That(int.TryParse(parts[i], out _), Is.True, parts[i]);
        }
    }
}
