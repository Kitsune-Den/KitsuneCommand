#!/usr/bin/env bash
#
# One-time minisign keypair generation for KC releases.
#
# Runs `minisign -G` interactively to create the KC release-signing
# keypair, then prints next-steps for: (1) committing the public key
# to docs/RELEASES.md, (2) registering CI secrets so the GHA release
# workflow can sign, and (3) backing up the private key + password to
# a password manager.
#
# Idempotent: refuses to overwrite an existing key file unless -f is
# passed. Losing the private key is recoverable (cut a new keypair,
# bump the public key in docs) but bumpy — fresh-key releases break
# automatic verifiability of older releases.
#
# See docs/SIGNING.md for the full one-time-setup walkthrough.

set -euo pipefail

KEY_DIR="$HOME/.keys"
FORCE=0

while [ $# -gt 0 ]; do
    case "$1" in
        -d|--dir)    KEY_DIR="$2"; shift 2 ;;
        -f|--force)  FORCE=1; shift ;;
        -h|--help)
            sed -n '3,18p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "Unknown arg: $1" >&2
            exit 2
            ;;
    esac
done

if ! command -v minisign >/dev/null 2>&1; then
    echo
    echo "minisign is not installed."
    echo
    echo "Install options:"
    echo "  Linux:    apt install minisign   (or yum/dnf install minisign)"
    echo "  macOS:    brew install minisign"
    echo "  Windows:  choco install minisign -y    (or scoop install minisign)"
    echo "  Manual:   https://github.com/jedisct1/minisign/releases"
    echo
    echo "Then re-run this script."
    exit 1
fi

mkdir -p "$KEY_DIR"
PUB_KEY="$KEY_DIR/kc-minisign.pub"
PRIV_KEY="$KEY_DIR/kc-minisign.key"

if [ -f "$PRIV_KEY" ] && [ "$FORCE" -ne 1 ]; then
    echo
    echo "Private key already exists at: $PRIV_KEY"
    echo "Refusing to overwrite. Pass -f / --force if you really mean it."
    echo
    echo "If you've lost the password but still have the key file, you cannot"
    echo "recover the password — generate a fresh keypair with -f, update"
    echo "docs/RELEASES.md with the new public key, and re-set the CI secrets."
    exit 1
fi

echo
echo "=== Generating KC release-signing keypair ==="
echo
echo "minisign will prompt you for a password TWICE."
echo "Use a strong unique password and save it in your password manager"
echo "RIGHT NOW. There's no recovery if it's lost."
echo

minisign -G -p "$PUB_KEY" -s "$PRIV_KEY"

echo
echo "=== Keypair generated ==="
echo
echo "Public key  (commit to repo, paste into docs/RELEASES.md):"
echo "  $PUB_KEY"
echo
echo "Private key (NEVER commit; never share; back up to password manager):"
echo "  $PRIV_KEY"
echo
echo "Public key contents:"
echo "----------"
cat "$PUB_KEY"
echo "----------"
echo
echo "Next steps (see docs/SIGNING.md for the full walkthrough):"
echo
echo "1. Paste the public key into docs/RELEASES.md under the 'Verifying"
echo "   a release' section, replacing the <PUBLIC-KEY-PLACEHOLDER> marker."
echo
echo "2. Back up the private key + password to your password manager:"
echo "   - The file $PRIV_KEY"
echo "   - The password you just typed"
echo
echo "3. Configure GitHub Actions secrets at"
echo "   https://github.com/Kitsune-Den/KitsuneCommand/settings/secrets/actions"
echo "   - KC_MINISIGN_PRIVATE_KEY            -> contents of $PRIV_KEY"
echo "     (paste the whole file contents, including the leading comment lines)"
echo "   - KC_MINISIGN_PRIVATE_KEY_PASSWORD   -> the password you used"
echo
echo "4. Cut a fresh release (tag push) — the release workflow will pick up"
echo "   the secrets and produce .sha256 + .minisig alongside the zip."
