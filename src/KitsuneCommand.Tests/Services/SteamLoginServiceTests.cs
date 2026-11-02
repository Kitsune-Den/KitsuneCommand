using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using NUnit.Framework;
using Moq;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Web.Auth;

namespace KitsuneCommand.Tests.Services
{
    [TestFixture]
    public class SteamLoginServiceTests
    {
        private const string BaseUrl = "https://panel.example.com";
        private const string SteamId = "76561197960287930";

        private Mock<IUserAccountRepository> _users;
        private InMemorySettings _settings;
        private List<string> _verificationBodies;
        private Func<string, string> _steamReply;
        private DateTime _now;
        private SteamLoginService _service;

        [SetUp]
        public void SetUp()
        {
            _users = new Mock<IUserAccountRepository>();
            _settings = new InMemorySettings();
            _verificationBodies = new List<string>();
            _steamReply = _ => "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n";
            _now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

            _service = new SteamLoginService(_users.Object, _settings,
                body => { _verificationBodies.Add(body); return _steamReply(body); },
                () => _now);
            _service.SaveSettings(new SteamLoginSettings { Enabled = true, PublicUrl = BaseUrl });
        }

        // ---- Settings ----

        [Test]
        public void SaveSettings_EnablingWithoutPublicUrl_Throws()
        {
            Assert.That(() => _service.SaveSettings(new SteamLoginSettings { Enabled = true, PublicUrl = "" }),
                Throws.ArgumentException);
        }

        [TestCase("https://panel.example.com/", "https://panel.example.com")]
        [TestCase("  http://203.0.113.5:8890  ", "http://203.0.113.5:8890")]
        [TestCase("https://example.com/kc/", "https://example.com/kc")]
        public void SaveSettings_NormalizesPublicUrl(string input, string expected)
        {
            var saved = _service.SaveSettings(new SteamLoginSettings { Enabled = true, PublicUrl = input });
            Assert.That(saved.PublicUrl, Is.EqualTo(expected));
            Assert.That(_service.GetSettings().PublicUrl, Is.EqualTo(expected));
        }

        [TestCase("panel.example.com")]
        [TestCase("ftp://panel.example.com")]
        [TestCase("https://panel.example.com/?x=1")]
        [TestCase("https://panel.example.com/#/login")]
        [TestCase("https://user:pw@panel.example.com")]
        public void SaveSettings_RejectsUnusablePublicUrl(string input)
        {
            Assert.That(() => _service.SaveSettings(new SteamLoginSettings { Enabled = false, PublicUrl = input }),
                Throws.ArgumentException);
        }

        [Test]
        public void IsEnabled_FalseUntilAnAdminTurnsItOn()
        {
            var fresh = new SteamLoginService(_users.Object, new InMemorySettings(), _ => "", () => _now);
            Assert.That(fresh.IsEnabled(), Is.False);
            Assert.That(fresh.BeginLogin(), Is.Null);
        }

        // ---- Starting ----

        [Test]
        public void BeginLogin_BuildsSteamUrlThatReturnsToOurCallback()
        {
            var start = _service.BeginLogin();
            var q = SteamLoginService.ParseQuery(new Uri(start.RedirectUrl).Query);

            Assert.That(start.RedirectUrl, Does.StartWith(SteamLoginService.SteamOpenIdEndpoint + "?"));
            Assert.That(q["openid.mode"], Is.EqualTo("checkid_setup"));
            Assert.That(q["openid.return_to"], Is.EqualTo(BaseUrl + "/api/auth/steam/callback?state=" + start.State));
            Assert.That(q["openid.realm"], Is.EqualTo(BaseUrl + "/"));
            Assert.That(q["openid.claimed_id"], Is.EqualTo("http://specs.openid.net/auth/2.0/identifier_select"));
            Assert.That(start.SecureCookie, Is.True);
        }

        [Test]
        public void BeginLogin_StatesAreUnique()
        {
            Assert.That(_service.BeginLogin().State, Is.Not.EqualTo(_service.BeginLogin().State));
        }

        // ---- Login callback ----

