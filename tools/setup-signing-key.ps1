<#
.SYNOPSIS
    One-time minisign keypair generation for KC releases.

.DESCRIPTION
    Runs `minisign -G` interactively to create the KC release-signing
    keypair, then prints next-steps for: (1) committing the public key
    to docs/RELEASES.md, (2) registering CI secrets so the GHA release
    workflow can sign, and (3) backing up the private key + password to
    a password manager.

    Idempotent: refuses to overwrite an existing key file unless -Force
    is passed. Losing the private key is recoverable (cut a new keypair,
    bump the public key in docs) but bumpy — fresh-key releases break
    automatic verifiability of older releases.

.PARAMETER KeyDir
    Where to put the keypair on local disk. Default: $HOME\.keys
    The `.keys/` directory is in .gitignore by default — keep it that way.

.PARAMETER Force
    Overwrite an existing key file. Don't use this casually.

.NOTES
    See docs/SIGNING.md for the full one-time-setup walkthrough.
#>

[CmdletBinding()]
param(
    [string]$KeyDir = $(Join-Path $HOME ".keys"),
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command minisign -ErrorAction SilentlyContinue)) {
    Write-Host ""
    Write-Host "minisign is not installed." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Install options on Windows:"
    Write-Host "  1. choco install minisign -y       (if you have Chocolatey)"
    Write-Host "  2. scoop install minisign          (if you have Scoop)"
    Write-Host "  3. Download manually from:         https://github.com/jedisct1/minisign/releases"
    Write-Host "     -> extract minisign.exe to a folder on your PATH"
    Write-Host ""
    Write-Host "Then re-run this script."
    exit 1
}

if (-not (Test-Path $KeyDir)) {
    New-Item -ItemType Directory -Path $KeyDir | Out-Null
}

$PubKeyPath = Join-Path $KeyDir "kc-minisign.pub"
$PrivKeyPath = Join-Path $KeyDir "kc-minisign.key"

if ((Test-Path $PrivKeyPath) -and (-not $Force)) {
    Write-Host ""
    Write-Host "Private key already exists at: $PrivKeyPath" -ForegroundColor Yellow
    Write-Host "Refusing to overwrite. Pass -Force if you really mean it."
    Write-Host ""
    Write-Host "If you've lost the password but still have the key file, you cannot"
    Write-Host "recover the password — generate a fresh keypair with -Force, update"
    Write-Host "docs/RELEASES.md with the new public key, and re-set the CI secrets."
    exit 1
}

Write-Host ""
Write-Host "=== Generating KC release-signing keypair ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "minisign will prompt you for a password TWICE."
Write-Host "Use a strong unique password and save it in your password manager"
Write-Host "RIGHT NOW. There's no recovery if it's lost."
Write-Host ""

& minisign -G -p $PubKeyPath -s $PrivKeyPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "minisign exited $LASTEXITCODE — keypair NOT generated."
}

Write-Host ""
Write-Host "=== Keypair generated ===" -ForegroundColor Green
Write-Host ""
Write-Host "Public key  (commit to repo, paste into docs/RELEASES.md):"
Write-Host "  $PubKeyPath"
Write-Host ""
Write-Host "Private key (NEVER commit; never share; back up to password manager):"
Write-Host "  $PrivKeyPath"
Write-Host ""

$pubKeyContents = Get-Content $PubKeyPath -Raw
Write-Host "Public key contents:"
Write-Host "----------"
Write-Host $pubKeyContents
Write-Host "----------"
Write-Host ""

Write-Host "Next steps (see docs/SIGNING.md for the full walkthrough):"
Write-Host ""
Write-Host "1. Paste the public key into docs/RELEASES.md under the 'Verifying"
Write-Host "   a release' section, replacing the <PUBLIC-KEY-PLACEHOLDER> marker."
Write-Host ""
Write-Host "2. Back up the private key + password to your password manager:"
Write-Host "   - The file $PrivKeyPath"
Write-Host "   - The password you just typed"
Write-Host ""
Write-Host "3. Configure GitHub Actions secrets at"
Write-Host "   https://github.com/Kitsune-Den/KitsuneCommand/settings/secrets/actions"
Write-Host "   - KC_MINISIGN_PRIVATE_KEY            -> contents of $PrivKeyPath"
Write-Host "     (paste the whole file contents, including the leading comment lines)"
Write-Host "   - KC_MINISIGN_PRIVATE_KEY_PASSWORD   -> the password you used"
Write-Host ""
Write-Host "4. Cut a fresh release (tag push) — the release workflow will pick up"
Write-Host "   the secrets and produce .sha256 + .minisig alongside the zip."
