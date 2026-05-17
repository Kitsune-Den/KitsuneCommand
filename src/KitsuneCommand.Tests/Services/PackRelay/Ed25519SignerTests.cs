// Ed25519 sign/verify tests. The big "this implementation actually
// computes Ed25519 correctly" test uses RFC 8032 test vector #1 —
// publicly auditable, reproduces with any conformant Ed25519 lib in
// any language. If this passes, we trust Chaos.NaCl + the wrapper
// (the wrapper is just plumbing).

using System;
using System.Text;
using KitsuneCommand.Services.PackRelay;
using NUnit.Framework;

namespace KitsuneCommand.Tests.Services.PackRelay
{
    [TestFixture]
    public class Ed25519SignerTests
    {
        // RFC 8032 § 7.1, Test 1.
        //   secret key seed = 9d61b19deffd5a60ba844af492ec2cc4 4449c5697b326919703bac031cae7f60
        //   public key      = d75a980182b10ab7d54bfed3c964073a 0ee172f3daa62325af021a68f707511a
        //   message         = (empty)
        //   signature       = e5564300c360ac729086e2cc806e828a 84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46b d25bf5f0595bbe24655141438e7a100b
        private static byte[] Rfc8032_Test1_Seed => FromHex(
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        private static byte[] Rfc8032_Test1_PublicKey => FromHex(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        private static byte[] Rfc8032_Test1_Message => Array.Empty<byte>();
        private static byte[] Rfc8032_Test1_Signature => FromHex(
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");

        // ---- Sign ----

        [Test]
        public void Sign_Rfc8032Test1_ProducesExpectedSignature()
        {
            // The "are we computing Ed25519 correctly" smoke test.
            // Bit-exact match with the RFC vector means the underlying
            // Chaos.NaCl + the wrapper agree with the standard.
            var sig = Ed25519Signer.Sign(Rfc8032_Test1_Message, Rfc8032_Test1_Seed);
            Assert.That(sig, Is.EqualTo(Rfc8032_Test1_Signature));
        }

        [Test]
        public void Sign_IsDeterministic()
        {
            // Ed25519 is deterministic by spec — same seed + same
            // message MUST produce the same signature byte-for-byte.
            // We rely on this in the publish flow: re-publishing the
            // same version twice produces an identical signed manifest.
            var msg = Encoding.UTF8.GetBytes("hello");
            var sig1 = Ed25519Signer.Sign(msg, Rfc8032_Test1_Seed);
            var sig2 = Ed25519Signer.Sign(msg, Rfc8032_Test1_Seed);
            Assert.That(sig1, Is.EqualTo(sig2));
        }

        [Test]
        public void Sign_RejectsWrongSeedLength()
        {
            var msg = Encoding.UTF8.GetBytes("x");
            Assert.Throws<ArgumentException>(
                () => Ed25519Signer.Sign(msg, new byte[31]));
            Assert.Throws<ArgumentException>(
                () => Ed25519Signer.Sign(msg, new byte[33]));
        }

        [Test]
        public void Sign_RejectsNullArgs()
        {
            Assert.Throws<ArgumentNullException>(
                () => Ed25519Signer.Sign(null, Rfc8032_Test1_Seed));
            Assert.Throws<ArgumentNullException>(
                () => Ed25519Signer.Sign(new byte[] { 1, 2 }, null));
        }

        // ---- Verify ----

        [Test]
        public void Verify_Rfc8032Test1_AcceptsValidSignature()
        {
            var ok = Ed25519Signer.Verify(
                Rfc8032_Test1_Signature,
                Rfc8032_Test1_Message,
                Rfc8032_Test1_PublicKey);
            Assert.That(ok, Is.True);
        }

        [Test]
        public void Verify_RejectsTamperedMessage()
        {
            var tampered = Encoding.UTF8.GetBytes("not the empty message");
            var ok = Ed25519Signer.Verify(
                Rfc8032_Test1_Signature,
                tampered,
                Rfc8032_Test1_PublicKey);
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Verify_RejectsWrongPublicKey()
        {
            // Just flip a bit in the first byte of the pubkey.
            var wrongPub = (byte[])Rfc8032_Test1_PublicKey.Clone();
            wrongPub[0] ^= 0x01;
            var ok = Ed25519Signer.Verify(
                Rfc8032_Test1_Signature,
                Rfc8032_Test1_Message,
                wrongPub);
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Verify_RejectsTruncatedSignature()
        {
            var truncated = new byte[63];
            Array.Copy(Rfc8032_Test1_Signature, truncated, 63);
            var ok = Ed25519Signer.Verify(
                truncated,
                Rfc8032_Test1_Message,
                Rfc8032_Test1_PublicKey);
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Verify_RejectsWrongPublicKeyLength()
        {
            var ok = Ed25519Signer.Verify(
                Rfc8032_Test1_Signature,
                Rfc8032_Test1_Message,
                new byte[31]);
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Verify_RejectsNullInputs()
        {
            // Soft-fail rather than throwing on bad inputs — the
            // cloud occasionally hands us garbage during testing
            // and a throw here would mask the "did not verify" signal.
            Assert.That(
                Ed25519Signer.Verify(null, Rfc8032_Test1_Message, Rfc8032_Test1_PublicKey),
                Is.False);
            Assert.That(
                Ed25519Signer.Verify(Rfc8032_Test1_Signature, null, Rfc8032_Test1_PublicKey),
                Is.False);
            Assert.That(
                Ed25519Signer.Verify(Rfc8032_Test1_Signature, Rfc8032_Test1_Message, null),
                Is.False);
        }

        // ---- Round trip ----

        [Test]
        public void SignThenVerify_RoundTrips()
        {
            var msg = Encoding.UTF8.GetBytes("PackRelay test manifest 1.2.3");
            var sig = Ed25519Signer.Sign(msg, Rfc8032_Test1_Seed);
            var ok = Ed25519Signer.Verify(sig, msg, Rfc8032_Test1_PublicKey);
            Assert.That(ok, Is.True);
        }

        // ---- DerivePublicKey ----

        [Test]
        public void DerivePublicKey_Rfc8032Test1_MatchesExpected()
        {
            var pub = Ed25519Signer.DerivePublicKey(Rfc8032_Test1_Seed);
            Assert.That(pub, Is.EqualTo(Rfc8032_Test1_PublicKey));
        }

        [Test]
        public void DerivePublicKey_RejectsWrongSeedLength()
        {
            Assert.Throws<ArgumentException>(
                () => Ed25519Signer.DerivePublicKey(new byte[31]));
        }

        // ---- Helpers ----

        private static byte[] FromHex(string hex)
        {
            if (hex.Length % 2 != 0)
                throw new ArgumentException("odd-length hex");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }
}