        [Test]
        public void Login_HappyPath_IssuesSingleUseCode()
        {
            var account = LinkedAccount();
            var start = _service.BeginLogin();

            var redirect = _service.HandleCallback(Assertion(start), start.State);

            Assert.That(redirect, Does.StartWith(BaseUrl + "/#/login?steam_code="));
            var code = redirect.Substring(redirect.IndexOf("steam_code=", StringComparison.Ordinal) + "steam_code=".Length);

            Assert.That(_service.RedeemCode(code)?.Id, Is.EqualTo(account.Id));
            Assert.That(_service.RedeemCode(code), Is.Null, "codes are single-use");
            _users.Verify(r => r.UpdateLastLogin(account.Username), Times.Once);
        }

        [Test]
        public void Login_AsksSteamToCheckTheExactAssertion()
        {
            LinkedAccount();
            var start = _service.BeginLogin();
            var assertion = Assertion(start);

            _service.HandleCallback(assertion, start.State);

            Assert.That(_verificationBodies, Has.Count.EqualTo(1));
            var sent = SteamLoginService.ParseQuery(_verificationBodies[0]);
            Assert.That(sent["openid.mode"], Is.EqualTo("check_authentication"));
            Assert.That(sent["openid.sig"], Is.EqualTo(assertion["openid.sig"]));
            Assert.That(sent["openid.return_to"], Is.EqualTo(assertion["openid.return_to"]));
            Assert.That(sent["state"], Is.Null, "only openid.* fields are echoed");
        }

        [Test]
        public void Login_UnlinkedSteamAccount_RedirectsWithNotLinked()
        {
            var start = _service.BeginLogin();
            Assert.That(_service.HandleCallback(Assertion(start), start.State),
                Is.EqualTo(BaseUrl + "/#/login?steam_error=not_linked"));
        }

        [Test]
        public void Callback_WithoutMatchingCookie_IsRejectedBeforeAskingSteam()
        {
            LinkedAccount();
            var start = _service.BeginLogin();

            Assert.That(_service.HandleCallback(Assertion(start), "someone-elses-state"),
                Is.EqualTo(BaseUrl + "/#/login?steam_error=session_mismatch"));
            Assert.That(_service.HandleCallback(Assertion(start), null), Does.EndWith("steam_error=expired"),
                "the state was consumed by the first attempt");
            Assert.That(_verificationBodies, Is.Empty);
        }

        [Test]
        public void Callback_UnknownState_IsExpired()
        {
            var q = Assertion(_service.BeginLogin());
            q["state"] = "made-up";
            Assert.That(_service.HandleCallback(q, "made-up"), Is.EqualTo(BaseUrl + "/#/login?steam_error=expired"));
        }

        [Test]
        public void Callback_ReplayedAssertion_IsRejected()
        {
            LinkedAccount();
            var start = _service.BeginLogin();
            var assertion = Assertion(start);

            _service.HandleCallback(assertion, start.State);

            Assert.That(_service.HandleCallback(assertion, start.State), Does.EndWith("steam_error=expired"));
            Assert.That(_verificationBodies, Has.Count.EqualTo(1));
        }

        [Test]
        public void Callback_AfterStateLifetime_IsExpired()
        {
            var start = _service.BeginLogin();
            _now += SteamLoginService.StateLifetime + TimeSpan.FromSeconds(1);
            Assert.That(_service.HandleCallback(Assertion(start), start.State), Does.EndWith("steam_error=expired"));
        }

        [Test]
        public void Callback_AfterAdminTurnsSteamOff_IsDisabled()
        {
            var start = _service.BeginLogin();
            _service.SaveSettings(new SteamLoginSettings { Enabled = false, PublicUrl = BaseUrl });
            Assert.That(_service.HandleCallback(Assertion(start), start.State), Does.EndWith("steam_error=disabled"));
        }

        [Test]
        public void Callback_UserCancelledOnSteam()
        {
            var start = _service.BeginLogin();
            var q = new NameValueCollection { ["state"] = start.State, ["openid.mode"] = "cancel" };
            Assert.That(_service.HandleCallback(q, start.State), Does.EndWith("steam_error=cancelled"));
        }

