using System.IO;
using NUnit.Framework;
using KitsuneCommand.GameIntegration;

namespace KitsuneCommand.Tests.Core
{
    /// <summary>
    /// Inside the game, GamePaths.InstallDir asks GameIO for the install folder.
    /// Tests run against a reference-only game assembly, so these cover the
    /// fallback and that the game call failing can't take callers down with it.
    /// </summary>
    [TestFixture]
    public class GamePathsTests
    {
        [Test]
        public void FromModPath_IsTwoFoldersAboveTheMod()
        {
            var mod = Path.Combine(Path.GetTempPath(), "7DaysToDieServer", "Mods", "KitsuneCommand");

            Assert.That(GamePaths.FromModPath(mod),
                Is.EqualTo(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "7DaysToDieServer"))));
        }

        [Test]
        public void FromModPath_ToleratesAMissingModPath()
        {
            Assert.DoesNotThrow(() => GamePaths.FromModPath(null));
        }

        [Test]
        public void InstallDir_FallsBackWhenTheGameCantAnswer()
        {
            // GameIO here is a reference-assembly stub that can't run, the same
            // situation as the game not being initialized yet.
            string dir = null;
            Assert.DoesNotThrow(() => dir = GamePaths.InstallDir);
            Assert.That(dir, Is.Not.Null.And.Not.Empty);
        }
    }
}
