using System;
using NUnit.Framework;
using KitsuneCommand.Configuration;
using KitsuneCommand.Web;

namespace KitsuneCommand.Tests.Core
{
    /// <summary>
    /// The panel listener used to proxy to a hardcoded 8888 and listen on a
    /// hardcoded 8890, so moving WebUrl broke every /api/ call. These cover the
    /// port plumbing that replaced those constants.
    /// </summary>
    [TestFixture]
    public class WebPortsTests
    {
        [TestCase("http://*:8888", 8888)]
        [TestCase("http://+:9000/", 9000)]
        [TestCase("http://localhost:8888/api", 8888)]
        [TestCase("  http://0.0.0.0:18888  ", 18888)]
        [TestCase("http://[::1]:7777", 7777)]
        [TestCase("http://*", 80)]
        [TestCase("https://*/", 443)]
        [TestCase("http://[::]", 80)]
        public void PortFromUrl_ReadsWildcardAndExplicitHosts(string url, int expected)
        {
            Assert.That(WebPorts.PortFromUrl(url), Is.EqualTo(expected));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("*:8888")]
        [TestCase("http://*:notaport")]
        [TestCase("http://*:0")]
        [TestCase("http://*:70000")]
        [TestCase("ftp://*")]
        public void PortFromUrl_RejectsUnusableUrls(string url)
        {
            Assert.Throws<ArgumentException>(() => WebPorts.PortFromUrl(url));
        }

        [Test]
        public void ProxyBaseUrl_TargetsLoopbackOnTheApiPort()
        {
            Assert.That(WebPorts.ProxyBaseUrl("http://*:9888"), Is.EqualTo("http://127.0.0.1:9888"));
            Assert.That(WebPorts.ProxyBaseUrl("https://+:9443/"), Is.EqualTo("https://127.0.0.1:9443"));
        }

        [Test]
        public void Defaults_KeepTheStockPorts()
        {
            // Existing installs have no WebPanelPort key; they must land on the old ports.
            var settings = new AppSettings();
            Assert.That(WebPorts.PortFromUrl(settings.WebUrl), Is.EqualTo(8888));
            Assert.That(settings.WebPanelPort, Is.EqualTo(8890));
            Assert.That(settings.WebSocketPort, Is.EqualTo(8889));
            Assert.That(WebPorts.ProxyBaseUrl(settings.WebUrl), Is.EqualTo("http://127.0.0.1:8888"));
        }

        [Test]
        public void InjectPortMeta_AddsBothPortsBeforeHeadClose()
        {
            var html = "<html><head><title>KC</title></head><body></body></html>";

            var result = WebPorts.InjectPortMeta(html, 9890, 9889);

            Assert.That(result, Is.EqualTo(
                "<html><head><title>KC</title>"
                + "<meta name=\"kc-panel-port\" content=\"9890\">"
                + "<meta name=\"kc-ws-port\" content=\"9889\">"
                + "</head><body></body></html>"));
        }

        [Test]
        public void InjectPortMeta_LeavesHtmlWithoutHeadAlone()
        {
            Assert.That(WebPorts.InjectPortMeta("<p>hi</p>", 8890, 8889), Is.EqualTo("<p>hi</p>"));
        }
    }
}
