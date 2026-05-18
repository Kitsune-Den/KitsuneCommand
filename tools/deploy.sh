#!/bin/bash
#
# Deploy KitsuneCommand to a remote 7DTD server.
#
# Replaces all stock mod files (DLLs, wwwroot, migrations, native libs)
# while preserving admin-modifiable paths:
#   - Config/appsettings.json   (admin's ports + URLs + token expiry)
#   - Plugins/                  (admin-dropped plugin DLLs)
#
# State that lives OUTSIDE this mod folder isn't touched:
#   - KitsuneCommand.db         (game root)
#   - KitsuneBackups/           (game root)
#   - KitsuneModpacks/          (game root)
#
# Usage:
#   tools/deploy.sh [--build] [--yes]
#
#   --build   Run tools/build.sh first (default: assume dist/ is fresh)
#   --yes     Skip the confirmation prompt
#
# Configuration is read from .deploy.env in the repo root (gitignored)
# or from process environment variables. Required keys:
#
#   KC_DEPLOY_HOST            e.g. 87.99.153.20 or kitsuneden.net
#   KC_DEPLOY_USER            SSH user with sudo+chown access (default: root)
#   KC_DEPLOY_MODS_PATH       remote 7DTD Mods/ dir (default: /home/ada/7d2d-server/Mods)
#   KC_DEPLOY_SERVICE_USER    user that owns the mod files (default: ada)
#   KC_DEPLOY_SERVICE_NAME    systemd unit (default: 7daystodie)
#
# See .deploy.env.example for the full template.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MOD_NAME="KitsuneCommand"
DIST_DIR="$REPO_ROOT/dist/$MOD_NAME"

# Paths inside the mod folder that survive a deploy. The list is short on
# purpose — anything not here gets replaced with what's in dist/. Both
# entries match rsync's exclude pattern syntax (no leading slash).
PRESERVE_PATHS=(
    "Config/appsettings.json"
    "Plugins/"
)

# Parse args.
DO_BUILD=0
SKIP_PROMPT=0
for arg in "$@"; do
    case "$arg" in
        --build) DO_BUILD=1 ;;
        --yes|-y) SKIP_PROMPT=1 ;;
        --help|-h)
            sed -n '3,33p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "Unknown argument: $arg" >&2
            echo "Run 'tools/deploy.sh --help' for usage." >&2
            exit 2
            ;;
    esac
done

# Load config from .deploy.env if present. Env vars already in the
# process win — useful for CI / one-off overrides.
if [ -f "$REPO_ROOT/.deploy.env" ]; then
    # shellcheck disable=SC1091
    set -a
    . "$REPO_ROOT/.deploy.env"
    set +a
fi

KC_DEPLOY_USER="${KC_DEPLOY_USER:-root}"
KC_DEPLOY_MODS_PATH="${KC_DEPLOY_MODS_PATH:-/home/ada/7d2d-server/Mods}"
KC_DEPLOY_SERVICE_USER="${KC_DEPLOY_SERVICE_USER:-ada}"
KC_DEPLOY_SERVICE_NAME="${KC_DEPLOY_SERVICE_NAME:-7daystodie}"

if [ -z "${KC_DEPLOY_HOST:-}" ]; then
    echo "ERROR: KC_DEPLOY_HOST not set." >&2
    echo "       Either set it in .deploy.env or export it: KC_DEPLOY_HOST=example.com tools/deploy.sh" >&2
    exit 1
fi

REMOTE="$KC_DEPLOY_USER@$KC_DEPLOY_HOST"
REMOTE_MOD_DIR="$KC_DEPLOY_MODS_PATH/$MOD_NAME"

# Sanity: don't deploy a stale build silently.
if [ "$DO_BUILD" -eq 1 ]; then
    echo "=== Building $MOD_NAME ==="
    bash "$REPO_ROOT/tools/build.sh"
fi
if [ ! -d "$DIST_DIR" ]; then
    echo "ERROR: $DIST_DIR doesn't exist." >&2
    echo "       Run with --build first or run tools/build.sh manually." >&2
    exit 1
fi
DLL_PATH="$DIST_DIR/$MOD_NAME.dll"
if [ ! -f "$DLL_PATH" ]; then
    echo "ERROR: $DLL_PATH missing — dist looks incomplete." >&2
    exit 1
fi

# Show the plan + ask for confirmation.
cat <<EOF
=== Deploy plan ===
  Local source:    $DIST_DIR
  Remote target:   $REMOTE:$REMOTE_MOD_DIR
  Service:         systemctl restart $KC_DEPLOY_SERVICE_NAME (as $KC_DEPLOY_USER)
  File owner:      $KC_DEPLOY_SERVICE_USER:$KC_DEPLOY_SERVICE_USER

Preserved (NOT touched on the remote):
EOF
for p in "${PRESERVE_PATHS[@]}"; do
    echo "    $p"
done
echo

if [ "$SKIP_PROMPT" -eq 0 ]; then
    read -r -p "Proceed? [y/N] " reply
    case "$reply" in
        y|Y|yes|YES) ;;
        *) echo "Aborted."; exit 1 ;;
    esac
fi

# Build the rsync exclude arguments. Only consumed by sync_via_rsync;
# the scp+snapshot path uses PRESERVE_PATHS directly.
RSYNC_EXCLUDES=()
for p in "${PRESERVE_PATHS[@]}"; do
    RSYNC_EXCLUDES+=(--exclude="$p")
done

