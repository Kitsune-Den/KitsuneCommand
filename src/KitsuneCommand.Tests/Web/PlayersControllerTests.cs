using NUnit.Framework;
using KitsuneCommand.Web.Controllers;

namespace KitsuneCommand.Tests.Web
{
    [TestFixture]
    public class PlayersControllerTests
    {
        [TestCase(0, 0, true)]
        [TestCase(1, 1, true)]
        [TestCase(1000, 1000, true)]
        [TestCase(5000, 1000, true)]
        [TestCase(1000, 0, false)] // a Steam group still makes them an admin
        [TestCase(1, 0, false)]
        [TestCase(0, 1000, false)]
        public void AdminLevelApplied_ComparesWhatTheGameReports(int requested, int effective, bool expected)
        {
            Assert.That(PlayersController.AdminLevelApplied(requested, effective), Is.EqualTo(expected));
        }
    }
}
