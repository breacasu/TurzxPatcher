# build.ps1 — Builds TurzxPatcher + TurzxSensorBridge, stages all artifacts
# into installer\build\, then compiles the Inno Setup installer.
#
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File installer\build.ps1
#
# Optional parameters:
#   -SensorBridgeRepo <path>   Path to the TurzxSensorBridge repo (default: ..\..\TurzxSensorBridge)
#   -SkipInnoSetup              Build + stage only, don't compile the installer

param(
    [string]$SensorBridgeRepo = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) "TurzxSensorBridge"),
    [switch]$SkipInnoSetup
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path $PSScriptRoot
$stagingDir = Join-Path $PSScriptRoot "build"

Write-Host "=== TurzxPatcher Installer Build ==="
Write-Host "  Patcher repo:     $repoRoot"
Write-Host "  SensorBridge repo: $SensorBridgeRepo"
Write-Host "  Staging dir:       $stagingDir"
Write-Host ""

if (-not (Test-Path $SensorBridgeRepo)) {
    Write-Error "TurzxSensorBridge repo not found at: $SensorBridgeRepo`nPass -SensorBridgeRepo <path>"
    exit 1
}

# ---- Clean staging dir ----
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# ---- Build TurzxPatcher ----
Write-Host "=== Building TurzxPatcher ==="
dotnet build (Join-Path $repoRoot "src\TurzxPatcher.csproj") -c Release
if ($LASTEXITCODE -ne 0) { Write-Error "TurzxPatcher build failed"; exit 1 }

# ---- Build TurzxSensorBridge ----
Write-Host "`n=== Building TurzxSensorBridge ==="
$sln = Join-Path $SensorBridgeRepo "TurzxSensorBridge.sln"
dotnet build $sln -c Release
if ($LASTEXITCODE -ne 0) { Write-Error "TurzxSensorBridge build failed"; exit 1 }

# ---- Stage TurzxPatcher.exe ----
$patcherOut = Join-Path $repoRoot "src\bin\Release\net48"
Write-Host "`n=== Staging TurzxPatcher ==="
Copy-Item (Join-Path $patcherOut "TurzxPatcher.exe") $stagingDir -Force
Copy-Item (Join-Path $patcherOut "TurzxPatcher.exe.config") $stagingDir -Force -ErrorAction SilentlyContinue

# ---- Stage PatchModule.dll ----
$patchModuleOut = Join-Path $SensorBridgeRepo "src\PatchModule\bin\Release\net48"
$patchesDir = Join-Path $stagingDir "patches"
New-Item -ItemType Directory -Path $patchesDir -Force | Out-Null
Write-Host "=== Staging PatchModule ==="
Copy-Item (Join-Path $patchModuleOut "PatchModule.dll") $patchesDir -Force

# ---- Stage SensorService ----
$sensorServiceOut = Join-Path $SensorBridgeRepo "src\SensorService\bin\Release\net48\win-x64"
$ssDest = Join-Path $patchesDir "SensorService"
New-Item -ItemType Directory -Path $ssDest -Force | Out-Null
Write-Host "=== Staging SensorService ==="
Copy-Item (Join-Path $sensorServiceOut "*") $ssDest -Recurse -Force
# Remove PDBs from release
Get-ChildItem $ssDest -Filter "*.pdb" -Recurse | Remove-Item -Force

# ---- Stage SensorConfig ----
$sensorConfigOut = Join-Path $SensorBridgeRepo "src\SensorConfig\bin\Release\net48\win-x64"
$scDest = Join-Path $stagingDir "SensorConfig"
New-Item -ItemType Directory -Path $scDest -Force | Out-Null
Write-Host "=== Staging SensorConfig ==="
Copy-Item (Join-Path $sensorConfigOut "*") $scDest -Recurse -Force
Get-ChildItem $scDest -Filter "*.pdb" -Recurse | Remove-Item -Force

# Remove patcher PDB
Remove-Item (Join-Path $stagingDir "*.pdb") -Force -ErrorAction SilentlyContinue

Write-Host "`n=== Staging complete ==="
Get-ChildItem $stagingDir -Recurse | ForEach-Object { Write-Host "  $($_.FullName.Replace($stagingDir, '.'))" }

# ---- Compile installer ----
if ($SkipInnoSetup) {
    Write-Host "`n-SkipInnoSetup: skipping Inno Setup compilation"
    return
}

$iscc = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) not found. Install it to compile the installer."
    Write-Warning "Staged files are in: $stagingDir"
    return
}

$issFile = Join-Path $PSScriptRoot "TurzxPatcher.iss"
Write-Host "`n=== Compiling installer ==="
Write-Host "  ISCC:  $iscc"
Write-Host "  ISS:   $issFile"
& $iscc $issFile
if ($LASTEXITCODE -ne 0) { Write-Error "Inno Setup compilation failed"; exit 1 }

Write-Host "`n=== Done! ==="
$outputDir = Join-Path $PSScriptRoot "output"
if (Test-Path $outputDir) {
    Get-ChildItem $outputDir -Filter "*.exe" | ForEach-Object { Write-Host "  Installer: $($_.FullName)" }
}