# Two transport flavors. rsync is the fast path — one round-trip,
# delta transfer, preserves perms, --exclude keeps Config/Plugins
# untouched in a single pass. When rsync isn't on PATH (typically
# native Windows Git Bash, which doesn't ship it), fall back to the
# same scp + snapshot pattern deploy.ps1 uses on Windows. Same
# observable result, more round-trips.
sync_via_rsync() {
    # -a   archive (preserves perms, recurses)
    # -z   compress in transit
    # -v   verbose (shows what changed)
    # --delete  remove files on remote that aren't in source (orphan cleanup)
    # Trailing slash on source dir is required — rsync syntax for "copy the
    # contents of, not the dir itself."
    rsync -avz --delete \
        "${RSYNC_EXCLUDES[@]}" \
        "$DIST_DIR/" \
        "$REMOTE:$REMOTE_MOD_DIR/"
}

sync_via_scp_snapshot() {
    # 1. Snapshot the admin-modifiable bits to a remote /tmp dir and
    #    wipe the mod folder. The script is piped to `ssh bash -s` via
    #    stdin (not passed as a multi-line argv) so newlines survive —
    #    same reason deploy.ps1 does it this way on Windows OpenSSH.
    echo "  Snapshotting preserved paths + wiping remote..."
    ssh "$REMOTE" "bash -s" <<EOF
set -e
TMP=\$(mktemp -d /tmp/kc-preserve-XXXXXX)
mkdir -p "\$TMP/Config" "\$TMP/Plugins"
if [ -f "$REMOTE_MOD_DIR/Config/appsettings.json" ]; then
    cp -p "$REMOTE_MOD_DIR/Config/appsettings.json" "\$TMP/Config/"
    echo "  preserved Config/appsettings.json"
fi
if [ -d "$REMOTE_MOD_DIR/Plugins" ]; then
    cp -rp "$REMOTE_MOD_DIR/Plugins/." "\$TMP/Plugins/" 2>/dev/null || true
    PLUGIN_COUNT=\$(find "\$TMP/Plugins" -mindepth 1 -maxdepth 1 -name '*.dll' 2>/dev/null | wc -l)
    echo "  preserved Plugins/ (\$PLUGIN_COUNT plugin DLL(s))"
fi
echo "\$TMP" > /tmp/kc-deploy-last-snapshot
rm -rf "$REMOTE_MOD_DIR"
EOF

    # 2. scp the fresh dist into the parent dir — scp recreates the
    #    source folder name inside it.
    echo "  Copying fresh dist..."
    scp -r "$DIST_DIR" "$REMOTE:$KC_DEPLOY_MODS_PATH/"

    # 3. Restore the snapshot. (Ownership + service restart happen
    #    after this function returns, same as the rsync path.)
    echo "  Restoring preserved paths..."
    ssh "$REMOTE" "bash -s" <<EOF
set -e
TMP=\$(cat /tmp/kc-deploy-last-snapshot 2>/dev/null || true)
if [ -n "\$TMP" ] && [ -d "\$TMP" ]; then
    if [ -f "\$TMP/Config/appsettings.json" ]; then
        cp -p "\$TMP/Config/appsettings.json" "$REMOTE_MOD_DIR/Config/"
        echo "  restored Config/appsettings.json"
    fi
    if [ -d "\$TMP/Plugins" ]; then
        mkdir -p "$REMOTE_MOD_DIR/Plugins"
        cp -rp "\$TMP/Plugins/." "$REMOTE_MOD_DIR/Plugins/" 2>/dev/null || true
        PLUGIN_COUNT=\$(find "$REMOTE_MOD_DIR/Plugins" -mindepth 1 -maxdepth 1 -name '*.dll' 2>/dev/null | wc -l)
        echo "  restored Plugins/ (\$PLUGIN_COUNT plugin DLL(s))"
    fi
    rm -rf "\$TMP"
    rm -f /tmp/kc-deploy-last-snapshot
fi
EOF
}

echo "=== Syncing files ==="
if command -v rsync >/dev/null 2>&1; then
    sync_via_rsync
else
    echo "  rsync not on PATH — falling back to scp + snapshot pattern."
    echo "  This is normal on native Windows Git Bash. On Linux/macOS,"
    echo "  installing rsync gives a faster delta transfer."
    sync_via_scp_snapshot
fi

echo "=== Fixing ownership + restarting service ==="
ssh "$REMOTE" "bash -s" <<EOF
set -e
chown -R $KC_DEPLOY_SERVICE_USER:$KC_DEPLOY_SERVICE_USER '$REMOTE_MOD_DIR'
systemctl restart $KC_DEPLOY_SERVICE_NAME
EOF

# Wait a few seconds + verify the service came back up. If it crashed
# during boot, surface that loudly so the operator can roll back.
echo "=== Verifying service health ==="
sleep 5
STATUS=$(ssh "$REMOTE" "systemctl is-active $KC_DEPLOY_SERVICE_NAME" 2>&1 || true)
case "$STATUS" in
    active)
        echo "✓ $KC_DEPLOY_SERVICE_NAME is active"
        ;;
    activating)
        echo "… $KC_DEPLOY_SERVICE_NAME is still starting (this is normal — 7DTD takes 30-90s)"
        ;;
    *)
        echo "WARNING: $KC_DEPLOY_SERVICE_NAME is '$STATUS' — last 20 log lines:" >&2
        ssh "$REMOTE" "journalctl -u $KC_DEPLOY_SERVICE_NAME -n 20 --no-pager" >&2 || true
        exit 1
        ;;
esac

echo
echo "Deploy complete. Hard-refresh the panel (Ctrl+F5) to bust the browser cache."
