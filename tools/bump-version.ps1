<#
.SYNOPSIS
    Bump KitsuneCommand's version everywhere it lives, and roll the CHANGELOG.

.DESCRIPTION
    Step 1 + 2 of docs/RELEASES.md in one shot:

      src/KitsuneCommand/ModInfo.xml   <Version value="X.Y.Z" />  (what 7DTD shows)
      frontend/package.json            "version"
      frontend/package-lock.json       "version" + packages[""].version
      CHANGELOG.md                     [Unreleased] entries move under a new
                                       "## [X.Y.Z] - YYYY-MM-DD" heading with the
                                       "> [Full notes](...)" line, and the compare
                                       links at the bottom get updated

    It refuses to run when the three version files disagree, when the new
    version isn't higher than the current one, when the tag already exists,
    or when [Unreleased] is empty (unless -AllowEmpty). It doesn't commit, tag
    or push: review the diff, write a short summary under "> [Full notes]",
    then follow docs/RELEASES.md from step 3.

    The package files are bumped through `npm version --no-git-tag-version`,
    so npm must be on PATH (it already is wherever the frontend builds).

    Keep this file pure ASCII: Windows PowerShell 5.1 reads a script without
    a BOM as ANSI, so any other character in a string comes out garbled.

.PARAMETER Version
    The new version: X.Y.Z, or X.Y.Z-rc.N / X.Y.Z-beta.N for a prerelease.

.PARAMETER Date
    The release date for the CHANGELOG heading (YYYY-MM-DD). Default: today.

.PARAMETER DryRun
    Show what would change without writing anything.

.PARAMETER AllowEmpty
    Cut the version even though [Unreleased] is empty, e.g. a final release
    after a release candidate whose entries are already under the rc heading.

.PARAMETER Check
    Only check that ModInfo.xml, package.json and package-lock.json agree.
    Exits 1 when they don't. Takes no version.

.EXAMPLE
    tools/bump-version.ps1 2.10.1
.EXAMPLE
    tools/bump-version.ps1 2.11.0-rc.1 -DryRun
.EXAMPLE
    tools/bump-version.ps1 -Check
#>

[CmdletBinding(DefaultParameterSetName = 'Bump')]
param(
    [Parameter(ParameterSetName = 'Bump', Mandatory = $true, Position = 0)]
    [string]$Version,

    [Parameter(ParameterSetName = 'Bump')]
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),

    [Parameter(ParameterSetName = 'Bump')]
    [switch]$DryRun,

    [Parameter(ParameterSetName = 'Bump')]
    [switch]$AllowEmpty,

    [Parameter(ParameterSetName = 'Check', Mandatory = $true)]
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$ModInfo = Join-Path $Root 'src/KitsuneCommand/ModInfo.xml'
$PackageJson = Join-Path $Root 'frontend/package.json'
$PackageLock = Join-Path $Root 'frontend/package-lock.json'
$Changelog = Join-Path $Root 'CHANGELOG.md'
$RepoUrl = 'https://github.com/Kitsune-Den/KitsuneCommand'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Fail([string]$Message) {
    Write-Host "bump-version: $Message" -ForegroundColor Red
    exit 1
}

function Read-Text([string]$Path) { [System.IO.File]::ReadAllText($Path, $Utf8NoBom) }
function Write-Text([string]$Path, [string]$Text) { [System.IO.File]::WriteAllText($Path, $Text, $Utf8NoBom) }

