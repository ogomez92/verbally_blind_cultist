# Builds a release zip: BepInEx 5 (x86) + the plugin + the x64 speech host with Prism and its licences + README.
# No game code or assets are included.
param([string]$Version = "1.1.0")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
& "$root\build.ps1"

$stage = Join-Path $root "release\CultistAccessibility-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

# 1. BepInEx 5 for 32-bit Windows games (the game is x86).
$bepinex = Get-ChildItem "$root\vendor" -Filter "BepInEx_win_x86_*.zip" | Sort-Object Name | Select-Object -Last 1
if (-not $bepinex) { throw "Put BepInEx_win_x86_5.4.x.zip into vendor\ (https://github.com/BepInEx/BepInEx/releases)" }
Expand-Archive -Path $bepinex.FullName -DestinationPath $stage

# 2. The plugin and the speech host, exactly as deployed by build.ps1.
$gamePath = if ($env:GAME_PATH) { $env:GAME_PATH } else { "S:\steam\steamapps\common\Cultist Simulator" }
$deployed = Join-Path $gamePath "BepInEx\plugins\CultistAccessibility"
$pluginDest = Join-Path $stage "BepInEx\plugins\CultistAccessibility"
New-Item -ItemType Directory -Force $pluginDest | Out-Null
Copy-Item "$deployed\CultistAccessibility.dll" $pluginDest
Copy-Item "$deployed\SpeechHost" $pluginDest -Recurse
Get-ChildItem $pluginDest -Recurse -Filter "*.pdb" | Remove-Item -Force

# 3. Documentation, and the built-in translations as a reference for corrections (see README, Languages).
Copy-Item "$root\README.md" $stage
Copy-Item "$root\CultistAccessibility\Lang" (Join-Path $stage "translations") -Recurse

$zip = Join-Path $root "release\CultistAccessibility-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Output "Created $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { $_.FullName.Substring($stage.Length + 1) }
