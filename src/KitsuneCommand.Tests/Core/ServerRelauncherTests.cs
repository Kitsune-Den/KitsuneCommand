using System;
using System.IO;
using NUnit.Framework;
using KitsuneCommand.Core;

namespace KitsuneCommand.Tests.Core
{
    /// <summary>
    /// Covers what the relauncher writes to disk and how it would start the
    /// helper. Actually spawning the helper (and the game) isn't unit-tested:
    /// that needs a live process to wait on and takes the full grace period.
    /// </summary>
    [TestFixture]
    public class ServerRelauncherTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kc-relaunch-tests-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static RelaunchPlan Plan() => new RelaunchPlan(
            4242, "/opt/7d2d/7DaysToDieServer.x86_64", "/opt/7d2d",
            new[] { "-configfile=serverconfig.xml", "-dedicated" });

        [Test]
        public void PrepareHelper_Windows_WritesPowerShellScriptAndArms()
        {
            var psi = ServerRelauncher.PrepareHelper(_dir, Plan(), isWindows: true);

            var script = Path.Combine(_dir, "relaunch-server.ps1");
            Assert.That(File.Exists(script), Is.True);
            Assert.That(File.ReadAllText(script), Does.Contain("$oldPid = 4242"));
            Assert.That(File.ReadAllBytes(script)[0], Is.EqualTo(0xEF), "Windows PowerShell needs the BOM for non-ASCII paths.");
            Assert.That(File.ReadAllText(Path.Combine(_dir, "relaunch.armed")), Is.EqualTo("4242"));

            Assert.That(psi.FileName, Is.EqualTo("powershell.exe"));
            Assert.That(psi.Arguments, Does.Contain("-ExecutionPolicy Bypass -File"));
            Assert.That(psi.Arguments, Does.Contain("relaunch-server.ps1"));
        }

        [Test]
        public void PrepareHelper_Linux_WritesShellScriptAndArms()
        {
            var psi = ServerRelauncher.PrepareHelper(_dir, Plan(), isWindows: false);

            var script = Path.Combine(_dir, "relaunch-server.sh");
            Assert.That(File.ReadAllBytes(script)[0], Is.EqualTo((byte)'#'), "A BOM would break the shebang line.");
            Assert.That(File.ReadAllText(script), Does.Contain("OLD_PID=4242\n"));
            Assert.That(File.Exists(Path.Combine(_dir, "relaunch.armed")), Is.True);

            Assert.That(psi.FileName, Is.EqualTo("/bin/sh"));
            Assert.That(psi.Arguments, Is.EqualTo(RelaunchPlan.QuoteShell(script)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PrepareHelper_StartsTheHelperDetachedFromTheGamesPipes(bool isWindows)
        {
            var psi = ServerRelauncher.PrepareHelper(_dir, Plan(), isWindows);

            Assert.That(psi.UseShellExecute, Is.False);
            Assert.That(psi.RedirectStandardOutput, Is.False, "A redirected pipe dies with the game and takes the helper with it.");
            Assert.That(psi.RedirectStandardError, Is.False);
            Assert.That(psi.CreateNoWindow, Is.True);
            Assert.That(psi.WorkingDirectory, Is.EqualTo(_dir));
        }

        [Test]
        public void PrepareHelper_DropsAnOversizedLogButKeepsANormalOne()
        {
            Directory.CreateDirectory(_dir);
            var log = Path.Combine(_dir, "relaunch.log");

            File.WriteAllText(log, "earlier attempt\n");
            ServerRelauncher.PrepareHelper(_dir, Plan(), isWindows: false);
            Assert.That(File.Exists(log), Is.True);

            File.WriteAllBytes(log, new byte[300 * 1024]);
            ServerRelauncher.PrepareHelper(_dir, Plan(), isWindows: false);
            Assert.That(File.Exists(log), Is.False);
        }

        [Test]
        public void Disarm_RemovesTheMarkerTheHelperChecks()
        {
            ServerRelauncher.PrepareHelper(_dir, Plan(), isWindows: false);

            ServerRelauncher.Disarm(_dir);

            Assert.That(File.Exists(Path.Combine(_dir, "relaunch.armed")), Is.False);
        }

        [Test]
        public void Disarm_WhenNothingIsArmed_DoesNothing()
        {
            Assert.DoesNotThrow(() => ServerRelauncher.Disarm(_dir));
        }
    }
}
