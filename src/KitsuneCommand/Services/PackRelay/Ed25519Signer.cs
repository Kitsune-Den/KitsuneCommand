// Ed25519 sign + verify. Wraps Chaos.NaCl (public-domain port of
// libsodium's NaCl primitives, shipped as a 172KB DLL in lib/).
// Same DLL the PackRelayServerTools mod uses to verify signed
// manifests on the server side — keeping the lib in sync across
// repos means any future security fix lands once and propagates.
//
// Why not BouncyCastle / NSec / System.Security.Cryptography:
//   - .NET Framework 4.8 lacks System.Security.Cryptography.Ed25519
//     (added in .NET 8); rolling that out via a polyfill package
//     isn't trivial on net48.
//   - NSec.Cryptography requires .NET Standard 2.1, which net48 does
//     not implement fully.
//   - BouncyCastle works but the package is ~3MB vs Chaos.NaCl's
//     172KB, and BC's API for Ed25519 is more verbose. Chaos.NaCl
//     is the minimal viable dependency.
//
// PackRelay key format:
//   - Ed25519 private key = 32-byte SEED (per RFC 8032). Chaos.NaCl
//     calls this `privateKeySeed` to avoid the older "expanded
//     64-byte private key" naming confusion. We accept the 32-byte
//     seed verbatim — same shape as what packrelay.cloud's
//     /account/keys page hands the publisher.
//   - Ed25519 public key = 32 bytes. Derived from the seed at
//     signing time, or distributed independently for verify.
//   - Signature = 64 bytes.
//
// Encoding convention: byte arrays at the boundary, base64 strings
// at the boundary with the cloud (HTTP headers, JSON bodies, env
// vars). Callers convert via Convert.ToBase64String / FromBase64String;
// this class never sees base64.

using System;
using Chaos.NaCl;

namespace KitsuneCommand.Services.PackRelay
{
    public static class Ed25519Signer
    {
        /// <summary>32 — Ed25519 private-key seed length per RFC 8032.</summary>
        public const int PrivateKeySeedLength = 32;
        /// <summary>32 — Ed25519 public-key length per RFC 8032.</summary>
        public const int PublicKeyLength = 32;
        /// <summary>64 — Ed25519 signature length per RFC 8032.</summary>
        public const int SignatureLength = 64;

        /// <summary>
        /// Sign <paramref name="message"/> with the 32-byte Ed25519
        /// private-key seed. Returns a 64-byte detached signature.
        ///
        /// The signature is over the message bytes verbatim (no
        /// pre-hashing); for PackRelay manifests this is the UTF-8
        /// canonical-JSON output of <see cref="CanonicalJson.Encode"/>.
        /// </summary>
        public static byte[] Sign(byte[] message, byte[] privateKeySeed)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (privateKeySeed == null) throw new ArgumentNullException(nameof(privateKeySeed));
            if (privateKeySeed.Length != PrivateKeySeedLength)
            {
                throw new ArgumentException(
                    "Ed25519 private-key seed must be exactly " +
                    PrivateKeySeedLength + " bytes; got " +
                    privateKeySeed.Length + ".",
                    nameof(privateKeySeed));
            }
            // Chaos.NaCl wants an "expanded private key" (seed || pubkey)
            // for its Sign API. Derive that pair from the seed; we
            // don't need to surface it to callers.
            byte[] expandedPrivate;
            byte[] publicKey;
            Ed25519.KeyPairFromSeed(out publicKey, out expandedPrivate, privateKeySeed);
            return Ed25519.Sign(message, expandedPrivate);
        }

        /// <summary>
        /// Verify <paramref name="signature"/> over <paramref name="message"/>
        /// using <paramref name="publicKey"/>. Returns false on any
        /// failure — wrong key, tampered message, malformed signature.
        /// Never throws on bad inputs; the cloud occasionally hands
        /// us garbage during testing and a hard-throw would mask the
        /// genuine "this didn't verify" signal.
        /// </summary>
        public static bool Verify(byte[] signature, byte[] message, byte[] publicKey)
        {
            if (signature == null || message == null || publicKey == null) return false;
            if (signature.Length != SignatureLength) return false;
            if (publicKey.Length != PublicKeyLength) return false;
            try
            {
                return Ed25519.Verify(signature, message, publicKey);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Derive the 32-byte public key from a 32-byte private-key
        /// seed. The cloud already hands the publisher both halves at
        /// keypair-creation time, but it's useful to re-derive
        /// client-side so we can show the user the public-key
        /// fingerprint in the settings UI without trusting whatever
        /// they pasted alongside the private key.
        /// </summary>
        public static byte[] DerivePublicKey(byte[] privateKeySeed)
        {
            if (privateKeySeed == null) throw new ArgumentNullException(nameof(privateKeySeed));
            if (privateKeySeed.Length != PrivateKeySeedLength)
            {
                throw new ArgumentException(
                    "Ed25519 private-key seed must be exactly " +
                    PrivateKeySeedLength + " bytes; got " +
                    privateKeySeed.Length + ".",
                    nameof(privateKeySeed));
            }
            byte[] expandedPrivate;
            byte[] publicKey;
            Ed25519.KeyPairFromSeed(out publicKey, out expandedPrivate, privateKeySeed);
            return publicKey;
        }
    }
}
