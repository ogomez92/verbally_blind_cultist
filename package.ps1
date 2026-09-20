# Builds the release payload: BepInEx 5 (x86) + the plugin + the x64 speech host with Prism and its licences
# + README. No game code or assets are included.
# The payload is staged in release\ and committed: the plugin references the game's own DLLs, so the GitHub
# release workflow cannot build it and zips this folder instead. The local zip goes to dist\ (not committed).
param([string]$Version = "1.1.0")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
& "$root\build.ps1"

$stage = Join-Path $root "release"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

# 1. BepInEx 5 for 32-bit Windows games (the game is x86).
$bepinex = Get-ChildItem "$root\vendor" -Filter "BepInEx_win_x86_*.zip" | Sort-Object Name | Select-Object -Last 1
if (-not $bepinex) { throw "Put BepInEx_win_x86_5.4.x.zip into vendor\ (https://github.com/BepInEx/BepInEx/releases)" }
Expand-Archive -Path $bepinex.FullName -DestinationPath $stage
# BepInEx's own changelog is not ours to leave in the player's game folder.
Remove-Item (Join-Path $stage 'changelog.txt') -Force -ErrorAction SilentlyContinue

# 2. The plugin and the speech host, exactly as deployed by build.ps1.
$gamePath = if ($env:GAME_PATH) { $env:GAME_PATH } else { "S:\steam\steamapps\common\Cultist Simulator" }
$deployed = Join-Path $gamePath "BepInEx\plugins\CultistAccessibility"
$pluginDest = Join-Path $stage "BepInEx\plugins\CultistAccessibility"
New-Item -ItemType Directory -Force $pluginDest | Out-Null
Copy-Item "$deployed\CultistAccessibility.dll" $pluginDest
Copy-Item "$deployed\SpeechHost" $pluginDest -Recurse
Get-ChildItem $pluginDest -Recurse -Filter "*.pdb" | Remove-Item -Force

# 3. Documentation, and the built-in translations as a reference for corrections (see README, Languages).
#    The release workflow adds readme.html, generated from README.md with pandoc.
Copy-Item "$root\README.md" $stage
Copy-Item "$root\CultistAccessibility\Lang" (Join-Path $stage "translations") -Recurse

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "CultistAccessibility-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Output "Created $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { $_.FullName.Substring($stage.Length + 1) }
