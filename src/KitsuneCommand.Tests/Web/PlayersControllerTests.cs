using System.Net;
using System.Web.Http;
using System.Web.Http.Results;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using KitsuneCommand.Abstractions.Models;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Controllers;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Tests.Web
{
    [TestFixture]
    public class PlayersControllerTests
    {
        private const int EntityId = 42;

        private Mock<LivePlayerManager> _manager;
        private PlayersController _controller;

        [SetUp]
        public void SetUp()
        {
            var bus = new ModEventBus();
            _manager = new Mock<LivePlayerManager>(bus);
            _manager.Setup(m => m.GetByEntityId(EntityId)).Returns(new PlayerInfo { PlayerName = "Alice" });
            _controller = new PlayersController(_manager.Object, new Mock<IPointsRepository>().Object, bus);
        }

        private IHttpActionResult SetLevel(int requested, int? effective)
        {
            _manager.Setup(m => m.SetAdminLevel(EntityId, requested)).Returns(effective);
            return _controller.SetAdminLevel(EntityId, new SetAdminLevelRequest { Level = requested });
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(1000, 1000)]
        [TestCase(5000, 1000)]
        public void SetAdminLevel_ReturnsTheLevelNowInEffect(int requested, int effective)
        {
            var result = SetLevel(requested, effective);

            Assert.That(result.GetType().GetGenericTypeDefinition(), Is.EqualTo(typeof(OkNegotiatedContentResult<>)));
            var content = JObject.FromObject(result.GetType().GetProperty("Content").GetValue(result));
            Assert.That(content["Data"]["level"].Value<int>(), Is.EqualTo(effective));
        }

        [Test]
        public void SetAdminLevel_SaysSoWhenASteamGroupHoldsTheLevel()
        {
            var result = SetLevel(1000, 0);

            var conflict = result as NegotiatedContentResult<ApiResponse>;
            Assert.That(conflict, Is.Not.Null);
            Assert.That(conflict.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(conflict.Content.Code, Is.EqualTo(409));
            Assert.That(conflict.Content.Message, Does.Contain("Alice").And.Contain("level 0").And.Contain("Steam group"));
        }

        [Test]
        public void SetAdminLevel_IsNotFoundForAPlayerWhoLeftBeforeTheChange()
        {
            Assert.That(SetLevel(0, null), Is.InstanceOf<NotFoundResult>());
        }

        [Test]
        public void SetAdminLevel_IsNotFoundForAPlayerWhoIsNotOnline()
        {
            var result = _controller.SetAdminLevel(7, new SetAdminLevelRequest { Level = 0 });

            Assert.That(result, Is.InstanceOf<NotFoundResult>());
            _manager.Verify(m => m.SetAdminLevel(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Test]
        public void SetAdminLevel_RejectsAMissingBody()
        {
            var result = _controller.SetAdminLevel(EntityId, null);

            Assert.That(result, Is.InstanceOf<BadRequestErrorMessageResult>());
            _manager.Verify(m => m.SetAdminLevel(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

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
