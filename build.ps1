# Builds the plugin and the x64 speech host, and deploys both into the game's BepInEx\plugins folder.
# The game locks the deployed files while it runs, so it is closed first.
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
Stop-Process -Name cultistsimulator -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
dotnet build "$PSScriptRoot\CultistAccessibility\CultistAccessibility.csproj" -c $Configuration --no-incremental -nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
$gamePath = if ($env:GAME_PATH) { $env:GAME_PATH } else { "S:\steam\steamapps\common\Cultist Simulator" }
$deployed = Join-Path $gamePath "BepInEx\plugins\CultistAccessibility\CultistAccessibility.dll"
$built = Join-Path $PSScriptRoot "CultistAccessibility\bin\$Configuration\CultistAccessibility.dll"
if ((Get-Item $deployed).LastWriteTime -ne (Get-Item $built).LastWriteTime) { throw "Deployment is stale: $deployed" }
Write-Output ("Deployed " + (Get-Item $deployed).LastWriteTime)