        [Test]
        public void Callback_SteamSaysInvalid_IsRejected()
        {
            LinkedAccount();
            _steamReply = _ => "ns:http://specs.openid.net/auth/2.0\nis_valid:false\n";
            var start = _service.BeginLogin();
            Assert.That(_service.HandleCallback(Assertion(start), start.State), Does.EndWith("steam_error=invalid"));
        }

        [Test]
        public void Callback_SteamUnreachable_IsReported()
        {
            LinkedAccount();
            _steamReply = _ => throw new System.Net.WebException("timeout");
            var start = _service.BeginLogin();
            Assert.That(_service.HandleCallback(Assertion(start), start.State), Does.EndWith("steam_error=steam_unreachable"));
        }

        [TestCase("openid.return_to", "https://evil.example/api/auth/steam/callback?state=x")]
        [TestCase("openid.op_endpoint", "https://evil.example/openid/login")]
        [TestCase("openid.claimed_id", "https://steamcommunity.com/openid/id/76561197960287931")]
        [TestCase("openid.signed", "signed,op_endpoint,claimed_id,identity,response_nonce,assoc_handle")]
        [TestCase("openid.mode", "check_authentication")]
        public void Callback_TamperedAssertion_IsRejectedBeforeAskingSteam(string field, string value)
        {
            LinkedAccount();
            var start = _service.BeginLogin();
            var q = Assertion(start);
            q[field] = value;

            Assert.That(_service.HandleCallback(q, start.State), Does.EndWith("steam_error=invalid"));
            Assert.That(_verificationBodies, Is.Empty);
        }

        [Test]
        public void Callback_NonSteamIdentity_IsRejected()
        {
            var start = _service.BeginLogin();
            var q = Assertion(start);
            q["openid.claimed_id"] = q["openid.identity"] = "https://steamcommunity.com/openid/id/123";
            Assert.That(_service.HandleCallback(q, start.State), Does.EndWith("steam_error=invalid"));
        }

        // ---- Codes ----

        [Test]
        public void RedeemCode_AfterCodeLifetime_ReturnsNull()
        {
            LinkedAccount();
            var code = LoginAndGetCode();
            _now += SteamLoginService.CodeLifetime + TimeSpan.FromSeconds(1);
            Assert.That(_service.RedeemCode(code), Is.Null);
        }

        [Test]
        public void RedeemCode_WhenUnlinkedMeanwhile_ReturnsNull()
        {
            var account = LinkedAccount();
            var code = LoginAndGetCode();
            account.SteamId = null;
            Assert.That(_service.RedeemCode(code), Is.Null);
        }

        [Test]
        public void RedeemCode_WhenDeactivatedMeanwhile_ReturnsNull()
        {
            var account = LinkedAccount();
            var code = LoginAndGetCode();
            account.IsActive = false;
            Assert.That(_service.RedeemCode(code), Is.Null);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("never-issued")]
        public void RedeemCode_Unknown_ReturnsNull(string code)
        {
            Assert.That(_service.RedeemCode(code), Is.Null);
        }

        // ---- Linking ----

        [Test]
        public void Link_HappyPath_StoresSteamId()
        {
            var user = new UserAccount { Id = 7, Username = "mod1", Role = "moderator", IsActive = true };
            _users.Setup(r => r.GetById(7)).Returns(user);
            var start = _service.BeginLink(7);

            var redirect = _service.HandleCallback(Assertion(start), start.State);

            Assert.That(redirect, Is.EqualTo(BaseUrl + "/#/settings?steam_linked=1"));
            _users.Verify(r => r.SetSteamId(7, SteamId), Times.Once);
        }

