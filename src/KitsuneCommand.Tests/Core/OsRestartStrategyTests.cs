using NUnit.Framework;
using KitsuneCommand.Core;

namespace KitsuneCommand.Tests.Core
{
    /// <summary>
    /// Covers the pure decision functions. The host probes themselves
    /// (PlatformHelper.IsWindows, the session-id P/Invoke, the environment
    /// read) aren't unit-tested here — the useful test surface is the
    /// mapping from those facts to a strategy, which is pure.
    /// </summary>
    [TestFixture]
    public class OsRestartStrategyTests
    {
        [Test]
        public void Decide_WindowsService_SkipsSystemctlProbe()
        {
            var strategy = OsRestartStrategy.Decide(isWindows: true, isSupervised: true);

            Assert.That(strategy, Is.EqualTo(OsRestartStrategy.Kind.InGameShutdownOnly),
                "Windows must skip systemctl — NSSM handles the bounce on game exit.");
        }

        [Test]
        public void Decide_LinuxSystemd_TriesSystemctlFirst()
        {
            var strategy = OsRestartStrategy.Decide(isWindows: false, isSupervised: true);

            Assert.That(strategy, Is.EqualTo(OsRestartStrategy.Kind.SystemctlThenInGameShutdown),
                "Linux must keep trying systemctl first; in-game shutdown is the fallback.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Decide_Unsupervised_RelaunchesItself(bool isWindows)
        {
            var strategy = OsRestartStrategy.Decide(isWindows, isSupervised: false);

            Assert.That(strategy, Is.EqualTo(OsRestartStrategy.Kind.SelfRelaunch),
                "With no supervisor, nothing else will start the server again.");
        }

        [Test]
        public void IsSupervised_Linux_FollowsSystemdInvocationId()
        {
            Assert.That(ServerRelauncher.IsSupervised(false, "0123456789abcdef", -1), Is.True);
            Assert.That(ServerRelauncher.IsSupervised(false, null, -1), Is.False);
            Assert.That(ServerRelauncher.IsSupervised(false, "", -1), Is.False);
        }

        [Test]
        public void IsSupervised_Windows_OnlyInTheServicesSession()
        {
            Assert.That(ServerRelauncher.IsSupervised(true, null, 0), Is.True);
            Assert.That(ServerRelauncher.IsSupervised(true, null, 1), Is.False);
            Assert.That(ServerRelauncher.IsSupervised(true, null, -1), Is.False,
                "An unknown session must not be mistaken for a service.");
            Assert.That(ServerRelauncher.IsSupervised(true, "leaked-from-wsl", 2), Is.False,
                "INVOCATION_ID means nothing on Windows.");
        }
    }
}
