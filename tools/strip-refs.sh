#!/usr/bin/env bash
#
# Regenerate src/KitsuneCommand/refs/ from your local 7DTD install.
#
# Background: KC's csproj references six 7DTD DLLs (Assembly-CSharp,
# Unity*, 0Harmony, LogLibrary). The real DLLs are proprietary game
# assets — we can't commit them to the repo. But we still need
# *something* committed so CI and contributors can compile the mod
# without owning a 7DTD copy.
#
# Solution: stripped reference assemblies. JetBrains Refasmer takes
# a real DLL and produces a same-public-API-but-no-IL-bodies version.
# Tiny, legal, compile-compatible. The .dll files in src/KitsuneCommand/refs/
# are what's committed; this script regenerates them from the real
# DLLs sitting in src/KitsuneCommand/7dtd-binaries/ (gitignored).
#
# Run this whenever:
#   - 7DTD ships a new minor/major version (some game types change)
#   - You add a new reference assembly to the csproj
#   - You upgrade your local 7DTD install
#
# Prereqs:
#   1. dotnet SDK (any version >= 6)
#   2. Refasmer:  `dotnet tool install --global JetBrains.Refasmer.CliTool`
#   3. src/KitsuneCommand/7dtd-binaries/ populated with the real DLLs from your
#      <7DTD-server>/7DaysToDieServer_Data/Managed/ directory

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC_DIR="$ROOT/src/KitsuneCommand/7dtd-binaries"
OUT_DIR="$ROOT/src/KitsuneCommand/refs"

REFS=(
    Assembly-CSharp.dll
    Assembly-CSharp-firstpass.dll
    LogLibrary.dll
    UnityEngine.dll
    UnityEngine.CoreModule.dll
    0Harmony.dll
)

if ! command -v refasmer >/dev/null 2>&1; then
    echo "refasmer not in PATH." >&2
    echo "Install: dotnet tool install --global JetBrains.Refasmer.CliTool" >&2
    exit 1
fi

if [ ! -d "$SRC_DIR" ]; then
    echo "Source DLLs not found at $SRC_DIR" >&2
    echo "Copy them from <7DTD-server>/7DaysToDieServer_Data/Managed/" >&2
    exit 1
fi

MISSING=()
for f in "${REFS[@]}"; do
    [ ! -f "$SRC_DIR/$f" ] && MISSING+=("$f")
done
if [ ${#MISSING[@]} -gt 0 ]; then
    echo "Missing source DLLs in $SRC_DIR:" >&2
    printf '  %s\n' "${MISSING[@]}" >&2
    exit 1
fi

mkdir -p "$OUT_DIR"

echo "=== Stripping ${#REFS[@]} DLLs with Refasmer ==="
INPUTS=()
for f in "${REFS[@]}"; do
    INPUTS+=("$SRC_DIR/$f")
done

refasmer --omit-non-api-members=true -O "$OUT_DIR" "${INPUTS[@]}"

echo ""
echo "=== Size summary ==="
printf "  %-30s  %10s  %10s  %s\n" "name" "original" "stripped" "ratio"
for f in "${REFS[@]}"; do
    orig=$(stat -c %s "$SRC_DIR/$f" 2>/dev/null || stat -f %z "$SRC_DIR/$f")
    strip=$(stat -c %s "$OUT_DIR/$f" 2>/dev/null || stat -f %z "$OUT_DIR/$f")
    # Compute ratio without bc — use awk which is universally available.
    ratio=$(awk "BEGIN { printf \"%.1f%%\", ($strip * 100.0) / $orig }")
    printf "  %-30s  %10d  %10d  %s\n" "$f" "$orig" "$strip" "$ratio"
done

echo ""
echo "Wrote ${#REFS[@]} stripped DLLs to $OUT_DIR"
echo "Review the diff (git status / git diff) and commit if it looks right."