function Get-Versions {
    $mod = [regex]::Match((Read-Text $ModInfo), '<Version\s+value="([^"]+)"')
    if (-not $mod.Success) { Fail "no <Version value=`"...`" /> in $ModInfo" }
    # Node reads the JSON: Windows PowerShell's ConvertFrom-Json can't take the
    # lockfile's empty "" key under "packages".
    $read = "const p=require(process.argv[1]),l=require(process.argv[2]);" +
        "console.log([p.version,l.version,((l.packages||{})[''])?.version].join('|'))"
    $out = node -e $read $PackageJson $PackageLock
    if ($LASTEXITCODE -ne 0) { Fail 'node could not read the frontend package files.' }
    $pkg, $lock, $lockRoot = "$out".Trim().Split('|')
    [ordered]@{
        'src/KitsuneCommand/ModInfo.xml'          = $mod.Groups[1].Value
        'frontend/package.json'                   = $pkg
        'frontend/package-lock.json'              = $lock
        'frontend/package-lock.json packages[""]' = $lockRoot
    }
}

# X.Y.Z, X.Y.Z-rc.N, X.Y.Z-beta.N -> sortable key. A prerelease sorts below its release.
function Get-VersionKey([string]$V) {
    $m = [regex]::Match($V, '^(\d+)\.(\d+)\.(\d+)(?:-(beta|rc)\.(\d+))?$')
    if (-not $m.Success) { return $null }
    $stage = switch ($m.Groups[4].Value) { 'beta' { 0 } 'rc' { 1 } default { 2 } }
    $n = if ($m.Groups[5].Success) { [int]$m.Groups[5].Value } else { 0 }
    '{0:D6}.{1:D6}.{2:D6}.{3}.{4:D6}' -f [int]$m.Groups[1].Value, [int]$m.Groups[2].Value, [int]$m.Groups[3].Value, $stage, $n
}

# --- versions must agree before anything else -------------------------------

$versions = Get-Versions
$distinct = @($versions.Values | Select-Object -Unique)
if ($distinct.Count -ne 1) {
    Write-Host 'Version files disagree:' -ForegroundColor Red
    $versions.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-45} {1}" -f $_.Key, $_.Value) }
    Fail 'make them match by hand, then re-run.'
}
$Current = $distinct[0]

if ($Check) {
    Write-Host "Version files agree: $Current" -ForegroundColor Green
    exit 0
}

# --- validate the new version ------------------------------------------------

if (-not (Get-VersionKey $Version)) { Fail "'$Version' isn't X.Y.Z, X.Y.Z-rc.N or X.Y.Z-beta.N." }
if (-not (Get-VersionKey $Current)) { Fail "current version '$Current' isn't X.Y.Z, X.Y.Z-rc.N or X.Y.Z-beta.N." }
if ((Get-VersionKey $Version) -le (Get-VersionKey $Current)) { Fail "$Version isn't higher than the current $Current." }
if ($Date -notmatch '^\d{4}-\d{2}-\d{2}$') { Fail "-Date '$Date' isn't YYYY-MM-DD." }
if (git -C $Root tag --list "v$Version") { Fail "tag v$Version already exists." }

# --- CHANGELOG ---------------------------------------------------------------

$log = Read-Text $Changelog
$nl = if ($log.Contains("`r`n")) { "`r`n" } else { "`n" }

$heading = [regex]::Match($log, "(?m)^## \[Unreleased\][ \t]*(?=\r?$)")
if (-not $heading.Success) { Fail 'no "## [Unreleased]" heading in CHANGELOG.md.' }
$next = [regex]::Match($log.Substring($heading.Index + $heading.Length), '(?m)^## \[')
$bodyEnd = if ($next.Success) { $heading.Index + $heading.Length + $next.Index } else { $log.Length }
$unreleased = $log.Substring($heading.Index + $heading.Length, $bodyEnd - $heading.Index - $heading.Length)
if (-not $unreleased.Trim() -and -not $AllowEmpty) {
    Fail '[Unreleased] in CHANGELOG.md is empty: nothing to release. (-AllowEmpty cuts it anyway, e.g. a final release after an rc.)'
}

$link = [regex]::Match($log, "(?m)^\[Unreleased\]: \S+/compare/v(\S+)\.\.\.HEAD[ \t]*(?=\r?$)")
if (-not $link.Success) { Fail 'no "[Unreleased]: .../compare/vX.Y.Z...HEAD" link at the bottom of CHANGELOG.md.' }
$Previous = $link.Groups[1].Value

$newSection = "## [Unreleased]$nl$nl## [$Version] - $Date$nl$nl> [Full notes]($RepoUrl/releases/tag/v$Version)$nl"
$newLinks = "[Unreleased]: $RepoUrl/compare/v$Version...HEAD$nl[$Version]: $RepoUrl/compare/v$Previous...v$Version"

# Replace the link first (it's after the heading, so the heading's index stays valid).
$updated = $log.Remove($link.Index, $link.Length).Insert($link.Index, $newLinks)
$updated = $updated.Remove($heading.Index, $heading.Length + ($unreleased.Length - $unreleased.TrimStart("`r", "`n").Length)).
    Insert($heading.Index, $newSection + $nl)

# --- report / write ------------------------------------------------------------

Write-Host "KitsuneCommand $Current -> $Version  (CHANGELOG: [$Version] - $Date, compare v$Previous...v$Version)"
$versions.Keys | ForEach-Object { Write-Host "  $_" }
Write-Host '  CHANGELOG.md'

if ($DryRun) {
    Write-Host ''
    Write-Host 'Dry run: nothing written. The CHANGELOG would start:' -ForegroundColor Yellow
    ($updated.Substring($heading.Index, [Math]::Min(400, $updated.Length - $heading.Index)) -split "\r?\n") |
        Select-Object -First 8 | ForEach-Object { Write-Host "    $_" }
    exit 0
}

$mod = Read-Text $ModInfo
Write-Text $ModInfo ([regex]::Replace($mod, '(<Version\s+value=")[^"]+(")', "`${1}$Version`${2}", 'None', [TimeSpan]::FromSeconds(1)))

Push-Location (Join-Path $Root 'frontend')
try {
    npm version $Version --no-git-tag-version --allow-same-version | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail 'npm version failed.' }
}
finally { Pop-Location }

Write-Text $Changelog $updated

$after = @((Get-Versions).Values | Select-Object -Unique)
if ($after.Count -ne 1 -or $after[0] -ne $Version) { Fail "after the bump the version files read: $($after -join ', ')" }

Write-Host ''
Write-Host "Bumped to $Version." -ForegroundColor Green
Write-Host 'Next:'
Write-Host "  1. Under '> [Full notes]' in CHANGELOG.md, add a short '> ' summary for the release page (see earlier releases)."
Write-Host "  2. Review: git diff"
Write-Host "  3. Commit on release/v$Version, open the release PR, then tag v$Version after it merges (docs/RELEASES.md step 3)."
