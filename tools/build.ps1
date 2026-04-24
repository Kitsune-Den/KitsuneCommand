param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "./dist",
    [ValidateSet("windows", "linux", "both")]
    [string]$Platform = "both"
)

$ErrorActionPreference = "Stop"
$modName = "KitsuneCommand"
$modDir = "$OutputDir/$modName"
$nugetCache = "$env:USERPROFILE/.nuget/packages"

Write-Host "=== KitsuneCommand Build Script ===" -ForegroundColor Cyan
Write-Host "  Platform: $Platform" -ForegroundColor Gray

# Clean
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $modDir | Out-Null

# Build frontend
Write-Host "`n--- Building Frontend ---" -ForegroundColor Yellow
Push-Location frontend
npm ci
npm run build
Pop-Location

# Build backend
Write-Host "`n--- Building Backend ---" -ForegroundColor Yellow
dotnet build "src/$modName/$modName.csproj" -c $Configuration

# Copy mod files
Write-Host "`n--- Packaging Mod ---" -ForegroundColor Yellow
$binDir = "src/$modName/bin/$Configuration"
Copy-Item "$binDir/*.dll" $modDir
Copy-Item "$binDir/$modName.dll.config" $modDir
Copy-Item "$binDir/System.Data.SQLite.dll.config" $modDir
Copy-Item "$binDir/SkiaSharp.dll.config" $modDir
Copy-Item "src/$modName/ModInfo.xml" $modDir

# --- Windows native libraries ---
if ($Platform -eq "windows" -or $Platform -eq "both") {
    Write-Host "`n  [Windows native libraries]" -ForegroundColor Cyan

    # sqlite3.dll (official SQLite native library)
    # System.Data.SQLite is built with SQLITE_STANDARD, so P/Invoke target is "sqlite3".
    # We ship the official sqlite3.dll (NOT SQLite.Interop.dll, which doesn't export standard functions).
    $sqliteSrc = "src/$modName/x64/sqlite3.dll"
    if (Test-Path $sqliteSrc) {
        $nativeDir = "$modDir/x64"
        New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
        Copy-Item $sqliteSrc $nativeDir
        Write-Host "    Copied sqlite3.dll (official) to x64/" -ForegroundColor Gray
    } else {
        Write-Warning "sqlite3.dll not found at $sqliteSrc. Download from https://sqlite.org/download.html"
    }

    # libSkiaSharp.dll (Windows)
    $skiaWinPaths = @(
        "$nugetCache/skiasharp.nativeassets.win32/3.116.1/runtimes/win-x64/native/libSkiaSharp.dll",
        "$nugetCache/skiasharp.nativeassets.win32/2.80.4/runtimes/win-x64/native/libSkiaSharp.dll",
        "$binDir/libSkiaSharp.dll"
    )
    $skiaWin = $skiaWinPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($skiaWin) {
        $nativeDir = "$modDir/x64"
        New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
        Copy-Item $skiaWin $nativeDir
        Write-Host "    Copied libSkiaSharp.dll to x64/" -ForegroundColor Gray
    } else {
        Write-Warning "libSkiaSharp.dll (Windows) not found"
    }

    # System.Runtime.InteropServices.RuntimeInformation is provided by RuntimeInfoShim.dll
    # (cross-platform, compiled from src/RuntimeInfoShim). Do NOT copy the Windows SDK version
    # — it's a Windows-only binary that crashes Mono on Linux and conflicts with our shim.
    Write-Host "    RuntimeInformation: provided by RuntimeInfoShim.dll (cross-platform)" -ForegroundColor Gray
}

# System.ComponentModel.DataAnnotations (required by System.Web.Http, not in Unity/Mono)
# This is a managed assembly needed on BOTH Windows and Linux.
$dataAnnotations = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.ComponentModel.DataAnnotations.dll'
if (Test-Path $dataAnnotations) {
    Copy-Item $dataAnnotations $modDir
    Write-Host "  Copied System.ComponentModel.DataAnnotations.dll (cross-platform)" -ForegroundColor Gray
} else {
    Write-Warning "System.ComponentModel.DataAnnotations.dll not found"
}

# --- Linux native libraries ---
if ($Platform -eq "linux" -or $Platform -eq "both") {
    Write-Host "`n  [Linux native libraries]" -ForegroundColor Cyan

    # libSkiaSharp.so (Linux, NoDependencies variant)
    $skiaLinuxPaths = @(
        "$nugetCache/skiasharp.nativeassets.linux.nodependencies/2.80.4/runtimes/linux-x64/native/libSkiaSharp.so",
        "$nugetCache/skiasharp.nativeassets.linux.nodependencies/2.80.2/runtimes/linux-x64/native/libSkiaSharp.so"
    )
    $skiaLinux = $skiaLinuxPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($skiaLinux) {
        # Ship libSkiaSharp.so in the same `x64/` folder as the Windows natives.
        # Different extensions, same folder. Matches SkiaSharp's default path lookup
        # on BOTH platforms so we don't have to fight its LibraryLoader.
        $nativeDir = "$modDir/x64"
        New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
        Copy-Item $skiaLinux $nativeDir
        Write-Host "    Copied libSkiaSharp.so to x64/" -ForegroundColor Gray
    } else {
        Write-Warning "libSkiaSharp.so (Linux) not found. Run 'dotnet restore' to download NuGet packages."
    }

    # SQLite: no native library needed for Linux!
    # System.Data.SQLite is built with SQLITE_STANDARD, so it P/Invokes "sqlite3".
    # Mono's global config maps sqlite3 -> libsqlite3.so.0 (system package).
    Write-Host "    SQLite: uses system libsqlite3 (no native library to ship)" -ForegroundColor Gray
}

# Copy config
if (Test-Path "src/$modName/Config") {
    Copy-Item -Recurse "src/$modName/Config" "$modDir/Config"
}

# Copy wwwroot
if (Test-Path "src/$modName/wwwroot") {
    Copy-Item -Recurse "src/$modName/wwwroot" "$modDir/wwwroot"
}

# Create Plugins directory
New-Item -ItemType Directory -Path "$modDir/Plugins" -Force | Out-Null

Write-Host "`n=== Build Complete ===" -ForegroundColor Green
Write-Host "Mod packaged to: $modDir" -ForegroundColor Green
Write-Host "Platform: $Platform" -ForegroundColor Gray
Write-Host "Copy the '$modDir' folder to your 7D2D server's Mods/ directory." -ForegroundColor Gray
