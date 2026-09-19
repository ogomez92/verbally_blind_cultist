# Cultist Simulator Accessibility — developer notes

Screen reader mod for Cultist Simulator (Weather Factory), built from `MOD_TEMPLATE.md`.
Player-facing documentation: `README.md`. Screen inventory: `SCREENS.md`. Patch targets: `PATCH_TARGETS.md`.

## Reconnaissance (verified 2026-09-19, game version 2026.1.g.2)

| Question | Answer |
|---|---|
| Backend | Mono (`cultistsimulator_Data\Managed\Assembly-CSharp.dll`, most code in `SecretHistories.Main.dll`) |
| Bitness | **32-bit (x86)** — `cultistsimulator.exe` and `UnityPlayer.dll` are PE32 i386 |
| Unity | 2022.3.62f3 |
| Mod loader | BepInEx 5.4.23.5 **x86** (installed into the game folder). The game's own DLL mod loading (`/mods/<mod>/dll`, see `StreamingAssets/MODDING_README.txt`) was rejected: it needs the mod enabled in `mods.txt` via the in-game mods screen, and the game's safe mode disables all mods after a crash. |
| UI | uGUI + TextMeshPro. The table (cards, verbs, verb windows, Mansus) is a **world-space** canvas (`MasterCanvas`); menus, status bar, options and notifications are screen-space. |
| Input | New Input System. `PlayerInput` (S0Master) with action map `Keybindings` (see below). The game's key handlers are `UIController.Input_*` (tabletop) and `MenuScreenController.OnAbort`, wired through `KeyboundGameEvent` UnityEvents. The EventSystem uses `InputSystemUIInputModule`. |
| Navigation tier | **Tier C** everywhere: no keyboard focus in menus, the table is mouse-only (click, drag and drop, double-click). The mod owns navigation. |
| Mouse-only gates | Drag and drop of cards into verb slots; clicks to open verbs, flip face-down cards, dismiss notifications; hover-only details windows. Splash and quote screens accept any key. No polled "press Select" waits were found. |
| Localisation | `Watchman.Get<ILocStringProvider>().Get("UI_...")` (LanguageManager); content labels come localised on the entities (Element.Label, Recipe.Label...). |
| Narrative | Recipes' StartLabel/StartDescription/Label/Description, delivered as "notes" (NotesSphere) inside the verb window. |

### Speech: why there is a separate process
Prism ships Windows binaries for x64 and arm64 only, and the game is x86, so `prism.dll` cannot be loaded in
process. `SpeechHost/` is a tiny x64 .NET Framework 4.8 console exe (the framework is part of every Windows 10/11)
that owns the Prism context. Config `SpeechBackend` (Auto, or a Prism backend name such as NVDA or SAPI) is passed
to it as `--backend`. The plugin starts it (`Core/Output/SpeechHostClient.cs`) and writes one UTF-8 command per line to its
stdin (`O<0|1>text`, `S..`, `B..`, `X`, `R`, `Q`). The host exits on stdin EOF, `Q`, or when the game process dies
(`--parent <pid>`). It registers Prism's availability callback so it switches to NVDA/JAWS when the screen reader
starts after the game. Only `Core/Speech.cs` talks to the client.

### Game key bindings (Keybindings action map, keyboard group)
Space pause (kbpause), N normal speed, M fast, S start recipe in the open verb window, C collect all in the open
window, Tab stack cards (kbstack), 1/2/3 zoom levels, Q/E zoom in/out, arrows camera pan (kbtruckleft/right,
kbpedestalup/down), Escape close window / open options (kbabort). Mouse wheel zooms.

### Mod keys
Arrows, Enter, Home/End, Page Up/Down, Delete/Backspace (navigation, owned by the mod); F1 help; F2 verbosity;
R repeat; I inspect; A read screen/group; H status; T timers; G next completed verb; Ctrl+arrows review buffers;
Ctrl+Shift+D debug dump (DebugLogging). Developer test keys (config DeveloperTestKeys, default false):
Ctrl+Shift+F5 Contentment card, F6 Despair countdown, F7 save now, F9 Mansus portal, F10 basic cards, F11 ending
(unlocks a real achievement, see Pitfalls); any screen: Ctrl+Shift+F4 game notification, F3 menu save messages
(cycles the four combinations the game shows) or the table's save error window, F8 (menu) the error scene S7UhO.
Letters were chosen to avoid N M S C Q E and digits. The arrow keys'
camera handlers are suppressed; everything else of the game is suppressed only while a mod modal is open.

