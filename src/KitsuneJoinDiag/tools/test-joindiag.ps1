# KitsuneJoinDiag -- diagnostic block extractor.
#
# Hunts the latest `[KitsuneJoinDiag] CONNECTION FAILED` block out of a
# ModLauncher profile's output_log.txt and prints it. Optionally tails
# the log live until a fresh block appears, so you can fire a failed
# connect attempt via the normal UI and have the answer waiting for you.
#
# Why not "launch the game with bad target + scrape" fully-automated?
# Because 7DTD 2.6's -connecttoip command line arg is parsed and then
# WARN'd as "not a configfile property, ignoring." The game never
# auto-connects from it -- the documented behavior is misleading. So we
# split the work: the human (or computer-use) drives the UI, the script
# extracts the result.
#
# Usage:
#   # Print the most recent diag block in the current log (one-shot):
#   .\test-joindiag.ps1
#
#   # Tail the log until a NEW diag block appears, then print:
#   .\test-joindiag.ps1 -Watch
#
#   # Tail and print, but also kill the game once we have the block:
#   .\test-joindiag.ps1 -Watch -StopGameOnHit
#
#   # Pick a different profile:
#   .\test-joindiag.ps1 -Profile Kitsune_Den -Watch
#
# Exit codes:
#   0 - a block was found and printed
#   1 - no block found (one-shot mode) / timed out (watch mode)
#   2 - bad args, missing files

[CmdletBinding()]
param(
    # ModLauncher profile name under G:\7D2D\Custom\.
    [string]$Profile = 'TestingDen',

    # If set, tail the log waiting for a NEW diag block (one written
    # AFTER the script starts). Without this flag, prints the most
    # recent block in the existing log.
    [switch]$Watch,

    # Max seconds to wait in -Watch mode.
    [int]$TimeoutSec = 180,

    # In -Watch mode, kill 7DTD after capturing a block. Saves the manual
    # alt-F4 between iterations.
    [switch]$StopGameOnHit,

    # Root for ModLauncher's per-profile UserDataFolders.
    [string]$ProfileRoot = 'G:\7D2D\Custom'
)

$ErrorActionPreference = 'Stop'

$logPath = Join-Path (Join-Path $ProfileRoot $Profile) 'output_log.txt'
if (-not (Test-Path $logPath)) {
    Write-Error "Log not found: $logPath  (profile '$Profile' may not have been launched yet)"
    exit 2
}

# Pattern: two 64-equals rules with the diag header and body between.
# `(?s)` for dotall so the body spans multiple lines.
$pattern = '(?s)={64}\s*\r?\n\[KitsuneJoinDiag\] CONNECTION FAILED[\s\S]*?={64}'

function Get-AllBlocks {
    param([string]$Path)
    # Read whole file (output_log.txt is rewritten each launch, typically
    # tens of KB to a few MB -- fits comfortably in memory).
    $text = [System.IO.File]::ReadAllText($Path)
    [regex]::Matches($text, $pattern) | ForEach-Object { $_.Value }
}

# --- One-shot mode: print the most recent existing block ---
if (-not $Watch) {
    $blocks = @(Get-AllBlocks -Path $logPath)
    if ($blocks.Count -eq 0) {
        Write-Host "[test-joindiag] no diag block found in $logPath" -ForegroundColor Yellow
        Write-Host "  (mod may not have caught a failure yet -- trigger a failed connect attempt and retry)"
        exit 1
    }
    Write-Host "[test-joindiag] $($blocks.Count) block(s) in log. Printing most recent:" -ForegroundColor Green
    Write-Host ""
    Write-Host $blocks[-1]
    exit 0
}

# --- Watch mode: wait for a NEW block written after script start ---
Write-Host "[test-joindiag] watching $logPath for new diag blocks (timeout ${TimeoutSec}s)..." -ForegroundColor Cyan
Write-Host "  Trigger a failed connect attempt via the normal Direct Connect flow." -ForegroundColor Cyan
Write-Host ""

$baselineBlocks = @(Get-AllBlocks -Path $logPath)
$baselineCount = $baselineBlocks.Count
Write-Host "[test-joindiag] baseline: $baselineCount existing block(s)" -ForegroundColor DarkGray

$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    if (-not (Test-Path $logPath)) {
        # Log rotated/deleted (e.g. game launched fresh) -- reset baseline.
        Write-Host "[test-joindiag] log gone, waiting for it to be recreated..." -ForegroundColor DarkGray
        $baselineCount = 0
        continue
    }
    $blocks = @(Get-AllBlocks -Path $logPath)
    if ($blocks.Count -gt $baselineCount) {
        Write-Host ""
        Write-Host "[test-joindiag] NEW BLOCK CAPTURED:" -ForegroundColor Green
        Write-Host ""
        Write-Host $blocks[-1]

        if ($StopGameOnHit) {
            $game = Get-Process -Name '7DaysToDie*' -ErrorAction SilentlyContinue
            if ($game) {
                Write-Host ""
                Write-Host "[test-joindiag] stopping 7DTD (PID $($game.Id))..." -ForegroundColor Cyan
                Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
            }
        }
        exit 0
    }
    # Log being smaller than last poll means the game restarted -- reset
    # our baseline to the new (smaller) count.
    if ($blocks.Count -lt $baselineCount) {
        Write-Host "[test-joindiag] log shrank (game restarted?). Resetting baseline." -ForegroundColor DarkGray
        $baselineCount = $blocks.Count
    }
}

Write-Host ""
Write-Host "[test-joindiag] timed out after ${TimeoutSec}s with no new block." -ForegroundColor Red
exit 1
