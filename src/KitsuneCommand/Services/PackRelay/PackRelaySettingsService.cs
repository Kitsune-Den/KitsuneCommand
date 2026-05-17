// Public API for the panel + Stage 5's controller. Mediates the
// encryption + repo layers so callers never deal with ciphertext
// blobs or the master-key lifecycle directly.
//
// Design notes:
//
// Master-key bootstrap. On first access, we generate a fresh 32-byte
// random master + persist it in the existing `settings` KV table
// under MasterKeySettingKey. Same master across restarts. One per
// panel installation (lives in the same DB as the credentials it
// protects — yes, that means anyone with the DB can decrypt, which
// is the standard trade-off for "single-binary deployable mod that
// runs on a 7DTD server box"; we're not building a hardware HSM here).
//
// "Set creds" methods accept plaintext, encrypt + persist. "Get
// creds" returns plaintext for use by the publish flow. The
// "redacted" GetStatus() never returns plaintext — it's what the
// settings UI uses to render "Connected as <publisher>; fingerprint
// <first 8 chars>..." without touching the secrets.
//
// API token validation: we deliberately DON'T verify the token
// shape (length, prefix) here. The cloud accepts arbitrary opaque
// tokens; format may evolve. Save what the user pastes, let the
// first publish surface "401 invalid token" if it's wrong.
//
// Signing key validation: we DO verify the 32-byte length, since
// ed25519 is rigid about that and a wrong length would surface as
// a cryptic Chaos.NaCl exception three layers deep.

using System;
using System.Security.Cryptography;
using KitsuneCommand.Data.Entities;
using KitsuneCommand.Data.Repositories;

namespace KitsuneCommand.Services.PackRelay
{
    /// <summary>
    /// Redacted view of the settings, safe to send over HTTP to
    /// the panel UI. Plaintext token + key NEVER appear in this
    /// shape; only their presence + derived public fields.
    /// </summary>
    public class PackRelaySettingsStatus
    {
        public bool HasApiToken { get; set; }
        public bool HasSigningKey { get; set; }
        /// <summary>Base64 public key, plain. Used by the UI to show a fingerprint.</summary>
        public string SigningKeyPublic { get; set; }
        public string PublicKeyId { get; set; }
        public string PublisherSlug { get; set; }
        public string UpdatedAt { get; set; }
    }

    /// <summary>
    /// Plaintext credentials. Returned ONLY to in-process consumers
    /// (the orchestrator, the publish controller's local-call path).
    /// Never serialized back over HTTP — see PackRelaySettingsStatus
    /// for the wire-safe shape.
    /// </summary>
    public class PackRelayCredentials
    {
        public string ApiToken { get; set; }
        public byte[] SigningKeySeed { get; set; }
        public string PublicKeyId { get; set; }
        public string PublisherSlug { get; set; }
    }

    public class PackRelaySettingsService
    {
        /// <summary>
        /// The `settings` KV key under which we persist the
        /// base64-encoded master secret. Same KV the rest of KC
        /// uses (no separate file, no DPAPI dependency).
        /// </summary>
        public const string MasterKeySettingKey = "packrelay.master_key";

        private readonly IPackRelaySettingsRepository _repo;
        private readonly ISettingsRepository _kv;
        private byte[] _cachedMaster;
        private readonly object _masterLock = new object();

        public PackRelaySettingsService(
            IPackRelaySettingsRepository repo,
            ISettingsRepository kv)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _kv = kv ?? throw new ArgumentNullException(nameof(kv));
        }

        // ---- Status (wire-safe) ----

        /// <summary>
        /// What the settings page renders. Never returns plaintext
        /// credentials. Returns a "no row" shape when nothing has
        /// been saved yet so the UI can show an empty-state form
        /// without a separate "is configured?" round-trip.
        /// </summary>
        public PackRelaySettingsStatus GetStatus()
        {
            var row = _repo.GetCurrent();
            if (row == null)
            {
                return new PackRelaySettingsStatus
                {
                    HasApiToken = false,
                    HasSigningKey = false,
                };
            }
            return new PackRelaySettingsStatus
            {
                HasApiToken = row.ApiTokenEncrypted != null && row.ApiTokenEncrypted.Length > 0,
                HasSigningKey = row.SigningKeyEncrypted != null && row.SigningKeyEncrypted.Length > 0,
                SigningKeyPublic = row.SigningKeyPublic,
                PublicKeyId = row.PublicKeyId,
                PublisherSlug = row.PublisherSlug,
                UpdatedAt = row.UpdatedAt,
            };
        }

        // ---- Set credentials ----

        /// <summary>
        /// Persist a fresh API token. Encrypts before writing; the
        /// plaintext never lands on disk.
        /// </summary>
        public void SetApiToken(string apiToken)
        {
            if (string.IsNullOrWhiteSpace(apiToken))
                throw new ArgumentException("apiToken required", nameof(apiToken));
            var master = GetOrCreateMaster();
            var encrypted = PackRelayEncryptor.EncryptString(apiToken, master);

            var row = _repo.GetCurrent() ?? new PackRelaySettings();
            row.ApiTokenEncrypted = encrypted;
            _repo.Upsert(row);
        }

