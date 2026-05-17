namespace KitsuneCommand.Data.Entities
{
    /// <summary>
    /// Encrypted credentials + immutable metadata for the panel's
    /// PackRelay publisher account. Singleton row (KC v1 = one
    /// publisher per panel installation).
    ///
    /// Encrypted columns hold raw ciphertext bytes (AES-CBC +
    /// HMAC-SHA256, version-tagged); the matching plaintext is
    /// only ever produced through <c>PackRelaySettingsService</c>,
    /// which mediates the crypto so callers never need to know
    /// about the key derivation.
    ///
    /// The "Public" + "PublicKeyId" + "PublisherSlug" fields are
    /// plain on purpose — they're either non-sensitive (public key
    /// is by definition public) or needed as plaintext at multiple
    /// layers (the slug is in URLs, the key ID is in manifest
    /// signatures).
    /// </summary>
    public class PackRelaySettings
    {
        public int Id { get; set; }

        /// <summary>AES-encrypted API token bytes. Null until the user pastes one.</summary>
        public byte[] ApiTokenEncrypted { get; set; }

        /// <summary>AES-encrypted 32-byte Ed25519 seed. Null until the user pastes one.</summary>
        public byte[] SigningKeyEncrypted { get; set; }

        /// <summary>Base64-encoded 32-byte Ed25519 public key. Null when no key is set.</summary>
        public string SigningKeyPublic { get; set; }

        /// <summary><c>&lt;publisher&gt;/&lt;key-name&gt;</c>, e.g. "kitsune-den/server-tools". Null until set.</summary>
        public string PublicKeyId { get; set; }

        /// <summary>Cloud-assigned pack slug; null until first successful publish.</summary>
        public string PublisherSlug { get; set; }

        public string UpdatedAt { get; set; }
    }
}
