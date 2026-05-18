<#
.SYNOPSIS
    Compute SHA-256 + minisign signature for a release zip.

.DESCRIPTION
    Produces two sidecar files next to the input zip:
      <name>.zip.sha256   — BSD-style sha256 sum line ("<hex>  <name>")
      <name>.zip.minisig  — minisign Ed25519 signature

    Both files are what the manual release process (and the GHA release
    workflow once #136 lands) attach to the GitHub release alongside the
    zip itself. End users verify via:

      minisign -Vm KitsuneCommand-vX.Y.Z.zip -P <public-key>
      sha256sum -c KitsuneCommand-vX.Y.Z.zip.sha256

    The public key lives in docs/RELEASES.md.

.PARAMETER ZipPath
    Path to the zip to sign. Required.

.PARAMETER KeyFile
    Path to the minisign private key file. Default: $HOME/.keys/kc-minisign.key.
    In CI, pass an empty value and set KC_MINISIGN_PRIVATE_KEY env to the
    .key file *contents*; the script writes it to a temp file, signs, then
    nukes the temp file.

.NOTES
    Graceful skip: if KC_MINISIGN_PRIVATE_KEY isn't set AND -KeyFile
    doesn't exist locally, the script still produces the .sha256 file but
    logs "skipping signing" and exits 0. This matches deploy.sh's pattern
    when rsync is missing — the build succeeds, just without one of the
    sidecar files.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$ZipPath,

    [string]$KeyFile = $(Join-Path $HOME ".keys/kc-minisign.key")
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ZipPath)) {
    Write-Error "Zip not found: $ZipPath"
}

$zipFull = (Resolve-Path $ZipPath).Path
$zipDir  = Split-Path -Parent $zipFull
$zipName = Split-Path -Leaf   $zipFull
$shaPath = "$zipFull.sha256"
$sigPath = "$zipFull.minisig"

# ---------- 1. SHA-256 sum ----------
Write-Host "[sign-release] Computing SHA-256 for $zipName ..."
$hash = (Get-FileHash -Path $zipFull -Algorithm SHA256).Hash.ToLower()
# BSD-style line matching `sha256sum`'s output exactly. CRITICAL:
# the line must end with a bare LF (0x0A), NOT CRLF — `sha256sum -c`
# parses the filename up to the newline and CRLF leaves a literal \r
# in the filename, causing "No such file or directory" on Linux/Git
# Bash. Out-File and Add-Content default to CRLF on Windows;
# [IO.File]::WriteAllText with explicit "`n" produces LF.
$line = "$hash  $zipName`n"
[System.IO.File]::WriteAllText($shaPath, $line, (New-Object System.Text.UTF8Encoding $false))
Write-Host "[sign-release] Wrote $shaPath"

# ---------- 2. minisign signature ----------
$skipReason = $null
$keyPath    = $null
$keyIsTemp  = $false

if ($env:KC_MINISIGN_PRIVATE_KEY) {
    # CI mode: secret in env var. Write to temp file, sign, delete.
    $keyPath   = [System.IO.Path]::GetTempFileName()
    $keyIsTemp = $true
    [System.IO.File]::WriteAllText($keyPath, $env:KC_MINISIGN_PRIVATE_KEY)
} elseif (Test-Path $KeyFile) {
    # Local mode: key on disk at the expected path.
    $keyPath = $KeyFile
} else {
    $skipReason = "no private key (KC_MINISIGN_PRIVATE_KEY env empty AND $KeyFile not on disk)"
}

if ($skipReason) {
    Write-Warning "[sign-release] Skipping minisign signature: $skipReason"
    Write-Warning "[sign-release] .sha256 produced; .minisig SKIPPED."
    Write-Warning "[sign-release] To enable signing, see docs/SIGNING.md."
    exit 0
}

if (-not (Get-Command minisign -ErrorAction SilentlyContinue)) {
    Write-Error "[sign-release] minisign not in PATH. Install via 'choco install minisign -y' or download from https://github.com/jedisct1/minisign/releases."
}

Write-Host "[sign-release] Signing $zipName with minisign ..."

# Build trusted comment so the signature carries human-meaningful provenance.
$timestamp = (Get-Date -AsUTC).ToString("yyyy-MM-ddTHH:mm:ssZ")
$trustComment = "KitsuneCommand release: $zipName signed $timestamp"

try {
    if ($env:KC_MINISIGN_PRIVATE_KEY_PASSWORD) {
        # Pipe the password via stdin. minisign reads its key password
        # from stdin when one isn't interactively available.
        $env:KC_MINISIGN_PRIVATE_KEY_PASSWORD | & minisign -Sm $zipFull -s $keyPath -t $trustComment -x $sigPath
    } else {
        # Interactive: minisign prompts on stderr.
        & minisign -Sm $zipFull -s $keyPath -t $trustComment -x $sigPath
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[sign-release] minisign exited $LASTEXITCODE"
    }
} finally {
    if ($keyIsTemp -and (Test-Path $keyPath)) {
        Remove-Item -Force $keyPath
    }
}

Write-Host "[sign-release] Wrote $sigPath"
Write-Host ""
Write-Host "Release sidecar files ready:"
Write-Host "  $shaPath"
Write-Host "  $sigPath"
Write-Host ""
Write-Host "Attach all three (zip + .sha256 + .minisig) to the GitHub release."
