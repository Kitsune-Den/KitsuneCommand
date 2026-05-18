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
    # CI mode: secret in env var. Normalize + write to temp file, sign, delete.
    #
    # Defensive normalization avoids two real failure modes we've hit:
    #   1. CRLF line endings from Windows-pasted secrets. Minisign on
    #      Windows tolerates CRLF in some builds but not all; LF-only
    #      is universally safe.
    #   2. Missing trailing newline. Minisign's `bin_read_line` expects
    #      every line (including the last) to end with `\n`; without
    #      one, the second line's base64 can't be fully consumed.
    #   3. UTF-8 BOM. Some PowerShell file writers prepend EF BB BF,
    #      which minisign reads as the start of the header line and
    #      rejects the file as "not a key file."
    $keyPath   = [System.IO.Path]::GetTempFileName()
    $keyIsTemp = $true

    $rawKey = $env:KC_MINISIGN_PRIVATE_KEY
    # Strip CR characters → LF-only.
    $normalizedKey = $rawKey -replace "`r", ""
    # Ensure exactly one trailing newline.
    if (-not $normalizedKey.EndsWith("`n")) {
        $normalizedKey += "`n"
    }
    # UTF-8 no-BOM. The explicit encoding object guarantees no BOM
    # regardless of which PowerShell flavor is running.
    [System.IO.File]::WriteAllText(
        $keyPath,
        $normalizedKey,
        (New-Object System.Text.UTF8Encoding $false)
    )

    # Sanity-print the first line header (the "untrusted comment: ..."
    # line) WITHOUT exposing the base64 body, so a future failure log
    # makes the cause obvious. Reveals nothing secret — the header is
    # the same on every minisign key file.
    $firstLine = ($normalizedKey -split "`n", 2)[0]
    $keyBytes  = (Get-Item $keyPath).Length
    Write-Host "[sign-release] Wrote temp key file ($keyBytes bytes, first line: '$firstLine')"
    if (-not $firstLine.StartsWith("untrusted comment:")) {
        Write-Warning "[sign-release] WARNING: key file does NOT start with 'untrusted comment:' — minisign will reject this. Check that KC_MINISIGN_PRIVATE_KEY contains the FULL .key file contents (both lines, not just the base64 body)."
    }
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
    # Pass the password via MINISIGN_PASSWORD env var (supported since
    # minisign 0.10, 2021). Cleaner than stdin-piping — no shell
    # quirks around how PowerShell pipes strings to native commands,
    # and minisign's stdout/stderr land directly in our GHA log so
    # we see the real error if one happens.
    if ($env:KC_MINISIGN_PRIVATE_KEY_PASSWORD) {
        $env:MINISIGN_PASSWORD = $env:KC_MINISIGN_PRIVATE_KEY_PASSWORD
    }
    & minisign -Sm $zipFull -s $keyPath -t $trustComment -x $sigPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "[sign-release] minisign exited $LASTEXITCODE. See docs/SIGNING.md § Common failure modes."
    }
} finally {
    # Always clean up. Both the temp key file (if we made one) and
    # the password env var so it doesn't leak into subsequent steps.
    if ($keyIsTemp -and (Test-Path $keyPath)) {
        Remove-Item -Force $keyPath
    }
    if ($env:MINISIGN_PASSWORD) {
        Remove-Item Env:\MINISIGN_PASSWORD -ErrorAction SilentlyContinue
    }
}

Write-Host "[sign-release] Wrote $sigPath"
Write-Host ""
Write-Host "Release sidecar files ready:"
Write-Host "  $shaPath"
Write-Host "  $sigPath"
Write-Host ""
Write-Host "Attach all three (zip + .sha256 + .minisig) to the GitHub release."
