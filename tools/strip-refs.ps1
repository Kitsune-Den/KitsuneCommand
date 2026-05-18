<#
.SYNOPSIS
    Regenerate src/KitsuneCommand/refs/ from your local 7DTD install.

.DESCRIPTION
    KC's csproj references six 7DTD DLLs (Assembly-CSharp, Unity*,
    0Harmony, LogLibrary). The real DLLs are proprietary game assets —
    we can't commit them to the repo. But we still need *something*
    committed so CI and contributors can compile the mod without
    owning a 7DTD copy.

    Solution: stripped reference assemblies. JetBrains Refasmer takes
    a real DLL and produces a same-public-API-but-no-IL-bodies version.
    Tiny, legal, compile-compatible. The .dll files in
    src/KitsuneCommand/refs/ are what's committed; this script regenerates
    them from the real DLLs sitting in src/KitsuneCommand/7dtd-binaries/
    (gitignored).

    Run this whenever:
      - 7DTD ships a new minor/major version (some game types change)
      - You add a new reference assembly to the csproj
      - You upgrade your local 7DTD install

.NOTES
    Prereqs:
      1. .NET SDK (any version >= 6)
      2. Refasmer: dotnet tool install --global JetBrains.Refasmer.CliTool
      3. src/KitsuneCommand/7dtd-binaries/ populated with the real DLLs from your
         <7DTD-server>/7DaysToDieServer_Data/Managed/ directory
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$root   = (Resolve-Path "$PSScriptRoot\..").Path
$srcDir = Join-Path $root "src\KitsuneCommand\7dtd-binaries"
$outDir = Join-Path $root "src\KitsuneCommand\refs"

$refs = @(
    "Assembly-CSharp.dll",
    "Assembly-CSharp-firstpass.dll",
    "LogLibrary.dll",
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "0Harmony.dll"
)

if (-not (Get-Command refasmer -ErrorAction SilentlyContinue)) {
    Write-Error @"
refasmer not in PATH.
Install: dotnet tool install --global JetBrains.Refasmer.CliTool
"@
}

if (-not (Test-Path $srcDir)) {
    Write-Error @"
Source DLLs not found at $srcDir
Copy them from <7DTD-server>\7DaysToDieServer_Data\Managed\
"@
}

$missing = @()
foreach ($r in $refs) {
    $p = Join-Path $srcDir $r
    if (-not (Test-Path $p)) { $missing += $r }
}
if ($missing.Count -gt 0) {
    Write-Error "Missing source DLLs in $srcDir`n  $($missing -join "`n  ")"
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Write-Host "=== Stripping $($refs.Count) DLLs with Refasmer ===" -ForegroundColor Cyan
$inputs = $refs | ForEach-Object { Join-Path $srcDir $_ }
& refasmer --omit-non-api-members=true -O $outDir @inputs

if ($LASTEXITCODE -ne 0) {
    Write-Error "refasmer exited $LASTEXITCODE"
}

Write-Host ""
Write-Host "=== Size summary ===" -ForegroundColor Cyan
"  {0,-30}  {1,10}  {2,10}  {3}" -f "name", "original", "stripped", "ratio" | Write-Host
foreach ($r in $refs) {
    $orig  = (Get-Item (Join-Path $srcDir $r)).Length
    $strip = (Get-Item (Join-Path $outDir $r)).Length
    $ratio = "{0:F1}%" -f ($strip * 100.0 / $orig)
    "  {0,-30}  {1,10}  {2,10}  {3}" -f $r, $orig, $strip, $ratio | Write-Host
}

Write-Host ""
Write-Host "Wrote $($refs.Count) stripped DLLs to $outDir" -ForegroundColor Green
Write-Host "Review the diff (git status / git diff) and commit if it looks right."
