#!/bin/bash
set -e

CONFIGURATION="${1:-Release}"
OUTPUT_DIR="${2:-./dist}"
MOD_NAME="KitsuneCommand"
MOD_DIR="$OUTPUT_DIR/$MOD_NAME"
NUGET_CACHE="$HOME/.nuget/packages"

echo "=== KitsuneCommand Build Script (Linux) ==="

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
cp "$BIN_DIR"/*.dll "$MOD_DIR/"
cp "$BIN_DIR/$MOD_NAME.dll.config" "$MOD_DIR/"
cp "$BIN_DIR/System.Data.SQLite.dll.config" "$MOD_DIR/"
cp "$BIN_DIR/SkiaSharp.dll.config" "$MOD_DIR/"
cp "src/$MOD_NAME/ModInfo.xml" "$MOD_DIR/"

# Linux native libraries
echo ""
echo "  [Linux native libraries]"

# libSkiaSharp.so
SKIA_LINUX=""
for path in \
    "$NUGET_CACHE/skiasharp.nativeassets.linux.nodependencies/2.80.4/runtimes/linux-x64/native/libSkiaSharp.so" \
    "$NUGET_CACHE/skiasharp.nativeassets.linux.nodependencies/2.80.2/runtimes/linux-x64/native/libSkiaSharp.so"; do
    if [ -f "$path" ]; then
        SKIA_LINUX="$path"
        break
    fi
done

if [ -n "$SKIA_LINUX" ]; then
    mkdir -p "$MOD_DIR/linux-x64"
    cp "$SKIA_LINUX" "$MOD_DIR/linux-x64/"
    echo "    Copied libSkiaSharp.so to linux-x64/"
else
    echo "    WARNING: libSkiaSharp.so not found. Run 'dotnet restore' first."
fi

# SQLite: no native library needed for Linux!
# System.Data.SQLite is built with SQLITE_STANDARD, so it P/Invokes "sqlite3".
# Mono's global config maps sqlite3 -> libsqlite3.so.0 (system package).
echo "    SQLite: uses system libsqlite3 (no native library to ship)"

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