## Architecture

```
Plugin.cs                    BepInEx entry: config, speech, buffers, patches, controller object, help contexts
Core/
  AccessibilityController    per-frame router (help modal > buffers/global keys > screen navigator)
  ScreenTracker              current screen from loaded scene names (S1Logo..S7UhO), never from patches
  ScreenAnnouncer            one-off screen texts (quote, ending)
  Speech + Output/SpeechHostClient   speech, event log file (CultistAccessibility_events.log next to the dll)
  Buffers/                   AnnouncementBuffer, BufferManager (Events, Details, Story, Verbs, Table, Status)
  KeyInput, InputGate        keyboard polling (unscaled time), which game key handlers may run
  ModConfig, Strings, TextCleaner
Navigation/                  generic uGUI navigator (menus, overlays, options, legacy choice, endings)
  UiNavigator                items = Selectables a mouse could click (raycast test) + text rows of the same panels
  UiReader                   labels/roles/states, game-specific readers (settings, legacy entries, mods, tabs)
  UiActions                  hover / click emulation, uGUI selection kept empty
  ScreenHints                per-screen default focus (Continue / Begin Game); screens whose message is read whole
  MenuMessages               speaks the menu's save / safe-mode messages (HintsHolder) when they appear
Tabletop/
  TabletopNavigator          Board (Verbs / Cards / Controls), verb Window, Mansus, modal Pickers
  Describer                  spoken summaries and detail lines for cards, verbs, slots, aspects, deck draws
  GameActions                all state changes, through the game's own code paths
  GameAccess                 read-only game state helpers
  TabletopEvents             4 Hz poll: verb start/continue/complete/appear/vanish, danger countdowns
  StatusReader               H and T readouts, refreshers for the Verbs/Table/Status buffers
  Picker                     modal list (card for a slot, verb for a card, portal contents)
  DeveloperTools             test-only keys (config DeveloperTestKeys, default false)
Patches/                     manual Harmony patches, see PATCH_TARGETS.md
Help/                        HelpSystem (F1 list) and one context per screen/mode
SpeechHost/                  x64 Prism host process
```

### Key design decisions
- **Mode from state, not keys.** The tabletop mode is recomputed every frame: Mansus if `Numa.IsOtherworldActive()`,
  Window if a `Situation.IsOpen`, else Board. Game-driven changes (placing a card opens the window, the Mansus opening
  by itself) are therefore always followed.
- **Placing a card = the game's drop path.** `GameActions.PlaceInSlot`: validate with the slot's own
  `GetMatchForTokenPayload` (speaks `ContainerMatchForStack.GetProblemDescription` on refusal), calve the stack to one
  card, `RequestHomeLocationFromCurrentSphere`, then `ThresholdSphere.TryAcceptToken(card, PlayerDrag)` — exactly
  what `ThresholdSphere.TryMoveAsideAndAcceptToken` does for a mouse drop (incumbent goes home, sound, state).
- **Removing a card** = `Token.GoAway(PlayerDrag)` (returns to its remembered table position).
- **Results arrive face down** (`OutputSphere.AlwaysShroudIncomingTokens`); Enter flips (`Payload.Unshroud`) like a
  click, Enter again takes the card to the table.
- **Menus: what a mouse can click.** A Selectable is navigable only when a raycast at its centre hits it (or its
  ScrollRect viewport), so modal blockers and hidden overlays are handled without a list of overlay names.
  Order is hierarchy order (designer order). "Windows" are `CanvasGroupFader` panels (or top-level panels); a change
  of the set of windows holding the reachable controls = a new context, announced with its title after it fades in.
- **Events are polled** (TabletopEvents) rather than patched wherever state can be read, so no code path is missed.

