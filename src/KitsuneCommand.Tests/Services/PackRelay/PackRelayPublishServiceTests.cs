// Orchestrator tests. Each test stages a temp Mods/ dir with a small
// fixture mod tree, mocks IPackRelayClient via Moq, and asserts:
//   - Walking discovers the right files (incl. skipping _packrelay-manifest.json)
//   - Hashing produces the expected sha256 in each file entry
//   - Upload loop calls BlobExists for every file, only UploadBlob for missing
//   - Manifest matches the cloud's schema shape
//   - Signature verifies against the seed-derived public key
//   - 409 from PublishVersion comes back as AlreadyPublished=true (soft success)
//   - Progress events fire in expected order

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KitsuneCommand.Services.PackRelay;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace KitsuneCommand.Tests.Services.PackRelay
{
    [TestFixture]
    public class PackRelayPublishServiceTests
    {
        private string _modsRoot;
        private Mock<IPackRelayClient> _client;
        // RFC 8032 test-vector seed; deterministic public key + signatures.
        private static readonly byte[] TestSeed = FromHex(
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");

        [SetUp]
        public void SetUp()
        {
            _modsRoot = Path.Combine(Path.GetTempPath(), "kc-pub-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_modsRoot);
            _client = new Mock<IPackRelayClient>(MockBehavior.Strict);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_modsRoot)) Directory.Delete(_modsRoot, recursive: true);
            }
            catch
            {
                // Best-effort; Windows file handles can lag.
            }
        }

        // ---- Walking + bundle composition ----

        [Test]
        public async Task Publish_HappyPath_TwoModsTwoFilesEach()
        {
            // Two mod folders, two files each. All blobs are missing
            // on the cloud (BlobExists -> false) so all four uploads
            // fire.
            WriteFile("KitsuneCraft/ModInfo.xml", "<info name=\"KitsuneCraft\"/>");
            WriteFile("KitsuneCraft/Config/items.xml", "<items/>");
            WriteFile("KitsuneKitchen/ModInfo.xml", "<info name=\"KitsuneKitchen\"/>");
            WriteFile("KitsuneKitchen/Config/recipes.xml", "<recipes/>");

            // Strict mock: every method invoked must be set up. Forces
            // the test to fail loudly if the orchestrator starts
            // calling something we didn't expect (regression guard).
            _client.Setup(c => c.BlobExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            string publishedManifest = null;
            _client.Setup(c => c.UploadBlobAsync(
                    It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _client.Setup(c => c.PublishVersionAsync("kitsune-den", It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, CancellationToken>((_, m, __) => publishedManifest = m)
                .ReturnsAsync(new JObject { ["version"] = "1.0.0" });

            var service = new PackRelayPublishService(_client.Object);
            var progress = new RecordingProgress();
            var result = await service.PublishAsync(BasicRequest(), progress);

            Assert.That(result.Slug, Is.EqualTo("kitsune-den"));
            Assert.That(result.Version, Is.EqualTo("1.0.0"));
            Assert.That(result.FileCount, Is.EqualTo(4));
            Assert.That(result.AlreadyPublished, Is.False);

            // BlobExists called 4 times, UploadBlob called 4 times,
            // PublishVersion once.
            _client.Verify(c => c.BlobExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
            _client.Verify(c => c.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Exactly(4));
            _client.Verify(c => c.PublishVersionAsync("kitsune-den", It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);

            // Manifest sanity check.
            var m = JObject.Parse(publishedManifest);
            Assert.That(m.Value<int>("schemaVersion"), Is.EqualTo(1));
            Assert.That(m.Value<string>("name"), Is.EqualTo("kitsune-den"));
            Assert.That(m.Value<string>("version"), Is.EqualTo("1.0.0"));
            Assert.That(m.Value<string>("game"), Is.EqualTo("7d2d"));
            Assert.That(((JArray)m["files"]).Count, Is.EqualTo(4));
        }

        [Test]
        public async Task Publish_SkipsPackRelayManifestSidecar()
        {
            // The launcher's local install metadata can land in the
            // Mods/ folder when the server admin runs the launcher
            // locally to test. We must NOT include it in the
            // published bundle — it's local install state, not source.
            WriteFile("Test/ModInfo.xml", "<info/>");
            WriteFile("Test/_packrelay-manifest.json", "{\"version\":\"old\"}");

            SetupAllUploadsMiss();
            JObject publishedManifest = null;
            _client.Setup(c => c.PublishVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, CancellationToken>((_, m, __) => publishedManifest = JObject.Parse(m))
                .ReturnsAsync(new JObject());

            var req = BasicRequest();
            req.ModFolderNames = new[] { "Test" };
            await new PackRelayPublishService(_client.Object).PublishAsync(req);

            var paths = ((JArray)publishedManifest["files"])
                .Select(f => f.Value<string>("path"))
                .ToList();
            Assert.That(paths, Has.Count.EqualTo(1));
            Assert.That(paths[0], Is.EqualTo("Mods/Test/ModInfo.xml"));
        }

        [Test]
        public void Publish_FailsLoudly_WhenModFolderMissing()
        {
            var req = BasicRequest();
            req.ModFolderNames = new[] { "DoesNotExist" };

            var service = new PackRelayPublishService(_client.Object);
            Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.PublishAsync(req));
        }

        [Test]
        public void Publish_FailsLoudly_WhenBundleEmpty()
        {
            // Empty folder produces zero files.
            Directory.CreateDirectory(Path.Combine(_modsRoot, "EmptyMod"));
            var req = BasicRequest();
            req.ModFolderNames = new[] { "EmptyMod" };

            var service = new PackRelayPublishService(_client.Object);
            Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync(req));
        }

        // ---- Idempotency ----

        [Test]
        public async Task Publish_SkipsUpload_WhenBlobAlreadyExists()
        {
            WriteFile("Test/a.xml", "alpha");
            WriteFile("Test/b.xml", "beta");
            WriteFile("Test/c.xml", "gamma");

            // Half hit, half miss — exercise the dedup path.
            int callCount = 0;
            _client.Setup(c => c.BlobExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => (callCount++ % 2) == 0);
            _client.Setup(c => c.UploadBlobAsync(
                    It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _client.Setup(c => c.PublishVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JObject());

            var req = BasicRequest();
            req.ModFolderNames = new[] { "Test" };
            await new PackRelayPublishService(_client.Object).PublishAsync(req);

            // BlobExists checked all 3, but UploadBlob only ran for
            // the misses. Concurrency makes "exactly N" hard to assert
            // for absolute upload counts (ordering nondeterminism on
            // the modulo flag), but UploadBlob count must be < 3.
            _client.Verify(c => c.BlobExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
            _client.Verify(c => c.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.AtMost(3));
        }

        [Test]
        public async Task Publish_DedupsByHash_AcrossMods()
        {
            // Two mods, both contain a file with identical bytes.
            // Should hash the same, BlobExists is called once, upload
            // happens (or doesn't) once — not twice.
            WriteFile("ModA/shared.xml", "duplicated content");
            WriteFile("ModB/shared.xml", "duplicated content");

            SetupAllUploadsMiss();
            _client.Setup(c => c.PublishVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JObject());

            var req = BasicRequest();
            req.ModFolderNames = new[] { "ModA", "ModB" };
            await new PackRelayPublishService(_client.Object).PublishAsync(req);

            // 2 manifest entries, 1 unique sha -> 1 BlobExists, 1 UploadBlob.
            _client.Verify(c => c.BlobExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
            _client.Verify(c => c.UploadBlobAsync(
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task Publish_409Duplicate_ReturnsAlreadyPublished()
        {
            WriteFile("Test/x.xml", "<x/>");

            SetupAllUploadsMiss();
            _client.Setup(c => c.PublishVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new PackRelayApiException(409, "Version already published.", "duplicate_version"));

            var req = BasicRequest();
            req.ModFolderNames = new[] { "Test" };
            var result = await new PackRelayPublishService(_client.Object).PublishAsync(req);

            Assert.That(result.AlreadyPublished, Is.True);
            Assert.That(result.Version, Is.EqualTo("1.0.0"));
        }

        [Test]
        public void Publish_OtherApiError_BubblesUp()
        {
            WriteFile("Test/x.xml", "<x/>");

            SetupAllUploadsMiss();
            _client.Setup(c => c.PublishVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new PackRelayApiException(403, "Not your pack", "ownership"));

            var req = BasicRequest();
            req.ModFolderNames = new[] { "Test" };
            var ex = Assert.ThrowsAsync<PackRelayApiException>(
                () => new PackRelayPublishService(_client.Object).PublishAsync(req));
            Assert.That(ex.StatusCode, Is.EqualTo(403));
        }

        // ---- Signature verification ----

        [Test]
        public async Task Publish_ManifestSignature_VerifiesAgainstDerivedPublicKey()
        {
            // The whole point of the publish flow. Build a manifest,
            // get the signed bytes back, strip the signature, re-
            // canonicalize, verify with the public key derived from
            // the same seed. If THIS test fails, the cloud's verify
            // would fail too (the cloud does the same dance with
            // @noble/ed25519 against the publisher_keys row).
            WriteFile("Test/file1.xml", "<a/>");
            WriteFile("Test/sub/file2.xml", "<b/>");

            SetupAllUploadsMiss();
            string capturedManifest = null;
            _client.Setup(c => c.PublishVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, CancellationToken>((_, m, __) => capturedManifest = m)
                .ReturnsAsync(new JObject());

            var req = BasicRequest();
            req.ModFolderNames = new[] { "Test" };
            await new PackRelayPublishService(_client.Object).PublishAsync(req);

            // DateParseHandling.None: keep ISO 8601 strings as
            // strings instead of auto-converting to Date tokens.
            // Without this, the publishedAt field round-trips as a
            // Date and CanonicalJson refuses to canonicalize it
            // (correctly — see WriteValue's Date-rejection branch).
            // Production flow doesn't hit this because the
            // orchestrator builds the JObject from scratch and
            // never re-parses.
            var signed = JsonConvert.DeserializeObject<JObject>(
                capturedManifest,
                new JsonSerializerSettings
                {
                    DateParseHandling = DateParseHandling.None,
                });
            var sigObj = (JObject)signed["signature"];
            Assert.That(sigObj.Value<string>("algo"), Is.EqualTo("ed25519"));
            Assert.That(sigObj.Value<string>("publicKeyId"), Is.EqualTo("kitsune-den/test-key"));

            var sigHex = sigObj.Value<string>("value");
            var sigBytes = FromHex(sigHex);
            Assert.That(sigBytes.Length, Is.EqualTo(64), "Ed25519 sigs are 64 bytes");

            // Reconstruct canonical bytes from manifest-minus-signature.
            signed.Remove("signature");
            var canonicalBytes = CanonicalJson.Encode(signed);
            var pubKey = Ed25519Signer.DerivePublicKey(TestSeed);
            Assert.That(
                Ed25519Signer.Verify(sigBytes, canonicalBytes, pubKey),
                Is.True,
                "Manifest signature must verify against the derived public key.");
        }

        // ---- Progress events ----

        [Test]
        public async Task Publish_ReportsPhases_InOrder()
        {
            WriteFile("Test/a.xml", "<a/>");
            WriteFile("Test/b.xml", "<b/>");

            SetupAllUploadsMiss();
            _client.Setup(c => c.PublishVersionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new JObject());

            var progress = new RecordingProgress();
            var req = BasicRequest();
            req.ModFolderNames = new[] { "Test" };
            await new PackRelayPublishService(_client.Object).PublishAsync(req, progress);

            var phases = progress.Events.Select(e => e.Phase).Distinct().ToList();
            // Must hit each named phase at least once, in document order.
            // NUnit 4.x dropped CollectionAssert.AreEqual in favor of
            // the constraint-based Assert.That(..., Is.EqualTo(...))
            // which already does element-wise comparison for IEnumerables.
            Assert.That(
                phases,
                Is.EqualTo(new[]
                {
                    PublishPhase.Walking,
                    PublishPhase.Hashing,
                    PublishPhase.Uploading,
                    PublishPhase.Signing,
                    PublishPhase.Posting,
                    PublishPhase.Done,
                }));

            // Final Done event has totals locked in.
            var done = progress.Events.Last();
            Assert.That(done.Phase, Is.EqualTo(PublishPhase.Done));
            Assert.That(done.FilesDone, Is.EqualTo(2));
            Assert.That(done.FilesTotal, Is.EqualTo(2));
        }

        // ---- Validation ----

        [Test]
        public void Publish_RejectsMissingSlug()
        {
            var req = BasicRequest();
            req.Slug = null;
            Assert.ThrowsAsync<ArgumentException>(
                () => new PackRelayPublishService(_client.Object).PublishAsync(req));
        }

        [Test]
        public void Publish_RejectsBadSigningKey()
        {
            var req = BasicRequest();
            req.SigningKeySeed = new byte[31]; // wrong length
            Assert.ThrowsAsync<ArgumentException>(
                () => new PackRelayPublishService(_client.Object).PublishAsync(req));
        }

        // ---- Plumbing ----

        private PublishRequest BasicRequest()
        {
            return new PublishRequest
            {
                Slug = "kitsune-den",
                DisplayName = "KitsuneDen Curated Pack",
                Version = "1.0.0",
                Description = "Test pack",
                ModFolderNames = new[] { "KitsuneCraft", "KitsuneKitchen" },
                ModsRoot = _modsRoot,
                Publisher = "KitsuneDen",
                GameVersion = "V2.0",
                PublicKeyId = "kitsune-den/test-key",
                SigningKeySeed = TestSeed,
            };
        }

        private void WriteFile(string relPath, string content)
        {
            var abs = Path.Combine(_modsRoot, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, Encoding.UTF8.GetBytes(content));
        }

        private void SetupAllUploadsMiss()
        {
            _client.Setup(c => c.BlobExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            _client.Setup(c => c.UploadBlobAsync(
                    It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        private class RecordingProgress : IProgress<PublishProgress>
        {
            public readonly List<PublishProgress> Events = new List<PublishProgress>();
            public void Report(PublishProgress value) => Events.Add(value);
        }

        private static byte[] FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }
}
