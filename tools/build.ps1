param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "./dist"
)

$ErrorActionPreference = "Stop"
$modName = "KitsuneCommand"
$modDir = "$OutputDir/$modName"

Write-Host "=== KitsuneCommand Build Script ===" -ForegroundColor Cyan

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
Copy-Item "src/$modName/ModInfo.xml" $modDir

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
Write-Host "Copy the '$modDir' folder to your 7D2D server's Mods/ directory." -ForegroundColor Gray
