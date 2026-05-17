// Authenticated encryption for the PackRelay credentials at rest.
//
// Construction: AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC). Sound
// + dependency-free. .NET 4.8 doesn't ship AES-GCM (added in
// .NET 5); BouncyCastle's GCM impl is 3 MB; the EtM pattern using
// only System.Security.Cryptography is small + auditable.
//
// Wire format (BLOB on disk):
//   [1 byte  version = 0x01]
//   [16 bytes IV (random per encrypt)]
//   [32 bytes HMAC-SHA256(version || IV || ciphertext)]
//   [N bytes  AES-256-CBC(plaintext, PKCS7 pad, derived AES key)]
//
// Why the version byte:
//   If we ever need to rotate to AES-GCM or AES-256-CBC-HMAC-SHA512,
//   bumping the version lets us read both old + new formats during
//   migration. Without it, a future rotation would either silently
//   re-encrypt under the old construction or require a flag-day.
//
// Key derivation:
//   - Master secret = random 32 bytes generated on first call,
//     persisted via the panel's settings KV. One master per panel
//     install; identical across restarts; never leaves the server.
//   - AES key  = SHA256(master || "packrelay-enc-v1-aes")
//   - HMAC key = SHA256(master || "packrelay-enc-v1-mac")
//   The label string is bound to BOTH the construction version and
//   the key purpose (separate AES + HMAC keys) so a master-key
//   compromise that leaks one derived key doesn't grant the other.
//
// HMAC ordering: encrypt-then-MAC over (version || IV || ciphertext).
// Including the version byte in the MAC input means a downgrade
// attack (rewriting the version byte to 0x00 to confuse a future
// reader) breaks integrity. Including the IV means a fixed-IV
// attack (re-using a known IV from another row's ciphertext) also
// breaks integrity. Verify is constant-time.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KitsuneCommand.Services.PackRelay
{
    public static class PackRelayEncryptor
    {
        private const byte CurrentVersion = 0x01;
        private const int VersionLength = 1;
        private const int IvLength = 16;
        private const int MacLength = 32;
        private const int HeaderLength = VersionLength + IvLength + MacLength;

        private const int AesKeyLength = 32;
        private const int MasterSecretLength = 32;

        private const string LabelAes = "packrelay-enc-v1-aes";
        private const string LabelMac = "packrelay-enc-v1-mac";

        /// <summary>
        /// Generate a fresh 32-byte master secret. Caller persists it
        /// (e.g. in the settings KV) — same secret across restarts is
        /// what makes the encrypted credentials decryptable later.
        /// </summary>
        public static byte[] GenerateMasterSecret()
        {
            var bytes = new byte[MasterSecretLength];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(bytes);
            }
            return bytes;
        }

        /// <summary>
        /// Encrypt <paramref name="plaintext"/> under the master secret.
        /// Returns the version-tagged BLOB to persist.
        /// </summary>
        public static byte[] Encrypt(byte[] plaintext, byte[] masterSecret)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            ValidateMaster(masterSecret);

            var aesKey = DeriveKey(masterSecret, LabelAes);
            var macKey = DeriveKey(masterSecret, LabelMac);

            byte[] iv;
            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = aesKey;
                aes.GenerateIV();
                iv = aes.IV;
                using (var enc = aes.CreateEncryptor())
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, enc, CryptoStreamMode.Write))
                    {
                        cs.Write(plaintext, 0, plaintext.Length);
                    }
                    ciphertext = ms.ToArray();
                }
            }

            // MAC = HMAC-SHA256(version || IV || ciphertext) using the
            // mac key. Version byte first so a downgrade-attack
            // attempt (overwriting it) doesn't slip past Verify.
            var mac = ComputeMac(macKey, CurrentVersion, iv, ciphertext);

            var blob = new byte[HeaderLength + ciphertext.Length];
            blob[0] = CurrentVersion;
            Buffer.BlockCopy(iv, 0, blob, VersionLength, IvLength);
            Buffer.BlockCopy(mac, 0, blob, VersionLength + IvLength, MacLength);
            Buffer.BlockCopy(ciphertext, 0, blob, HeaderLength, ciphertext.Length);

            // Best-effort zero-out of the derived keys + ciphertext
            // local. The CLR may have shuffled them around the heap
            // already, but clearing reduces the wall-clock window
            // where they sit in addressable memory.
            Array.Clear(aesKey, 0, aesKey.Length);
            Array.Clear(macKey, 0, macKey.Length);
            return blob;
        }

        /// <summary>
        /// Decrypt <paramref name="blob"/>. Verifies HMAC before
        /// touching the ciphertext — a tampered (or wrong-master)
        /// payload throws <see cref="CryptographicException"/>
        /// without producing any plaintext.
        /// </summary>
        public static byte[] Decrypt(byte[] blob, byte[] masterSecret)
        {
            if (blob == null) throw new ArgumentNullException(nameof(blob));
            ValidateMaster(masterSecret);
            if (blob.Length < HeaderLength)
            {
                throw new CryptographicException(
                    "Ciphertext blob too short (got " + blob.Length +
                    " bytes; need at least " + HeaderLength + ").");
            }
            byte version = blob[0];
            if (version != CurrentVersion)
            {
                throw new CryptographicException(
                    "Unsupported PackRelay ciphertext version: 0x" +
                    version.ToString("x2"));
            }

            var iv = new byte[IvLength];
            Buffer.BlockCopy(blob, VersionLength, iv, 0, IvLength);
            var expectedMac = new byte[MacLength];
            Buffer.BlockCopy(blob, VersionLength + IvLength, expectedMac, 0, MacLength);
            var ciphertext = new byte[blob.Length - HeaderLength];
            Buffer.BlockCopy(blob, HeaderLength, ciphertext, 0, ciphertext.Length);

            var aesKey = DeriveKey(masterSecret, LabelAes);
            var macKey = DeriveKey(masterSecret, LabelMac);

            var actualMac = ComputeMac(macKey, version, iv, ciphertext);
            if (!ConstantTimeEquals(actualMac, expectedMac))
            {
                Array.Clear(aesKey, 0, aesKey.Length);
                Array.Clear(macKey, 0, macKey.Length);
                throw new CryptographicException(
                    "PackRelay ciphertext failed HMAC verification — " +
                    "the master key changed, the blob was tampered with, " +
                    "or the wrong master was passed.");
            }

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = aesKey;
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor())
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, dec, CryptoStreamMode.Write))
                    {
                        cs.Write(ciphertext, 0, ciphertext.Length);
                    }
                    var plaintext = ms.ToArray();
                    Array.Clear(aesKey, 0, aesKey.Length);
                    Array.Clear(macKey, 0, macKey.Length);
                    return plaintext;
                }
            }
        }

        /// <summary>
        /// Convenience: encrypt a UTF-8 string. The dominant case —
        /// API tokens, base64-encoded keys, slugs.
        /// </summary>
        public static byte[] EncryptString(string plaintext, byte[] masterSecret)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            return Encrypt(Encoding.UTF8.GetBytes(plaintext), masterSecret);
        }

        /// <summary>
        /// Convenience: decrypt a UTF-8 string.
        /// </summary>
        public static string DecryptString(byte[] blob, byte[] masterSecret)
        {
            var plaintext = Decrypt(blob, masterSecret);
            try
            {
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                Array.Clear(plaintext, 0, plaintext.Length);
            }
        }

        // ---- Internals ----

        private static byte[] DeriveKey(byte[] master, string label)
        {
            // SHA256(master || ASCII(label)) -> 32-byte key.
            // Not HKDF, but adequate here: distinct labels per purpose,
            // master is already random + uniformly distributed (so no
            // PBKDF-style stretching needed), no salt to worry about
            // since the master IS the key material.
            var labelBytes = Encoding.ASCII.GetBytes(label);
            var input = new byte[master.Length + labelBytes.Length];
            Buffer.BlockCopy(master, 0, input, 0, master.Length);
            Buffer.BlockCopy(labelBytes, 0, input, master.Length, labelBytes.Length);
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(input);
            }
        }

        private static byte[] ComputeMac(byte[] macKey, byte version, byte[] iv, byte[] ciphertext)
        {
            using (var hmac = new HMACSHA256(macKey))
            {
                hmac.TransformBlock(new[] { version }, 0, 1, null, 0);
                hmac.TransformBlock(iv, 0, iv.Length, null, 0);
                hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                return hmac.Hash;
            }
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }

        private static void ValidateMaster(byte[] masterSecret)
        {
            if (masterSecret == null) throw new ArgumentNullException(nameof(masterSecret));
            if (masterSecret.Length != MasterSecretLength)
            {
                throw new ArgumentException(
                    "Master secret must be exactly " + MasterSecretLength +
                    " bytes; got " + masterSecret.Length + ".",
                    nameof(masterSecret));
            }
        }
    }
}
