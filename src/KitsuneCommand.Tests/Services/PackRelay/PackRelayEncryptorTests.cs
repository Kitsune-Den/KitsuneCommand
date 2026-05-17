// Pure-crypto tests for the AES-CBC + HMAC encrypt-then-MAC
// construction. These run in isolation from the DB layer — no
// settings table, no master-key persistence. PackRelaySettingsService
// tests cover that side.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KitsuneCommand.Services.PackRelay;
using NUnit.Framework;

namespace KitsuneCommand.Tests.Services.PackRelay
{
    [TestFixture]
    public class PackRelayEncryptorTests
    {
        [Test]
        public void GenerateMasterSecret_Returns32Bytes()
        {
            var master = PackRelayEncryptor.GenerateMasterSecret();
            Assert.That(master.Length, Is.EqualTo(32));
        }

        [Test]
        public void GenerateMasterSecret_Distinct_AcrossCalls()
        {
            // Cryptographic randomness — two consecutive calls must
            // not produce the same bytes. If this ever fires, RNG
            // is broken.
            var a = PackRelayEncryptor.GenerateMasterSecret();
            var b = PackRelayEncryptor.GenerateMasterSecret();
            Assert.That(a, Is.Not.EqualTo(b));
        }

        // ---- Round trip ----

        [Test]
        public void EncryptDecrypt_RoundTrips_Bytes()
        {
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var plaintext = Encoding.UTF8.GetBytes("the quick brown fox");
            var blob = PackRelayEncryptor.Encrypt(plaintext, master);
            var restored = PackRelayEncryptor.Decrypt(blob, master);
            Assert.That(restored, Is.EqualTo(plaintext));
        }

        [Test]
        public void EncryptDecrypt_RoundTrips_Empty()
        {
            // Empty plaintext is still a valid input (PKCS7 pads to
            // one full block). Token strings should never be empty
            // in practice, but the primitive needs to handle it.
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var blob = PackRelayEncryptor.Encrypt(new byte[0], master);
            var restored = PackRelayEncryptor.Decrypt(blob, master);
            Assert.That(restored.Length, Is.EqualTo(0));
        }

        [Test]
        public void EncryptDecrypt_RoundTrips_LargePlaintext()
        {
            // The signing-key seed is 32 bytes; the API token is
            // ~100. The largest plausible input is maybe a few KB
            // (a config-export blob). Sanity-check at 64 KB anyway.
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var plaintext = new byte[64 * 1024];
            for (int i = 0; i < plaintext.Length; i++) plaintext[i] = (byte)(i & 0xFF);
            var blob = PackRelayEncryptor.Encrypt(plaintext, master);
            var restored = PackRelayEncryptor.Decrypt(blob, master);
            Assert.That(restored, Is.EqualTo(plaintext));
        }

        [Test]
        public void EncryptStringDecryptString_RoundTrips_Unicode()
        {
            // Realistic token shape includes mixed-case + digits +
            // possibly punctuation. Also throw a snowman in for
            // the UTF-8 sanity.
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var plaintext = "kc-PaT_aB1cD2eF3gH4iJ5kL6mN7oP8qR9sT0_☃";
            var blob = PackRelayEncryptor.EncryptString(plaintext, master);
            var restored = PackRelayEncryptor.DecryptString(blob, master);
            Assert.That(restored, Is.EqualTo(plaintext));
        }

        // ---- Wire format ----

        [Test]
        public void Encrypt_OutputIsNotPlaintext()
        {
            // The threat model says someone reading the SQLite file
            // shouldn't see the secret. Confirm the ciphertext bytes
            // don't contain the plaintext as a substring.
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var secret = Encoding.UTF8.GetBytes("super-secret-api-token");
            var blob = PackRelayEncryptor.Encrypt(secret, master);
            Assert.That(IndexOf(blob, secret), Is.EqualTo(-1),
                "Ciphertext blob must not contain the plaintext bytes.");
        }

