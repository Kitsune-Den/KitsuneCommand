-- Encrypted publisher credentials for the PackRelay publish flow.
--
-- Singleton row (same shape as `modpack`): KC v1 supports one
-- packrelay.cloud publisher account per panel installation. If a
-- future feature needs multi-publisher support, this table grows
-- a unique-publisher index and the singleton query in the repo
-- relaxes; the schema doesn't need to change.
--
-- Why the encrypted columns are BLOB (not TEXT):
--   - AES ciphertext + 16-byte IV + 32-byte HMAC tag is raw binary,
--     not base64. Storing as BLOB avoids the size-bloat + double-
--     encoding burden + the temptation to log the value (TEXT
--     columns show up in ad-hoc `SELECT *` more often than BLOBs).
--   - The plain `signing_key_public` is base64 by spec (matches
--     what packrelay.cloud's /account/keys page hands the user);
--     keeping it TEXT lets the settings UI show "fingerprint:
--     <first 8 base64 chars>..." without a decode round-trip.
--
-- publisher_slug is what the cloud assigned on first publish. We
-- pin it after first success so a future re-publish always goes
-- to the same catalog entry — but it's nullable until then so the
-- settings panel can persist creds before the first publish run.
CREATE TABLE IF NOT EXISTS pack_relay_settings (
    id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    -- Encrypted personal API token minted at packrelay.cloud /account/tokens.
    -- Decrypts to a plaintext string the PackRelayClient sends as
    -- Authorization: Bearer <token>.
    api_token_encrypted   BLOB,
    -- Encrypted 32-byte Ed25519 private-key seed (the form
    -- packrelay.cloud /account/keys hands the publisher at
    -- keypair generation, after which the cloud only stores the
    -- public half).
    signing_key_encrypted BLOB,
    -- Base64-encoded 32-byte Ed25519 public key, plain. Kept so the
    -- settings UI can show a key fingerprint + the orchestrator can
    -- spot-check the seed it just decrypted re-derives to the same
    -- public key (catches a corrupted master_key without falsely
    -- claiming "your token is bad").
    signing_key_public    TEXT,
    -- Publisher's key identifier from the cloud, format
    -- <publisher>/<key-name>. Embedded in the signed manifest's
    -- signature.publicKeyId field so the cloud knows which
    -- publisher_keys row to verify against.
    public_key_id         TEXT,
    -- Cloud-assigned pack slug after first publish (immutable
    -- post-assignment so the same KC always pushes to the same
    -- catalog entry). NULL until first successful publish; the
    -- settings UI lets the user set it explicitly before that.
    publisher_slug        TEXT,
    updated_at            TEXT NOT NULL DEFAULT (datetime('now'))
);