        [Test]
        public void Link_SteamAccountHeldByAnotherUser_IsRefused()
        {
            _users.Setup(r => r.GetById(7)).Returns(new UserAccount { Id = 7, Username = "mod1", IsActive = true });
            _users.Setup(r => r.GetBySteamId(SteamId)).Returns(new UserAccount { Id = 8, Username = "other", IsActive = true });
            var start = _service.BeginLink(7);

            Assert.That(_service.HandleCallback(Assertion(start), start.State),
                Is.EqualTo(BaseUrl + "/#/settings?steam_error=already_linked"));
            _users.Verify(r => r.SetSteamId(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void Link_UniqueIndexViolation_IsReportedAsAlreadyLinked()
        {
            _users.Setup(r => r.GetById(7)).Returns(new UserAccount { Id = 7, Username = "mod1", IsActive = true });
            _users.Setup(r => r.SetSteamId(7, SteamId)).Throws(new InvalidOperationException("UNIQUE constraint failed"));
            var start = _service.BeginLink(7);

            Assert.That(_service.HandleCallback(Assertion(start), start.State), Does.EndWith("steam_error=already_linked"));
        }

        [Test]
        public void Link_ErrorsReturnToSettings()
        {
            _users.Setup(r => r.GetById(7)).Returns(new UserAccount { Id = 7, Username = "mod1", IsActive = true });
            var start = _service.BeginLink(7);
            Assert.That(_service.HandleCallback(Assertion(start), "wrong"),
                Is.EqualTo(BaseUrl + "/#/settings?steam_error=session_mismatch"));
        }

        // ---- Parsing ----

        [Test]
        public void ParseQuery_DecodesPercentAndPlus()
        {
            var q = SteamLoginService.ParseQuery("?openid.sig=ab%2Bc%2F%3D&name=a+b&flag");
            Assert.That(q["openid.sig"], Is.EqualTo("ab+c/="));
            Assert.That(q["name"], Is.EqualTo("a b"));
            Assert.That(q["flag"], Is.EqualTo(""));
        }

        [TestCase("ns:http://specs.openid.net/auth/2.0\nis_valid:true\n", true)]
        [TestCase("ns:http://specs.openid.net/auth/2.0\r\nis_valid:true\r\n", true)]
        [TestCase("ns:http://specs.openid.net/auth/2.0\nis_valid:false\n", false)]
        [TestCase("<html>is_valid:true</html>", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsValidVerificationResponse(string response, bool expected)
        {
            Assert.That(SteamLoginService.IsValidVerificationResponse(response), Is.EqualTo(expected));
        }

        // ---- Helpers ----

        private UserAccount LinkedAccount()
        {
            var account = new UserAccount { Id = 3, Username = "admin", Role = "admin", IsActive = true, SteamId = SteamId };
            _users.Setup(r => r.GetBySteamId(SteamId)).Returns(() => account.IsActive && account.SteamId == SteamId ? account : null);
            _users.Setup(r => r.GetById(3)).Returns(account);
            return account;
        }

        private string LoginAndGetCode()
        {
            var start = _service.BeginLogin();
            var redirect = _service.HandleCallback(Assertion(start), start.State);
            return redirect.Substring(redirect.IndexOf("steam_code=", StringComparison.Ordinal) + "steam_code=".Length);
        }

        private static int _nonce;

        /// <summary>What Steam sends back for a successful sign-in of <see cref="SteamId"/>.</summary>
        private static NameValueCollection Assertion(SteamAuthStart start)
        {
            var returnTo = SteamLoginService.ParseQuery(new Uri(start.RedirectUrl).Query)["openid.return_to"];
            var claimed = "https://steamcommunity.com/openid/id/" + SteamId;
            return new NameValueCollection
            {
                ["state"] = start.State,
                ["openid.ns"] = "http://specs.openid.net/auth/2.0",
                ["openid.mode"] = "id_res",
                ["openid.op_endpoint"] = SteamLoginService.SteamOpenIdEndpoint,
                ["openid.claimed_id"] = claimed,
                ["openid.identity"] = claimed,
                ["openid.return_to"] = returnTo,
                ["openid.response_nonce"] = "2026-10-23T12:00:00Z" + (++_nonce),
                ["openid.assoc_handle"] = "1234567890",
                ["openid.signed"] = "signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
                ["openid.sig"] = "W0u5DRbtHE1GG0ZKXjerUZDUGmc=",
            };
        }

        private class InMemorySettings : ISettingsRepository
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
            public string Get(string name) => _values.TryGetValue(name, out var v) ? v : null;
            public void Set(string name, string value) => _values[name] = value;
        }
    }
}