## Pitfalls found (keep reading before changing things)
- `Selectable.allSelectablesArray` also contains the developer console and bug-report UI of the persistent
  `S0Master` scene: excluded by scene name.
- Nested canvases (`Viewport` on the new-game screen) and small CanvasGroups (legacy entries) are **not** windows;
  only `CanvasGroupFader` marks a window. Titles must be visible (hidden info panels keep placeholder headers).
- Overlay fades: raycasts reach an overlay before its title is visible; announcements wait for window alpha >= 0.6
  and for the context to hold for two refreshes.
- New-game legacy toggles: the ToggleGroup marks the first toggle `isOn` although nothing is selected; the real
  selection is the private `NewGameScreenController.selectedLegacy`.
- `NotificationWindow.Show()` is called before `SetDetails()` in the menu; notifications are read one frame later.
- Game key callbacks run before our Update; `InputGate` flags describe previous frames, so the Escape that closes a mod
  modal is still blocked from the game.
- Output/face-down cards: never say their name before they are turned over (sighted players cannot see it either).
- Mansus place names exist only in the map artwork; no text source exists, so choices are numbered.
- `Ending.DefaultEnding()` has null achievements and crashes the game's chronicler; the dev ending key uses
  `deathofthebody`.
- F12 is Steam's screenshot key and never reaches the game.
- `EnRouteSphere` (cards travelling) and `ExhibitCardsSphere` (credits/news cards) are both `SphereCategory.World`:
  test for `TabletopSphere` by type when "on the table" matters.
- Verbs that have no definition in `verbs/*.json` (despair, illhealth, suspicion...) are created by the Compendium
  with label = id; `Describer.VerbName` prettifies the id ("Despair").
- The mods list (`ModsDisplayPanel`) destroys and recreates its entries on Enable/Disable; the navigator refocuses by
  hierarchy path, then by index, and speaks the new state.
- On the table with Options open, the status bar is still raycast-reachable but disabled; windows whose controls are
  all disabled are treated as background and dropped when any window has a usable control.
- Reaching an ending unlocks a real Steam achievement and writes the game's `achievements.json` to Steam Remote
  Storage; do not trigger endings while testing on a player's account (see memory notes). At startup the game syncs
  both ways (`Achievement.TryValidateAsOfficialAchievement`), so a revert must clear Steam and both files in one
  session (`AchievementsChronicler.ClearAchievement`).
- The table's save error dialog is `Notifier`'s private `SaveErrorWindow` field (a plain `NotificationWindow`); the
  `SaveErrorWindow` class exists only for the save/load panel. Notification windows are windows of their own for the
  navigator (they sit inside a canvas that is itself a `CanvasGroupFader`).
- `InputGate.ModalOpen` is derived from two claims (`HelpOpen`, `PickerOpen`): help can open over a picker, and
  closing help must leave the picker's claim, or the next Escape reaches the game and closes the verb window.
- Key rebinding: the capture flag must be a plain bool (Unity's null check on the destroyed field would leave
  `InputGate.PassThrough` on and every mod key dead); `UiNavigator.Reset` ends a capture.
- `TokenTravelItinerary.Arrive` keeps the departure `Context`, so a card the player sends back (`PlayerDrag`) that
  merges into a table stack (`InteractWithIncoming`) is not an arrival.
- The first announcement of a screen adopts its window set as the context; otherwise the two-refresh debounce reports
  the same set as a change and the screen is announced twice. A context change that leaves focus on the control the
  player last heard (a panel without controls came or went) does not read it again.
- The menu notifier and the save error window are permanent `NotificationWindow`s that get reused: repeat
  suppression is time-limited, or the same message shown twice is read once.
- Build with `build.ps1` (closes the game, `--no-incremental`, verifies the deployed timestamp); a running game locks
  the deployed files.

## Test loop
`build.ps1` → launch → `BepInEx\LogOutput.log` (set `DebugLogging = true` to log every spoken line as `Spoke:`).
Ctrl+Shift+D dumps the navigable items. Back up `%USERPROFILE%\AppData\LocalLow\Weather Factory\Cultist Simulator`
(save.json, restart.json) before driving the game with synthetic keys; a counted key sequence once hit Purge Save.
