#requires -Version 5.1
<#
.SYNOPSIS
    Deploy KitsuneCommand to a remote 7DTD server.

.DESCRIPTION
    Replaces all stock mod files (DLLs, wwwroot, migrations, native libs)
    while preserving admin-modifiable paths:
      - Config/appsettings.json   (admin's ports + URLs + token expiry)
      - Plugins/                  (admin-dropped plugin DLLs)

    State that lives OUTSIDE this mod folder isn't touched:
      - KitsuneCommand.db         (game root)
      - KitsuneBackups/           (game root)
      - KitsuneModpacks/          (game root)

    Configuration is read from .deploy.env in the repo root (gitignored)
    or from process environment variables. See .deploy.env.example for
    the full template.

    Required keys:
      KC_DEPLOY_HOST            e.g. 87.99.153.20 or kitsuneden.net
      KC_DEPLOY_USER            SSH user (default: root)
      KC_DEPLOY_MODS_PATH       remote Mods/ dir (default: /home/ada/7d2d-server/Mods)
      KC_DEPLOY_SERVICE_USER    file owner (default: ada)
      KC_DEPLOY_SERVICE_NAME    systemd unit (default: 7daystodie)

.PARAMETER Build
    Run tools/build.ps1 first. Default: assume dist/ is fresh.

.PARAMETER Yes
    Skip the confirmation prompt.

.EXAMPLE
    tools\deploy.ps1
    Deploy whatever's in dist/ to the host configured in .deploy.env.

.EXAMPLE
    tools\deploy.ps1 -Build -Yes
    Run build first, deploy, no prompt. Use in scripts / CI.

.NOTES
    PowerShell-native — no rsync needed. Uses ssh + scp (built into
    Win10+ via the OpenSSH Client optional feature). The bash flavor
    in deploy.sh uses rsync's --exclude for the preserve-paths; this
    one snapshots them to a remote temp dir, wipes the mod folder,
    scps the fresh dist, then restores the snapshot. Same observable
    result, more round-trips.
#>

[CmdletBinding()]
param(
    [switch]$Build,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$modName  = "KitsuneCommand"
$distDir  = "$repoRoot\dist\$modName"

# Paths inside the mod folder that survive a deploy. The list is short
# on purpose — anything not here gets replaced with what's in dist/.
$preservePaths = @(
    "Config/appsettings.json",
    "Plugins"
)

# Load .deploy.env (if present) into the process env. Anything already
# set in the process wins — useful for CI / one-off overrides.
$envFile = Join-Path $repoRoot ".deploy.env"
if (Test-Path $envFile) {
    foreach ($line in Get-Content $envFile) {
        $trimmed = $line.Trim()
        if ($trimmed -eq "" -or $trimmed.StartsWith("#")) { continue }
        $eq = $trimmed.IndexOf("=")
        if ($eq -lt 1) { continue }
        $k = $trimmed.Substring(0, $eq).Trim()
        $v = $trimmed.Substring($eq + 1).Trim().Trim("'`"")
        if (-not [Environment]::GetEnvironmentVariable($k)) {
            [Environment]::SetEnvironmentVariable($k, $v)
        }
    }
}

$host_       = $env:KC_DEPLOY_HOST
$user        = if ($env:KC_DEPLOY_USER) { $env:KC_DEPLOY_USER } else { "root" }
$modsPath    = if ($env:KC_DEPLOY_MODS_PATH) { $env:KC_DEPLOY_MODS_PATH } else { "/home/ada/7d2d-server/Mods" }
$serviceUser = if ($env:KC_DEPLOY_SERVICE_USER) { $env:KC_DEPLOY_SERVICE_USER } else { "ada" }
$serviceName = if ($env:KC_DEPLOY_SERVICE_NAME) { $env:KC_DEPLOY_SERVICE_NAME } else { "7daystodie" }

if (-not $host_) {
    Write-Error "KC_DEPLOY_HOST not set. Either put it in .deploy.env or run with `$env:KC_DEPLOY_HOST = 'example.com' ; tools\deploy.ps1`."
}

$remote        = "$user@$host_"
$remoteModDir  = "$modsPath/$modName"

# Sanity: don't deploy a stale build silently.
if ($Build) {
    Write-Host "=== Building $modName ===" -ForegroundColor Cyan
    & "$repoRoot\tools\build.ps1"
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed" }
}
if (-not (Test-Path $distDir)) {
    Write-Error "$distDir doesn't exist. Run with -Build first or run tools\build.ps1 manually."
}
if (-not (Test-Path "$distDir\$modName.dll")) {
    Write-Error "$distDir\$modName.dll missing — dist looks incomplete."
}

# Show the plan + confirmation.
Write-Host ""
Write-Host "=== Deploy plan ===" -ForegroundColor Cyan
Write-Host "  Local source:    $distDir"
Write-Host "  Remote target:   $remote`:$remoteModDir"
Write-Host "  Service:         systemctl restart $serviceName (as $user)"
Write-Host "  File owner:      $serviceUser`:$serviceUser"
Write-Host ""
Write-Host "Preserved (NOT touched on the remote):" -ForegroundColor Yellow
foreach ($p in $preservePaths) { Write-Host "    $p" }
Write-Host ""

if (-not $Yes) {
    $reply = Read-Host "Proceed? [y/N]"
    if ($reply -notmatch '^(y|Y|yes|YES)$') {
        Write-Host "Aborted." -ForegroundColor Red
        exit 1
    }
}

# 1. Snapshot the admin-modifiable bits to a remote temp dir, then wipe
#    the mod folder. The script is piped to `ssh ... bash -s` via stdin
#    rather than passed as an argv string — Windows OpenSSH joins argv
#    with spaces (not newlines), so a multi-line script-as-argv collapses
#    to one line on the remote and bash chokes on `set -` / `-e` as
#    separate tokens. Stdin-piping preserves newlines verbatim. The bash
#    flavor in deploy.sh uses the same pattern for parity.
Write-Host ""
Write-Host "=== Snapshotting preserved paths + wiping remote ===" -ForegroundColor Cyan
$snapshotScript = @"
set -e
TMP=`$(mktemp -d /tmp/kc-preserve-XXXXXX)
mkdir -p "`$TMP/Config" "`$TMP/Plugins"
if [ -f "$remoteModDir/Config/appsettings.json" ]; then
    cp -p "$remoteModDir/Config/appsettings.json" "`$TMP/Config/"
    echo "  preserved Config/appsettings.json"
fi
if [ -d "$remoteModDir/Plugins" ]; then
    cp -rp "$remoteModDir/Plugins/." "`$TMP/Plugins/" 2>/dev/null || true
    PLUGIN_COUNT=`$(find "`$TMP/Plugins" -mindepth 1 -maxdepth 1 -name '*.dll' 2>/dev/null | wc -l)
    echo "  preserved Plugins/ (`$PLUGIN_COUNT plugin DLL(s))"
fi
echo "`$TMP" > /tmp/kc-deploy-last-snapshot
rm -rf "$remoteModDir"
"@
$snapshotScript | & ssh $remote "bash -s"
if ($LASTEXITCODE -ne 0) { throw "snapshot step failed" }

# 2. scp the fresh dist. The path is the parent dir; scp recreates
#    the source folder name inside it.
Write-Host ""
Write-Host "=== Copying fresh dist ===" -ForegroundColor Cyan
& scp -r $distDir "$remote`:$modsPath/"
if ($LASTEXITCODE -ne 0) { throw "scp failed" }

# 3. Restore the snapshot, fix ownership, restart systemd.
Write-Host ""
Write-Host "=== Restoring preserved paths + ownership + restart ===" -ForegroundColor Cyan
$restoreScript = @"
set -e
TMP=`$(cat /tmp/kc-deploy-last-snapshot 2>/dev/null || true)
if [ -n "`$TMP" ] && [ -d "`$TMP" ]; then
    if [ -f "`$TMP/Config/appsettings.json" ]; then
        cp -p "`$TMP/Config/appsettings.json" "$remoteModDir/Config/"
        echo "  restored Config/appsettings.json"
    fi
    if [ -d "`$TMP/Plugins" ]; then
        mkdir -p "$remoteModDir/Plugins"
        cp -rp "`$TMP/Plugins/." "$remoteModDir/Plugins/" 2>/dev/null || true
        PLUGIN_COUNT=`$(find "$remoteModDir/Plugins" -mindepth 1 -maxdepth 1 -name '*.dll' 2>/dev/null | wc -l)
        echo "  restored Plugins/ (`$PLUGIN_COUNT plugin DLL(s))"
    fi
    rm -rf "`$TMP"
    rm -f /tmp/kc-deploy-last-snapshot
fi
chown -R $serviceUser`:$serviceUser "$remoteModDir"
systemctl restart $serviceName
"@
$restoreScript | & ssh $remote "bash -s"
if ($LASTEXITCODE -ne 0) { throw "restore/restart step failed" }

# 4. Health check. 7DTD takes 30-90s to come back online; we just check
#    that systemd thinks the unit's at least activating rather than
#    failed.
Write-Host ""
Write-Host "=== Verifying service health ===" -ForegroundColor Cyan
Start-Sleep -Seconds 5
$status = (ssh $remote "systemctl is-active $serviceName" 2>&1)
switch -Regex ($status) {
    "^active$" {
        Write-Host "✓ $serviceName is active" -ForegroundColor Green
    }
    "^activating$" {
        Write-Host "… $serviceName is still starting (this is normal — 7DTD takes 30-90s)" -ForegroundColor Yellow
    }
    default {
        Write-Host "WARNING: $serviceName is '$status' — last 20 log lines:" -ForegroundColor Red
        ssh $remote "journalctl -u $serviceName -n 20 --no-pager"
        exit 1
    }
}

Write-Host ""
Write-Host "Deploy complete. Hard-refresh the panel (Ctrl+F5) to bust the browser cache." -ForegroundColor Green
