// Known-vector tests for the SHA-256 wrapper. The hash values here
// reproduce with:
//   echo -n "hello" | sha256sum  -> 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
//   printf "" | sha256sum         -> e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
// and on the cloud:
//   crypto.createHash("sha256").update("hello").digest("hex")
//
// Format: 64-char lowercase hex with no separators. The cloud's
// /api/v1/files/exists endpoint keys off this exact string, so any
// drift here (uppercase, leading "0x", colons) would silently break
// blob-existence checks at the publish step.

using System.IO;
using System.Text;
using KitsuneCommand.Services.PackRelay;
using NUnit.Framework;

namespace KitsuneCommand.Tests.Services.PackRelay
{
    [TestFixture]
    public class Sha256HasherTests
    {
        // Reference hashes from the sha256sum CLI / Node's crypto.
        private const string Hash_Empty =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        private const string Hash_Hello =
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";
        private const string Hash_TheQuickBrownFox =
            // "The quick brown fox jumps over the lazy dog"
            "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592";

        // ---- HashBytes ----

        [Test]
        public void HashBytes_Empty_MatchesReference()
        {
            Assert.That(Sha256Hasher.HashBytes(new byte[0]), Is.EqualTo(Hash_Empty));
        }

        [Test]
        public void HashBytes_Hello_MatchesReference()
        {
            var input = Encoding.UTF8.GetBytes("hello");
            Assert.That(Sha256Hasher.HashBytes(input), Is.EqualTo(Hash_Hello));
        }

        [Test]
        public void HashBytes_QuickBrownFox_MatchesReference()
        {
            var input = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
            Assert.That(Sha256Hasher.HashBytes(input), Is.EqualTo(Hash_TheQuickBrownFox));
        }

        [Test]
        public void HashBytes_OutputIsLowercaseHex_64Chars()
        {
            var hash = Sha256Hasher.HashBytes(Encoding.UTF8.GetBytes("anything"));
            Assert.That(hash.Length, Is.EqualTo(64));
            Assert.That(hash, Is.EqualTo(hash.ToLowerInvariant()));
            foreach (var c in hash)
            {
                Assert.That(
                    (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'),
                    Is.True,
                    "hash should be lowercase hex; got '" + c + "'");
            }
        }

        // ---- HashStream ----

        [Test]
        public void HashStream_MatchesHashBytes()
        {
            // The same bytes, two APIs, must produce the same hex.
            var data = Encoding.UTF8.GetBytes("hello");
            using (var ms = new MemoryStream(data))
            {
                Assert.That(Sha256Hasher.HashStream(ms), Is.EqualTo(Hash_Hello));
            }
        }

        [Test]
        public void HashStream_LargeInput_ChunkBoundaryRobust()
        {
            // 200 KB of zeros — straddles the 64 KB read buffer multiple
            // times. Confirms the TransformBlock loop handles >1 chunk
            // correctly (i.e. we don't accidentally hash only the
            // last buffer, or double-hash the first chunk).
            //
            // The "expected" is computed via HashBytes — which the
            // earlier tests already prove correct against well-known
            // vectors. The point of this test isn't to assert another
            // hex constant; it's to assert HashStream and HashBytes
            // agree across the chunk boundary.
            var big = new byte[200 * 1024];
            var expected = Sha256Hasher.HashBytes(big);
            using (var ms = new MemoryStream(big))
            {
                Assert.That(Sha256Hasher.HashStream(ms), Is.EqualTo(expected));
            }
        }

        // ---- HashFile ----

        [Test]
        public void HashFile_MatchesHashBytes()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "kc-sha256-test-" + System.Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                File.WriteAllBytes(tmp, Encoding.UTF8.GetBytes("hello"));
                Assert.That(Sha256Hasher.HashFile(tmp), Is.EqualTo(Hash_Hello));
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }

        [Test]
        public void HashFile_EmptyFile_MatchesReference()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "kc-sha256-test-" + System.Guid.NewGuid().ToString("N") + ".empty");
            try
            {
                File.WriteAllBytes(tmp, new byte[0]);
                Assert.That(Sha256Hasher.HashFile(tmp), Is.EqualTo(Hash_Empty));
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
    }
}
