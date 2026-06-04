using NUnit.Framework;
using KitsuneCommand.Core;

namespace KitsuneCommand.Tests.Core
{
    /// <summary>
    /// Covers the pure decision function. The platform-detection probe itself
    /// (PlatformHelper.IsWindows / DecideForCurrentHost) isn't unit-tested
    /// here because it just reads Environment.OSVersion.Platform — the
    /// useful test surface is the strategy mapping, which is pure.
    /// </summary>
    [TestFixture]
    public class OsRestartStrategyTests
    {
        [Test]
        public void Decide_OnWindows_SkipsSystemctlProbe()
        {
            var strategy = OsRestartStrategy.Decide(isWindows: true);

            Assert.That(strategy, Is.EqualTo(OsRestartStrategy.Kind.InGameShutdownOnly),
                "Windows must skip systemctl — NSSM handles the bounce on game exit.");
        }

        [Test]
        public void Decide_OnLinux_TriesSystemctlFirst()
        {
            var strategy = OsRestartStrategy.Decide(isWindows: false);

            Assert.That(strategy, Is.EqualTo(OsRestartStrategy.Kind.SystemctlThenInGameShutdown),
                "Linux must keep trying systemctl first; in-game shutdown is the fallback.");
        }
    }
}
