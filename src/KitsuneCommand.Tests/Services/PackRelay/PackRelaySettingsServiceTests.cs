// End-to-end settings tests against a real SQLite temp DB. Confirms
// the encryptor + repo + service compose correctly:
//   - Plaintext in, plaintext out (round trip)
//   - Disk inspection shows ciphertext NOT plaintext (the threat
//     model: someone with read access to the .db should not learn
//     the API token)
//   - Master key persists across service instances (a restart of
//     the panel must not invalidate stored credentials)
//   - Reset() wipes everything

using System;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using Dapper;
using KitsuneCommand.Data;
using KitsuneCommand.Data.Repositories;
using KitsuneCommand.Services.PackRelay;
using NUnit.Framework;

namespace KitsuneCommand.Tests.Services.PackRelay
{
    [TestFixture]
    public class PackRelaySettingsServiceTests
    {
        // RFC 8032 test-vector seed; same one used in Ed25519SignerTests.
        // Deterministic public key derivation lets us cross-check that
        // SetSigningKey stored the right thing.
        private static readonly byte[] TestSeed = FromHex(
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");

        private string _dbPath;
        private DbConnectionFactory _factory;
        private ISettingsRepository _kv;
        private IPackRelaySettingsRepository _repo;
        private PackRelaySettingsService _svc;

        [SetUp]
        public void SetUp()
        {
            _dbPath = TestDbFixture.CreateTempDatabase();
            _factory = TestDbFixture.CreateFactory(_dbPath);
            _kv = new SettingsRepository(_factory);
            _repo = new PackRelaySettingsRepository(_factory);
            _svc = new PackRelaySettingsService(_repo, _kv);
        }

        [TearDown]
        public void TearDown()
        {
            TestDbFixture.Cleanup(_dbPath);
        }

        // ---- Status ----

        [Test]
        public void GetStatus_BeforeAnyWrite_ReturnsEmpty()
        {
            var status = _svc.GetStatus();
            Assert.That(status.HasApiToken, Is.False);
            Assert.That(status.HasSigningKey, Is.False);
            Assert.That(status.SigningKeyPublic, Is.Null);
            Assert.That(status.PublicKeyId, Is.Null);
            Assert.That(status.PublisherSlug, Is.Null);
        }

        [Test]
        public void GetStatus_AfterTokenSet_HasApiTokenTrue_NeverLeaksPlaintext()
        {
            _svc.SetApiToken("kc-pat-abcdef123");
            var status = _svc.GetStatus();
            Assert.That(status.HasApiToken, Is.True);
            // The wire-safe Status type must not include the token
            // in any string field. Reflect over the public props to
            // make this future-proof.
            var props = typeof(PackRelaySettingsStatus).GetProperties()
                .Where(p => p.PropertyType == typeof(string));
            foreach (var p in props)
            {
                var v = (string)p.GetValue(status);
                Assert.That(
                    v == null || !v.Contains("kc-pat-abcdef123"),
                    "PackRelaySettingsStatus." + p.Name + " leaked the plaintext token.");
            }
        }

        [Test]
        public void GetStatus_AfterSigningKeySet_ExposesPublicKeyOnly()
        {
            _svc.SetSigningKey(TestSeed, "kitsune-den/test-key");
            var status = _svc.GetStatus();
            Assert.That(status.HasSigningKey, Is.True);
            Assert.That(status.PublicKeyId, Is.EqualTo("kitsune-den/test-key"));

            // Derived public key matches what the RFC 8032 vector says.
            var expectedPublic = Convert.ToBase64String(
                FromHex("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"));
            Assert.That(status.SigningKeyPublic, Is.EqualTo(expectedPublic));
        }

        // ---- Encryption-at-rest invariants ----

        [Test]
        public void SetApiToken_StoresCiphertext_NotPlaintext()
        {
            const string token = "kc-pat-secret-XYZ-12345";
            _svc.SetApiToken(token);

            // Bypass the service; read the raw BLOB straight from
            // SQLite and assert the plaintext bytes don't appear.
            using (var conn = new SQLiteConnection("Data Source=" + _dbPath + ";Version=3;"))
            {
                conn.Open();
                var blob = conn.QueryFirstOrDefault<byte[]>(
                    "SELECT api_token_encrypted FROM pack_relay_settings LIMIT 1");
                Assert.That(blob, Is.Not.Null);
                Assert.That(blob.Length, Is.GreaterThan(0));
                var asAscii = Encoding.UTF8.GetString(blob);
                Assert.That(
                    asAscii.Contains(token),
                    Is.False,
                    "Raw DB bytes for api_token_encrypted contained the plaintext token.");
            }
        }

        [Test]
        public void SetSigningKey_StoresCiphertext_NotPlaintext()
        {
            _svc.SetSigningKey(TestSeed, "kitsune-den/test-key");
            using (var conn = new SQLiteConnection("Data Source=" + _dbPath + ";Version=3;"))
            {
                conn.Open();
                var blob = conn.QueryFirstOrDefault<byte[]>(
                    "SELECT signing_key_encrypted FROM pack_relay_settings LIMIT 1");
                Assert.That(blob, Is.Not.Null);
                // The seed bytes don't appear in the encrypted blob.
                Assert.That(
                    IndexOf(blob, TestSeed),
                    Is.EqualTo(-1),
                    "Raw DB bytes contained the plaintext signing-key seed.");
            }
        }

        // ---- Round trip ----

        [Test]
        public void GetCredentials_RoundTrips_Token()
        {
            const string token = "round-trip-token";
            _svc.SetApiToken(token);
            _svc.SetSigningKey(TestSeed, "x/y");
            var creds = _svc.GetCredentials();
            Assert.That(creds.ApiToken, Is.EqualTo(token));
        }

        [Test]
        public void GetCredentials_RoundTrips_SigningKey()
        {
            _svc.SetApiToken("t");
            _svc.SetSigningKey(TestSeed, "x/y");
            var creds = _svc.GetCredentials();
            Assert.That(creds.SigningKeySeed, Is.EqualTo(TestSeed));
            Assert.That(creds.PublicKeyId, Is.EqualTo("x/y"));
        }

        [Test]
        public void GetCredentials_ThrowsWhenNotConfigured()
        {
            // Token only — no signing key.
            _svc.SetApiToken("t");
            Assert.Throws<InvalidOperationException>(() => _svc.GetCredentials());
        }

        // ---- Master-key persistence ----

        [Test]
        public void Credentials_SurviveAcrossServiceInstances()
        {
            // Simulates a panel restart: write through one service
            // instance, read through a fresh one. Master must be
            // persisted in the settings KV (not just held in
            // process memory) for this to work.
            const string token = "survives-restart";
            _svc.SetApiToken(token);
            _svc.SetSigningKey(TestSeed, "k/v");

            // Fresh service instance, same DB.
            var freshSvc = new PackRelaySettingsService(
                new PackRelaySettingsRepository(_factory),
                new SettingsRepository(_factory));
            var creds = freshSvc.GetCredentials();
            Assert.That(creds.ApiToken, Is.EqualTo(token));
            Assert.That(creds.SigningKeySeed, Is.EqualTo(TestSeed));
        }

        [Test]
        public void Reset_WipesEverything()
        {
            _svc.SetApiToken("t");
            _svc.SetSigningKey(TestSeed, "k/v");
            _svc.SetPublisherSlug("kitsune-den");
            _svc.Reset();

            var status = _svc.GetStatus();
            Assert.That(status.HasApiToken, Is.False);
            Assert.That(status.HasSigningKey, Is.False);
            Assert.That(status.PublisherSlug, Is.Null);

            // Master key wiped too — a fresh encrypt + decrypt cycle
            // must compose correctly against the NEW master that
            // GetOrCreateMaster generates on next access. Set both
            // credentials back so GetCredentials() can succeed
            // (it requires both — partial-config is an error state
            // tested elsewhere).
            _svc.SetApiToken("new-token-post-reset");
            _svc.SetSigningKey(TestSeed, "new/key");
            var creds = _svc.GetCredentials();
            Assert.That(creds.ApiToken, Is.EqualTo("new-token-post-reset"));
            Assert.That(creds.SigningKeySeed, Is.EqualTo(TestSeed));
            Assert.That(creds.PublicKeyId, Is.EqualTo("new/key"));
        }

        // ---- Validation ----

        [Test]
        public void SetApiToken_RejectsBlank()
        {
            Assert.Throws<ArgumentException>(() => _svc.SetApiToken(null));
            Assert.Throws<ArgumentException>(() => _svc.SetApiToken(""));
            Assert.Throws<ArgumentException>(() => _svc.SetApiToken("   "));
        }

        [Test]
        public void SetSigningKey_RejectsWrongLength()
        {
            Assert.Throws<ArgumentException>(
                () => _svc.SetSigningKey(new byte[31], "x/y"));
            Assert.Throws<ArgumentException>(
                () => _svc.SetSigningKey(new byte[33], "x/y"));
        }

        [Test]
        public void SetSigningKey_RejectsBlankKeyId()
        {
            Assert.Throws<ArgumentException>(
                () => _svc.SetSigningKey(TestSeed, ""));
        }

        // ---- Helpers ----

        private static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
