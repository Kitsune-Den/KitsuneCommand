#!/usr/bin/env bash
#
# Compute SHA-256 + minisign signature for a release zip.
#
# Produces two sidecar files next to the input zip:
#   <name>.zip.sha256   — BSD-style sha256 sum line ("<hex>  <name>")
#   <name>.zip.minisig  — minisign Ed25519 signature
#
# Both files are what the manual release process (and the GHA release
# workflow once #136 lands) attach to the GitHub release alongside the
# zip itself. End users verify via:
#
#   minisign -Vm KitsuneCommand-vX.Y.Z.zip -P <public-key>
#   sha256sum -c KitsuneCommand-vX.Y.Z.zip.sha256
#
# Graceful skip: if KC_MINISIGN_PRIVATE_KEY isn't set AND no key file
# exists at the default path, the script still produces the .sha256 but
# logs "skipping signing" and exits 0. Same posture as deploy.sh when
# rsync is missing — the build succeeds, just without one sidecar.

set -euo pipefail

usage() {
    cat <<EOF
Usage: $0 <zip-path> [key-file]

  <zip-path>   path to the release zip to sign
  [key-file]   minisign private key file (default: ~/.keys/kc-minisign.key)

Env vars (CI mode):
  KC_MINISIGN_PRIVATE_KEY            .key file contents (overrides key-file)
  KC_MINISIGN_PRIVATE_KEY_PASSWORD   password for the private key
EOF
    exit 2
}

