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

        // Process tree used below: 1 (systemd) → 500 (startserver.sh) → 600 (game).
        private static int ParentOf(int pid)
        {
            switch (pid)
            {
                case 600: return 500;
                case 500: return 1;
                default: return 0;
            }
        }

        [Test]
        public void IsSupervisedOnLinux_NotUnderSystemd_IsFalse()
        {
            Assert.That(ServerRelauncher.IsSupervisedOnLinux(null, null, 600, ParentOf), Is.False);
            Assert.That(ServerRelauncher.IsSupervisedOnLinux("", "600", 600, ParentOf), Is.False);
        }

        [Test]
        public void IsSupervisedOnLinux_GameIsTheUnitsProcess_IsTrue()
        {
            Assert.That(ServerRelauncher.IsSupervisedOnLinux("abc123", "600", 600, ParentOf), Is.True);
        }

        [Test]
        public void IsSupervisedOnLinux_UnitStartedAWrapperScript_IsTrue()
        {
            Assert.That(ServerRelauncher.IsSupervisedOnLinux("abc123", "500", 600, ParentOf), Is.True);
        }

        [Test]
        public void IsSupervisedOnLinux_SpawnedByAPanelService_IsFalse()
        {
            // The panel's unit started PID 400; the game was detached from it
            // (parent is now init) and only inherited the environment.
            Assert.That(ServerRelauncher.IsSupervisedOnLinux("abc123", "400", 700, pid => pid == 700 ? 1 : 0), Is.False,
                "systemd restarts the panel, not a game the panel launched.");
        }

        [Test]
        public void IsSupervisedOnLinux_OldSystemdWithoutExecPid_AssumesSupervised()
        {
            Assert.That(ServerRelauncher.IsSupervisedOnLinux("abc123", null, 600, ParentOf), Is.True);
            Assert.That(ServerRelauncher.IsSupervisedOnLinux("abc123", "garbage", 600, ParentOf), Is.True);
        }

        [Test]
        public void IsSupervisedOnWindows_OnlyInTheServicesSession()
        {
            Assert.That(ServerRelauncher.IsSupervisedOnWindows(0), Is.True);
            Assert.That(ServerRelauncher.IsSupervisedOnWindows(1), Is.False);
            Assert.That(ServerRelauncher.IsSupervisedOnWindows(-1), Is.False,
                "An unknown session must not be mistaken for a service.");
        }
    }
}
