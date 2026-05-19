# Release Signing

Every KC release zip is paired with two sidecar files:

- `KitsuneCommand-vX.Y.Z.zip.sha256` — BSD-style sha256 sum line
- `KitsuneCommand-vX.Y.Z.zip.minisig` — [minisign](https://jedisct1.github.io/minisign/) Ed25519 signature

Together they let a downloader verify the zip is **byte-identical** to what GitHub Actions built (sha256) **and signed by the KC maintainer** (minisign).

This document covers both sides:

1. [**For end users**](#verifying-a-downloaded-release) — how to verify a release before extracting
2. [**For the maintainer**](#one-time-keypair-setup) — one-time setup + ongoing release process

---

## Verifying a downloaded release

You'll need [minisign](https://jedisct1.github.io/minisign/) installed (single small binary; package-manager available on every platform).

```bash
# Download all three from the GitHub release page:
#   KitsuneCommand-vX.Y.Z.zip
#   KitsuneCommand-vX.Y.Z.zip.sha256
#   KitsuneCommand-vX.Y.Z.zip.minisig

# 1. Verify the SHA-256 — proves the bytes match what was published
sha256sum -c KitsuneCommand-vX.Y.Z.zip.sha256

# 2. Verify the signature — proves the zip was signed by the KC maintainer
minisign -Vm KitsuneCommand-vX.Y.Z.zip -P RWQ+HmFzPKzD6llIm6vKIVja0QU1t5DPYBxHfPb6tcRqQecwAlWjyyln
```

The public key is published in [`docs/RELEASES.md`](RELEASES.md#verifying-a-release) and on the GitHub release notes for the first release that shipped signed.

If `minisign -V` prints `Signature and comment signature verified`, you're good. If it prints anything else — **don't extract the zip.** Open an issue at the repo.

> **Why both?** SHA-256 alone proves the zip you downloaded is the one GitHub stored — but anyone with write access to the release page (or a successful MITM in the rare case of an old TLS surprise) could swap both the zip and its sha256. The minisign signature, anchored to a key the maintainer holds offline, closes that gap.

---

## One-time keypair setup

You're reading this because you maintain KC and need to generate the signing keypair for the first time, or rotate it.

### 1. Install minisign

| Platform | Install |
|----------|---------|
| Linux (Debian/Ubuntu) | `sudo apt install minisign` |
| Linux (Fedora/RHEL)   | `sudo dnf install minisign` |
| macOS                 | `brew install minisign` |
| Windows (Chocolatey)  | `choco install minisign -y` |
| Windows (Scoop)       | `scoop install minisign` |
| Manual                | [Download from jedisct1/minisign releases](https://github.com/jedisct1/minisign/releases) — single ~150 KB binary, drop in PATH |

Confirm with `minisign -v`.

### 2. Generate the keypair

```bash
# Linux/macOS/Git Bash
bash tools/setup-signing-key.sh
```

```powershell
# Windows native PowerShell
tools\setup-signing-key.ps1
```

The script:

- Refuses to overwrite an existing key (pass `-f` / `-Force` if you've lost the password and need a new one — but read [key rotation](#key-rotation) first)
- Calls `minisign -G` which prompts twice for a password
- Saves `kc-minisign.pub` + `kc-minisign.key` under `~/.keys/`
- Prints the public key contents and the next-step checklist

**The password has no recovery path.** Type it into your password manager *before* you confirm it in minisign.

### 3. Commit the public key

The script prints the public key contents. Paste them into [`docs/RELEASES.md`](RELEASES.md) under the `Verifying a release > Public key` section, replacing the existing key block. Commit the change. (Old releases stay verifiable against the old key — see [key rotation](#key-rotation).)

### 4. Back up the private key + password

Put both in a password manager (1Password, KeePass, Bitwarden, etc.):

- **File**: `~/.keys/kc-minisign.key` — paste the contents into a secure note. The file is small (~280 bytes), and it's encrypted with your password, but treat both as a single unit.
- **Password**: the one you just typed.

`~/.keys/` is in `.gitignore` — the key file will never land in the repo by accident. But you can also delete the local copy after backing it up. Future signings happen via the GitHub Actions secrets (next step), so you don't need the file on your laptop except for emergencies.

### 5. Configure GitHub Actions secrets

The release workflow signs each release zip in CI. It needs the private key + password as secrets:

1. Open https://github.com/Kitsune-Den/KitsuneCommand/settings/secrets/actions
2. **New repository secret** `KC_MINISIGN_PRIVATE_KEY` — paste the **entire contents** of `~/.keys/kc-minisign.key`, including the leading `untrusted comment:` lines
3. **New repository secret** `KC_MINISIGN_PRIVATE_KEY_PASSWORD` — the password

The CI workflow uses these temporarily (writes the key to a temp file, signs, deletes the temp file). They never appear in logs because GitHub redacts secret values automatically.

### 6. Smoke-test by cutting a release

```bash
# From main, with all today's commits, after a version bump + CHANGELOG update:
git tag -a v2.7.2 -m "v2.7.2 — first signed release"
git push origin v2.7.2
```

GitHub Actions runs the release workflow. When it finishes you'll see a draft release with three assets:

- `KitsuneCommand-v2.7.2.zip`
- `KitsuneCommand-v2.7.2.zip.sha256`
- `KitsuneCommand-v2.7.2.zip.minisig`

Verify locally before publishing the draft:

```bash
minisign -Vm KitsuneCommand-v2.7.2.zip -P <your-public-key>
```

If the signature verifies, hit Publish on the draft release.

---

## Key rotation

If you need to change the keypair — lost password, suspected compromise, or just rotating periodically — the steps are:

1. **Generate a new keypair**: `bash tools/setup-signing-key.sh -f` (or `-Force` on PowerShell). Overwrites the local copy. Old keypair stays in your password manager backup but isn't usable for new signings.
2. **Update `docs/RELEASES.md`** with the new public key. Add a note about when the rotation happened — old releases stay verifiable with the old public key, new ones use the new one.
3. **Update the GitHub secrets** — both `KC_MINISIGN_PRIVATE_KEY` and `KC_MINISIGN_PRIVATE_KEY_PASSWORD` get replaced.
4. **Document in the release notes** for the first release on the new key.

Older releases stay verifiable against the old public key (which lives forever in `docs/RELEASES.md`'s history). New releases verify against the new key. Both states are normal.

---

## Common failure modes

| Symptom | Cause | Fix |
|---------|-------|-----|
| `[sign-release] Skipping minisign signature: no private key` in CI logs | Secrets not set | Step 5 above |
| `Wrong password` from minisign | Password mismatch | Re-set `KC_MINISIGN_PRIVATE_KEY_PASSWORD`. If you lost the password, see [key rotation](#key-rotation) |
| `WARNING: key file does NOT start with 'untrusted comment:'` in CI logs | `KC_MINISIGN_PRIVATE_KEY` secret was pasted incompletely — only the base64 body, not the full file contents | Open `~/.keys/kc-minisign.key` in a text editor, copy **both lines** (the `untrusted comment: ...` header AND the base64 body), paste both as the secret value |
| `<tempfile>: No error` and `minisign exited 2` | Older symptom of the same issue above — pre-hardening sign-release.ps1 didn't print the header check | After this PR merges, the WARNING above should appear instead; re-set the secret with both lines |
| `Signature didn't verify` on the downloader's side | Zip was modified post-signing | Re-download. If it persists, open an issue. Don't extract the zip. |
| `bash: minisign: command not found` locally | minisign not installed | Step 1 |

---

## Why minisign (and not GPG)?

Three reasons:

1. **Project consistency.** The PackRelay launcher already uses minisign for its updater signatures (Tauri's update system is built on minisign internally). One signing primitive across the product family is easier to reason about.
2. **Smaller surface area.** GPG has a long footgun list (key servers, web of trust, subkeys, expirations). minisign has one keypair, one password, one signature. Less to misconfigure.
3. **Lighter install on the verifier side.** A KC server admin verifying a download on Windows can install minisign with one command. GPG on Windows requires Gpg4win which is significantly heavier.

If you need to verify on a system that already has `gpg` and you don't want to install minisign, you can still use the `.sha256` file alone — it's the integrity floor. The minisig adds authenticity on top.