[ $# -lt 1 ] && usage

ZIP_PATH="$1"
KEY_FILE="${2:-$HOME/.keys/kc-minisign.key}"

if [ ! -f "$ZIP_PATH" ]; then
    echo "[sign-release] Zip not found: $ZIP_PATH" >&2
    exit 1
fi

ZIP_FULL=$(readlink -f "$ZIP_PATH" 2>/dev/null || realpath "$ZIP_PATH" 2>/dev/null || echo "$ZIP_PATH")
ZIP_NAME=$(basename "$ZIP_FULL")
SHA_PATH="$ZIP_FULL.sha256"
SIG_PATH="$ZIP_FULL.minisig"

# ---------- 1. SHA-256 sum ----------
echo "[sign-release] Computing SHA-256 for $ZIP_NAME ..."

# Prefer sha256sum (Linux/Git Bash); fall back to shasum on macOS.
if command -v sha256sum >/dev/null 2>&1; then
    ( cd "$(dirname "$ZIP_FULL")" && sha256sum "$ZIP_NAME" ) > "$SHA_PATH"
elif command -v shasum >/dev/null 2>&1; then
    ( cd "$(dirname "$ZIP_FULL")" && shasum -a 256 "$ZIP_NAME" ) > "$SHA_PATH"
else
    echo "[sign-release] Neither sha256sum nor shasum found in PATH." >&2
    exit 1
fi
echo "[sign-release] Wrote $SHA_PATH"

# ---------- 2. minisign signature ----------
SKIP_REASON=""
KEY_PATH=""
KEY_IS_TEMP=0

if [ -n "${KC_MINISIGN_PRIVATE_KEY:-}" ]; then
    # CI mode: secret in env var. Normalize + write to temp file, sign, delete.
    #
    # Defensive normalization avoids three failure modes:
    #   1. CRLF line endings from Windows-pasted secrets — strip to LF.
    #   2. Missing trailing newline — minisign's bin_read_line wants one.
    #   3. UTF-8 BOM — minisign reads it as part of the header and rejects.
    KEY_PATH=$(mktemp)
    KEY_IS_TEMP=1
    # tr -d removes CR chars; printf '%s\n' guarantees exactly one
    # trailing newline regardless of what the source had. The BOM
    # issue is bash-side a non-issue (bash doesn't add a BOM) but the
    # normalization is consistent with the PowerShell flavor.
    printf '%s\n' "$KC_MINISIGN_PRIVATE_KEY" | tr -d '\r' > "$KEY_PATH"

    # Sanity-print the first line header so a future failure log
    # makes the cause obvious. Reveals nothing secret — every
    # minisign key starts with this same header.
    FIRST_LINE=$(head -n 1 "$KEY_PATH")
    KEY_BYTES=$(wc -c < "$KEY_PATH")
    echo "[sign-release] Wrote temp key file ($KEY_BYTES bytes, first line: '$FIRST_LINE')"
    if ! echo "$FIRST_LINE" | grep -q '^untrusted comment:'; then
        echo "[sign-release] WARNING: key file does NOT start with 'untrusted comment:' — minisign will reject this. Check that KC_MINISIGN_PRIVATE_KEY contains the FULL .key file contents (both lines, not just the base64 body)." >&2
    fi
elif [ -f "$KEY_FILE" ]; then
    KEY_PATH="$KEY_FILE"
else
    SKIP_REASON="no private key (KC_MINISIGN_PRIVATE_KEY env empty AND $KEY_FILE not on disk)"
fi

cleanup_key() {
    if [ "$KEY_IS_TEMP" -eq 1 ] && [ -n "$KEY_PATH" ] && [ -f "$KEY_PATH" ]; then
        rm -f "$KEY_PATH"
    fi
}
trap cleanup_key EXIT

if [ -n "$SKIP_REASON" ]; then
    echo "[sign-release] Skipping minisign signature: $SKIP_REASON" >&2
    echo "[sign-release] .sha256 produced; .minisig SKIPPED." >&2
    echo "[sign-release] To enable signing, see docs/SIGNING.md." >&2
    exit 0
fi

if ! command -v minisign >/dev/null 2>&1; then
    echo "[sign-release] minisign not in PATH." >&2
    echo "[sign-release] Linux: apt/yum install minisign. macOS: brew install minisign." >&2
    echo "[sign-release] Windows: 'choco install minisign -y' or download from https://github.com/jedisct1/minisign/releases." >&2
    exit 1
fi

echo "[sign-release] Signing $ZIP_NAME with minisign ..."

TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
TRUST_COMMENT="KitsuneCommand release: $ZIP_NAME signed $TIMESTAMP"

# Resolve password from env. Preferred MINISIGN_PASSWORD, fallback
# KC_MINISIGN_PRIVATE_KEY_PASSWORD for local-dev compatibility.
PASSWORD="${MINISIGN_PASSWORD:-${KC_MINISIGN_PRIVATE_KEY_PASSWORD:-}}"
if [ -z "$PASSWORD" ]; then
    cat >&2 <<'PWERR'
[sign-release] Password env var not set.
Neither MINISIGN_PASSWORD nor KC_MINISIGN_PRIVATE_KEY_PASSWORD found
in the calling environment. In CI, check that the
KC_MINISIGN_PRIVATE_KEY_PASSWORD GitHub secret is configured at:
  https://github.com/Kitsune-Den/KitsuneCommand/settings/secrets/actions
PWERR
    exit 1
fi
echo "[sign-release] Password resolved (length: ${#PASSWORD})."

# Pipe password to minisign's stdin. The MINISIGN_PASSWORD env var
# SHOULD work since minisign 0.10 (2021), but the chocolatey-
# installed Windows build on windows-latest ignores it and falls
# through to its interactive get_password() prompt — which fails
# on CI (no TTY). Stdin-pipe works on every minisign version. We
# also export MINISIGN_PASSWORD as belt-and-suspenders.
export MINISIGN_PASSWORD="$PASSWORD"
printf '%s\n' "$PASSWORD" | minisign -Sm "$ZIP_FULL" -s "$KEY_PATH" -t "$TRUST_COMMENT" -x "$SIG_PATH"

echo "[sign-release] Wrote $SIG_PATH"
echo
echo "Release sidecar files ready:"
echo "  $SHA_PATH"
echo "  $SIG_PATH"
echo
echo "Attach all three (zip + .sha256 + .minisig) to the GitHub release."