        [Test]
        public void Encrypt_NonDeterministic_AcrossCalls()
        {
            // Same plaintext, same master, two encryptions — IVs
            // must differ, so ciphertexts must differ. A
            // deterministic output would leak "this row's token
            // didn't change" to an observer.
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var plaintext = Encoding.UTF8.GetBytes("identical input");
            var a = PackRelayEncryptor.Encrypt(plaintext, master);
            var b = PackRelayEncryptor.Encrypt(plaintext, master);
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Encrypt_OutputHasVersionByte_0x01()
        {
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var blob = PackRelayEncryptor.Encrypt(Encoding.UTF8.GetBytes("x"), master);
            Assert.That(blob[0], Is.EqualTo((byte)0x01),
                "First byte should be the version tag; bumping requires a migration path.");
        }

        // ---- Tamper detection ----

        [Test]
        public void Decrypt_RejectsTamperedCiphertext()
        {
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var blob = PackRelayEncryptor.Encrypt(Encoding.UTF8.GetBytes("don't tamper"), master);
            // Flip a bit in the ciphertext region (after the 49-byte
            // header).
            blob[blob.Length - 1] ^= 0x01;
            Assert.Throws<CryptographicException>(
                () => PackRelayEncryptor.Decrypt(blob, master));
        }

        [Test]
        public void Decrypt_RejectsTamperedMac()
        {
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var blob = PackRelayEncryptor.Encrypt(Encoding.UTF8.GetBytes("hi"), master);
            // The MAC sits at bytes [17..49). Flip a bit there.
            blob[20] ^= 0x80;
            Assert.Throws<CryptographicException>(
                () => PackRelayEncryptor.Decrypt(blob, master));
        }

        [Test]
        public void Decrypt_RejectsTamperedVersionByte()
        {
            // Including the version byte in the HMAC input means a
            // future-attacker downgrading the wire format gets
            // caught by the integrity check.
            var master = PackRelayEncryptor.GenerateMasterSecret();
            var blob = PackRelayEncryptor.Encrypt(Encoding.UTF8.GetBytes("hi"), master);
            blob[0] = 0x99; // unsupported version
            Assert.Throws<CryptographicException>(
                () => PackRelayEncryptor.Decrypt(blob, master));
        }

        [Test]
        public void Decrypt_RejectsWrongMaster()
        {
            var master1 = PackRelayEncryptor.GenerateMasterSecret();
            var master2 = PackRelayEncryptor.GenerateMasterSecret();
            var blob = PackRelayEncryptor.Encrypt(Encoding.UTF8.GetBytes("secret"), master1);
            Assert.Throws<CryptographicException>(
                () => PackRelayEncryptor.Decrypt(blob, master2));
        }

        [Test]
        public void Decrypt_RejectsTruncatedBlob()
        {
            var master = PackRelayEncryptor.GenerateMasterSecret();
            // Smaller than the 49-byte header.
            Assert.Throws<CryptographicException>(
                () => PackRelayEncryptor.Decrypt(new byte[10], master));
        }

        // ---- Argument validation ----

        [Test]
        public void Encrypt_RejectsNull()
        {
            var master = PackRelayEncryptor.GenerateMasterSecret();
            Assert.Throws<ArgumentNullException>(
                () => PackRelayEncryptor.Encrypt(null, master));
            Assert.Throws<ArgumentNullException>(
                () => PackRelayEncryptor.Encrypt(new byte[] { 1 }, null));
        }

        [Test]
        public void Encrypt_RejectsWrongMasterLength()
        {
            Assert.Throws<ArgumentException>(
                () => PackRelayEncryptor.Encrypt(new byte[] { 1 }, new byte[31]));
            Assert.Throws<ArgumentException>(
                () => PackRelayEncryptor.Encrypt(new byte[] { 1 }, new byte[33]));
        }

        // ---- Helpers ----

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
