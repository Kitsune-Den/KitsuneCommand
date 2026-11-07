using System.Collections.Generic;
using System.Web.Http.Results;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using KitsuneCommand.Configuration;
using KitsuneCommand.Core;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Features;
using KitsuneCommand.Web.Controllers;
using KitsuneCommand.Web.Models;

namespace KitsuneCommand.Tests.Web
{
    [TestFixture]
    public class SafeZoneControllerTests
    {
        private static SafeZoneFeature NewFeature()
        {
            var feature = new SafeZoneFeature(new ModEventBus(), new ConfigManager(new AppSettings()),
                new Mock<ISettingsRepository>().Object);
            feature.LoadSettings();
            return feature;
        }

        private static SafeZoneController Controller(params IFeature[] features) =>
            new SafeZoneController(new FeatureManager(features));

        private static JObject Data(object result)
        {
            Assert.That(result, Is.InstanceOf<OkNegotiatedContentResult<ApiResponse<object>>>());
            var body = ((OkNegotiatedContentResult<ApiResponse<object>>)result).Content;
            Assert.That(body.Code, Is.EqualTo(200));
            return JObject.FromObject(body.Data);
        }

        [Test]
        public void Get_ReturnsTheZones()
        {
            var feature = NewFeature();
            feature.UpdateSettings(new SafeZoneSettings
            {
                Zones = new List<SafeZone> { new SafeZone { Id = "a", Name = "Town", MinX = 1, MaxX = 9, RemoveAnimals = true } },
            });

            var data = Data(Controller(feature).Get());

            Assert.That((bool)data["enabled"], Is.True);
            var zone = (JObject)data["zones"][0];
            Assert.That((string)zone["id"], Is.EqualTo("a"));
            Assert.That((string)zone["name"], Is.EqualTo("Town"));
            Assert.That((int)zone["maxX"], Is.EqualTo(9));
            Assert.That((bool)zone["removeAnimals"], Is.True);
            Assert.That((int)zone["removedCount"], Is.EqualTo(0));
        }

        [Test]
        public void Update_SavesAndReturnsTheNormalizedZones()
        {
            var feature = NewFeature();

            var data = Data(Controller(feature).Update(new SafeZoneSettings
            {
                Enabled = false,
                Zones = new List<SafeZone> { new SafeZone { Id = "a", MinX = 50, MaxX = -50 } },
            }));

            Assert.That((bool)data["enabled"], Is.False);
            Assert.That((int)data["zones"][0]["minX"], Is.EqualTo(-50));
            Assert.That(feature.Settings.Enabled, Is.False);
        }

        [Test]
        public void Update_WithNoBody_IsABadRequest()
        {
            Assert.That(Controller(NewFeature()).Update(null), Is.InstanceOf<BadRequestErrorMessageResult>());
        }

        [Test]
        public void MissingFeature_Returns404Envelope()
        {
            var controller = Controller();
            foreach (var result in new[] { controller.Get(), controller.Update(new SafeZoneSettings()) })
            {
                Assert.That(result, Is.InstanceOf<OkNegotiatedContentResult<ApiResponse>>());
                Assert.That(((OkNegotiatedContentResult<ApiResponse>)result).Content.Code, Is.EqualTo(404));
            }
        }
    }
}
