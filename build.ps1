# Builds Inventory Manager in Release mode and produces:
#   dist\InventoryManager\InventoryManager.exe   single file, everything inside (Windows 8 / 8.1 / 10 / 11)
#   dist\InventoryManager-Setup.exe              installer with the offline .NET Framework 4.8 (also Windows 7)
# Pass -NoSetup to skip the installer.
param([switch]$NoSetup)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$msbuild = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $msbuild) { throw 'MSBuild not found. Install Visual Studio or the Build Tools.' }

# Rebuild, so the DLLs and fonts embedded in the exe are always the current ones.
& $msbuild "$root\InventoryManager.sln" -restore -t:Rebuild -m -nologo -v:minimal -p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

$exe  = Join-Path $root 'src\InventoryManager\bin\Release\InventoryManager.exe'
$dist = Join-Path $root 'dist\InventoryManager'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item $exe $dist
Copy-Item "$root\README.md" $dist

Write-Host ""
Write-Host "App built -> $dist\InventoryManager.exe" -ForegroundColor Green

if (-not $NoSetup) {
    & "$root\installer\build-setup.ps1" -Exe $exe -OutFile (Join-Path $root 'dist\InventoryManager-Setup.exe')
}