        /// <summary>
        /// Persist a signing key. The 32-byte Ed25519 seed gets
        /// encrypted; the derived public key + caller-supplied
        /// publicKeyId are stored plain (public-by-spec).
        /// </summary>
        public void SetSigningKey(byte[] privateKeySeed, string publicKeyId)
        {
            if (privateKeySeed == null)
                throw new ArgumentNullException(nameof(privateKeySeed));
            if (privateKeySeed.Length != Ed25519Signer.PrivateKeySeedLength)
                throw new ArgumentException(
                    "Ed25519 private-key seed must be " +
                    Ed25519Signer.PrivateKeySeedLength + " bytes; got " +
                    privateKeySeed.Length + ".",
                    nameof(privateKeySeed));
            if (string.IsNullOrWhiteSpace(publicKeyId))
                throw new ArgumentException("publicKeyId required", nameof(publicKeyId));

            var master = GetOrCreateMaster();
            var encrypted = PackRelayEncryptor.Encrypt(privateKeySeed, master);

            // Derive the public key from the seed and persist
            // base64. The UI uses this to show a fingerprint; the
            // orchestrator uses it as a cross-check that the seed
            // hasn't been corrupted by a master-key mishap.
            var publicKey = Ed25519Signer.DerivePublicKey(privateKeySeed);
            var publicKeyB64 = Convert.ToBase64String(publicKey);

            var row = _repo.GetCurrent() ?? new PackRelaySettings();
            row.SigningKeyEncrypted = encrypted;
            row.SigningKeyPublic = publicKeyB64;
            row.PublicKeyId = publicKeyId;
            _repo.Upsert(row);
        }

        /// <summary>
        /// Set the cloud-assigned pack slug after a successful first
        /// publish. Stored plain (it's in URLs anyway). Idempotent
        /// re-sets — passing the same slug doesn't error.
        /// </summary>
        public void SetPublisherSlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("slug required", nameof(slug));
            var row = _repo.GetCurrent() ?? new PackRelaySettings();
            row.PublisherSlug = slug;
            _repo.Upsert(row);
        }

        // ---- Get plaintext credentials (in-process consumers only) ----

        /// <summary>
        /// Decrypt + return the credentials. Used by the publish
        /// controller to build the orchestrator request. Throws if
        /// either credential is missing — caller checks GetStatus()
        /// first if it needs to show a "credentials not configured"
        /// error instead.
        /// </summary>
        public PackRelayCredentials GetCredentials()
        {
            var row = _repo.GetCurrent();
            if (row == null ||
                row.ApiTokenEncrypted == null ||
                row.SigningKeyEncrypted == null)
            {
                throw new InvalidOperationException(
                    "PackRelay credentials are not fully configured. " +
                    "Set both the API token and signing key on the " +
                    "PackRelay tab before publishing.");
            }
            var master = GetOrCreateMaster();
            return new PackRelayCredentials
            {
                ApiToken = PackRelayEncryptor.DecryptString(row.ApiTokenEncrypted, master),
                SigningKeySeed = PackRelayEncryptor.Decrypt(row.SigningKeyEncrypted, master),
                PublicKeyId = row.PublicKeyId,
                PublisherSlug = row.PublisherSlug,
            };
        }

        // ---- Wipe ----

        /// <summary>
        /// Drop all credentials AND the master key. After this,
        /// GetStatus returns the empty-row shape. The "rotate key"
        /// path on the settings UI calls this then prompts re-paste.
        /// </summary>
        public void Reset()
        {
            _repo.Delete();
            _kv.Set(MasterKeySettingKey, null);
            lock (_masterLock)
            {
                if (_cachedMaster != null)
                {
                    Array.Clear(_cachedMaster, 0, _cachedMaster.Length);
                    _cachedMaster = null;
                }
            }
        }

        // ---- Master-key lifecycle ----

        /// <summary>
        /// Get the persisted master, generating + storing one on
        /// first access. Cached in-process to avoid the round-trip
        /// on every operation; thread-safe via _masterLock.
        /// </summary>
        private byte[] GetOrCreateMaster()
        {
            lock (_masterLock)
            {
                if (_cachedMaster != null) return _cachedMaster;
                var b64 = _kv.Get(MasterKeySettingKey);
                if (string.IsNullOrEmpty(b64))
                {
                    var fresh = PackRelayEncryptor.GenerateMasterSecret();
                    _kv.Set(MasterKeySettingKey, Convert.ToBase64String(fresh));
                    _cachedMaster = fresh;
                    return _cachedMaster;
                }
                try
                {
                    _cachedMaster = Convert.FromBase64String(b64);
                }
                catch (FormatException ex)
                {
                    throw new CryptographicException(
                        "Stored PackRelay master key is not valid base64. " +
                        "Reset PackRelay settings to regenerate.", ex);
                }
                if (_cachedMaster.Length != 32)
                {
                    throw new CryptographicException(
                        "Stored PackRelay master key is " + _cachedMaster.Length +
                        " bytes; expected 32. Reset PackRelay settings to regenerate.");
                }
                return _cachedMaster;
            }
        }
    }
}
