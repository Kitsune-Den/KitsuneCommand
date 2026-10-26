#!/bin/bash
set -e

CONFIGURATION="${1:-Release}"
OUTPUT_DIR="${2:-./dist}"
MOD_NAME="KitsuneCommand"
MOD_DIR="$OUTPUT_DIR/$MOD_NAME"

echo "=== KitsuneCommand Build Script ==="

# Clean
if [ -d "$OUTPUT_DIR" ]; then
    rm -rf "$OUTPUT_DIR"
fi
mkdir -p "$MOD_DIR"

# Build frontend
echo ""
echo "--- Building Frontend ---"
pushd frontend > /dev/null
npm ci
npm run build
popd > /dev/null

# Build backend
echo ""
echo "--- Building Backend ---"
dotnet build "src/$MOD_NAME/$MOD_NAME.csproj" -c "$CONFIGURATION"

# Copy mod files
echo ""
echo "--- Packaging Mod ---"
BIN_DIR="src/$MOD_NAME/bin/$CONFIGURATION"
SRC_DIR="src/$MOD_NAME"
cp "$BIN_DIR"/*.dll "$MOD_DIR/"
# .dll.config files are static (Mono dllmap + .NET binding-redirects
# config); no build-time transformation. The csproj declares them as
# <None CopyToOutputDirectory> but that rule fires inconsistently on
# net48 SDK-style projects across .NET SDK versions. Copy from source
# directly so a fresh CI checkout (no cached bin/ output) succeeds the
# same way a local incremental build does.
cp "$SRC_DIR/$MOD_NAME.dll.config" "$MOD_DIR/"
cp "$SRC_DIR/System.Data.SQLite.dll.config" "$MOD_DIR/"
cp "$SRC_DIR/SkiaSharp.dll.config" "$MOD_DIR/"
cp "$SRC_DIR/ModInfo.xml" "$MOD_DIR/"

# Native libraries
# Ship the committed natives for BOTH platforms, the same set tools/build.ps1
# ships by default, so one zip runs on Windows and Linux. The mod folder is
# often built on Linux and dropped onto a Windows server (and vice versa);
# a dist missing libSkiaSharp.dll is what crashed MapTileRenderer on Windows.
#   libSkiaSharp.dll / libSkiaSharp.so: map renderer + item icons
#   sqlite3.dll: Windows only. On Linux, System.Data.SQLite is built with
#     SQLITE_STANDARD and Mono maps "sqlite3" to the system libsqlite3.so.0.
echo ""
echo "  [Native libraries]"
mkdir -p "$MOD_DIR/x64"
for native in libSkiaSharp.dll libSkiaSharp.so sqlite3.dll; do
    if [ ! -f "$SRC_DIR/x64/$native" ]; then
        echo "    ERROR: $SRC_DIR/x64/$native is missing. It's committed to the repo;"
        echo "           restore it (git checkout -- $SRC_DIR/x64) and rebuild."
        exit 1
    fi
    cp "$SRC_DIR/x64/$native" "$MOD_DIR/x64/"
    echo "    Copied $native to x64/"
done

# System.ComponentModel.DataAnnotations (required by System.Web.Http, not in Unity/Mono)
# Try Mono GAC first, then .NET reference assemblies
DATA_ANNOTATIONS=""
for path in \
    "/usr/lib/mono/4.5/System.ComponentModel.DataAnnotations.dll" \
    "/usr/lib/mono/gac/System.ComponentModel.DataAnnotations/4.0.0.0__31bf3856ad364e35/System.ComponentModel.DataAnnotations.dll" \
    "$BIN_DIR/System.ComponentModel.DataAnnotations.dll"; do
    if [ -f "$path" ]; then
        DATA_ANNOTATIONS="$path"
        break
    fi
done

if [ -n "$DATA_ANNOTATIONS" ]; then
    cp "$DATA_ANNOTATIONS" "$MOD_DIR/"
    echo "    Copied System.ComponentModel.DataAnnotations.dll"
else
    echo "    WARNING: System.ComponentModel.DataAnnotations.dll not found."
    echo "             Install mono-devel or copy from .NET Framework."
fi

# Copy config
if [ -d "src/$MOD_NAME/Config" ]; then
    cp -r "src/$MOD_NAME/Config" "$MOD_DIR/Config"
fi

# Copy wwwroot
if [ -d "src/$MOD_NAME/wwwroot" ]; then
    cp -r "src/$MOD_NAME/wwwroot" "$MOD_DIR/wwwroot"
fi

# Create Plugins directory
mkdir -p "$MOD_DIR/Plugins"

echo ""
echo "=== Build Complete ==="
echo "Mod packaged to: $MOD_DIR"
echo "Copy the '$MOD_DIR' folder to your 7D2D server's Mods/ directory."
