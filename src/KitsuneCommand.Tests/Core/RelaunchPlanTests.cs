using System;
using NUnit.Framework;
using KitsuneCommand.Core;

namespace KitsuneCommand.Tests.Core
{
    [TestFixture]
    public class RelaunchPlanTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 13, 4, 0, 7);

        private static readonly string[] WindowsCommandLine =
        {
            "7DaysToDieServer",
            "-logfile", @"C:\7D2D Server\7DaysToDieServer_Data\output_log_dedicated__2026-11-12__18-30-00.txt",
            "-quit", "-batchmode", "-nographics", "-configfile=serverconfig.xml", "-dedicated",
        };

        private static RelaunchPlan WindowsPlan() => RelaunchPlan.FromCommandLine(
            4242, @"C:\7D2D Server\7DaysToDieServer.exe", @"C:\7D2D Server", WindowsCommandLine, Now);

        [Test]
        public void FromCommandLine_DropsProgramNameAndKeepsArgumentOrder()
        {
            var plan = WindowsPlan();

            Assert.That(plan.Arguments, Has.Count.EqualTo(7));
            Assert.That(plan.Arguments[0], Is.EqualTo("-logfile"));
            Assert.That(plan.Arguments[6], Is.EqualTo("-dedicated"));
        }

        [Test]
        public void FromCommandLine_GivesTheRelaunchedServerAFreshLogFile()
        {
            var plan = WindowsPlan();

            Assert.That(plan.Arguments[1],
                Is.EqualTo(@"C:\7D2D Server\7DaysToDieServer_Data\output_log_dedicated__2026-11-13__04-00-07.txt"));
        }

        [Test]
        public void RefreshLogTimestamp_LeavesUnstampedNamesAlone()
        {
            Assert.That(RelaunchPlan.RefreshLogTimestamp("/srv/7d2d/server.log", Now), Is.EqualTo("/srv/7d2d/server.log"));
        }

        [Test]
        public void InstanceMarker_IsTheConfigFileArgument()
        {
            Assert.That(WindowsPlan().InstanceMarker, Is.EqualTo("-configfile=serverconfig.xml"));

            var bare = new RelaunchPlan(1, "/srv/7d2d/7DaysToDieServer.x86_64", "/srv/7d2d", new[] { "-dedicated" });
            Assert.That(bare.InstanceMarker, Is.Empty);
        }

        [TestCase("-dedicated", "-dedicated")]
        [TestCase("", "\"\"")]
        [TestCase(@"C:\7D2D Server\log.txt", "\"C:\\7D2D Server\\log.txt\"")]
        [TestCase(@"C:\My Dir\", "\"C:\\My Dir\\\\\"")]
        [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
        public void QuoteWindowsArg_FollowsCommandLineRules(string arg, string expected)
        {
            Assert.That(RelaunchPlan.QuoteWindowsArg(arg), Is.EqualTo(expected));
        }

        [Test]
        public void Constructor_RejectsAPlanThatCouldNotBeLaunched()
        {
            Assert.Throws<ArgumentException>(() => new RelaunchPlan(1, "", "/srv/7d2d", null));
            Assert.Throws<ArgumentException>(() => new RelaunchPlan(1, "/srv/7d2d/server", " ", null));
        }

        [Test]
        public void FromCommandLine_WithNoArguments_RendersABareLaunch()
        {
            var plan = RelaunchPlan.FromCommandLine(9, "/srv/7d2d/server", "/srv/7d2d", null, Now);

            Assert.That(plan.Arguments, Is.Empty);
            Assert.That(plan.RenderLinuxScript("/a", "/l"), Does.Contain("nohup \"$EXE\" </dev/null"));
            Assert.That(plan.RenderWindowsScript("a", "l"), Does.Contain("$argLine = ''"));
        }

        [Test]
        public void QuotePowerShell_DoublesSingleQuotes()
        {
            Assert.That(RelaunchPlan.QuotePowerShell(@"C:\Ada's Server"), Is.EqualTo(@"'C:\Ada''s Server'"));
        }

        [Test]
        public void QuoteShell_EscapesSingleQuotes()
        {
            Assert.That(RelaunchPlan.QuoteShell("/home/ada/it's here"), Is.EqualTo(@"'/home/ada/it'\''s here'"));
        }

        [Test]
        public void RenderWindowsScript_EmbedsThePlan()
        {
            var script = WindowsPlan().RenderWindowsScript(@"C:\data\relaunch.armed", @"C:\data\relaunch.log");

            Assert.That(script, Does.Contain("$oldPid = 4242"));
            Assert.That(script, Does.Contain(@"$exe = 'C:\7D2D Server\7DaysToDieServer.exe'"));
            Assert.That(script, Does.Contain(@"$workDir = 'C:\7D2D Server'"));
            Assert.That(script, Does.Contain("$marker = '-configfile=serverconfig.xml'"));
            Assert.That(script, Does.Contain(
                "$argLine = '-logfile \"C:\\7D2D Server\\7DaysToDieServer_Data\\output_log_dedicated__2026-11-13__04-00-07.txt\" -quit"));
            Assert.That(script, Does.Contain("Start-Process -FilePath $exe"));
        }

        [Test]
        public void RenderLinuxScript_EmbedsThePlanWithUnixLineEndings()
        {
            var plan = RelaunchPlan.FromCommandLine(
                777, "/home/ada/7d2d server/7DaysToDieServer.x86_64", "/home/ada/7d2d server",
                new[]
                {
                    "./7DaysToDieServer.x86_64",
                    "-logfile", "/home/ada/7d2d server/output_log__2026-11-12__18-30-00.txt",
                    "-quit", "-batchmode", "-nographics", "-dedicated", "-configfile=serverconfig.xml",
                },
                Now);

            var script = plan.RenderLinuxScript("/data/relaunch.armed", "/data/relaunch.log");

            Assert.That(script, Does.StartWith("#!/bin/sh\n"));
            Assert.That(script, Does.Not.Contain("\r"), "sh chokes on CRLF.");
            Assert.That(script, Does.Contain("OLD_PID=777\n"));
            Assert.That(script, Does.Contain("WORKDIR='/home/ada/7d2d server'\n"));
            Assert.That(script, Does.Contain(
                "nohup \"$EXE\" '-logfile' '/home/ada/7d2d server/output_log__2026-11-13__04-00-07.txt' '-quit' '-batchmode' '-nographics' '-dedicated' '-configfile=serverconfig.xml' </dev/null >/dev/null 2>&1 &\n"));
        }
    }
}
