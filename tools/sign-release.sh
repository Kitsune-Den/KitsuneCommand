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
    # CI mode: secret in env var. Write to temp file, sign, delete.
    KEY_PATH=$(mktemp)
    KEY_IS_TEMP=1
    # Use printf instead of echo to avoid adding/stripping trailing
    # newlines that minisign cares about.
    printf '%s' "$KC_MINISIGN_PRIVATE_KEY" > "$KEY_PATH"
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

if [ -n "${KC_MINISIGN_PRIVATE_KEY_PASSWORD:-}" ]; then
    # Pipe password via stdin. minisign reads its key password from
    # stdin when one isn't interactively available.
    printf '%s\n' "$KC_MINISIGN_PRIVATE_KEY_PASSWORD" | \
        minisign -Sm "$ZIP_FULL" -s "$KEY_PATH" -t "$TRUST_COMMENT" -x "$SIG_PATH"
else
    # Interactive: minisign prompts on stderr.
    minisign -Sm "$ZIP_FULL" -s "$KEY_PATH" -t "$TRUST_COMMENT" -x "$SIG_PATH"
fi

echo "[sign-release] Wrote $SIG_PATH"
echo
echo "Release sidecar files ready:"
echo "  $SHA_PATH"
echo "  $SIG_PATH"
echo
echo "Attach all three (zip + .sha256 + .minisig) to the GitHub release."
