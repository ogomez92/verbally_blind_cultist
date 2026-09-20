# Installer

`CultistSimulatorAccessibilityInstaller.exe` is the one-click installer attached to every GitHub release. It finds
the game through Steam (registry, then `libraryfolders.vdf`, with a Browse fallback for GOG and manual installs),
downloads the latest release zip, extracts it into the game folder, verifies every file landed, and uninstalls
cleanly. It is screen reader accessible, and `--cli` gives a terminal flow.

It is built from the shared installer codebase of the accessibility mods (`mods\installer`, one `Game` entry per
game in `src/games.rs`, `src/bin/cultist.rs` as the entry point). The exe is committed here so the release workflow
can attach it without a manual upload: rebuild it with `build_installer.bat` in that folder and copy the new
`CultistSimulatorAccessibilityInstaller.exe` over this one. The installer reads the release list at run time, so it
rarely needs rebuilding.
