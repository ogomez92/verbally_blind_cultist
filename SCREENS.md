# Screen inventory

Sources: scene list and prefabs from the AssetRipper export (S0Master, S1Logo, S2Quote, S3Menu, S4Tabletop,
S5GameOver, S6NewGame, S7UhO), the decompiled `*Screen*`/`*Panel*`/`*Window*` classes, and runtime screen logging
(`Screen: A -> B` lines in the BepInEx log).

Status: **announced** (opening says its name) / **navigable** (every control and text reachable and read with state) /
**verified** (walked in the running game with the log as ears, including edge cases).
Focus model: all screens are tier C (no keyboard focus in the game); the mod's navigators own the keyboard.

| Screen | Class / object | Entry (verified) | Focus model | What a sighted player sees | Hover-only info | Status |
|---|---|---|---|---|---|---|
| Logo video | `SplashAnimation` (S1Logo) | scene load | any key skips (game) | video | – | verified |
| Title quote | `SplashScreen` (S2Quote) | scene load | any key continues (game) | quote, advice | – | verified (quote read) |
| Main menu | `MenuScreenController` (S3Menu) | scene load | UiNavigator | legacy subtitle, Continue/Begin, Credits, Purge, Leave, side buttons, promo | – | verified |
| The Sixth History (DLC and mods) | `OverlayWindow_Mods`, `MenuLegacyStartEntry`, `ModsDisplayPanel`, `ModEntry` | side button | UiNavigator | DLC legacy entries (installed or not), mod list with enable/priority/upload | `[DLL]` hint (HasHintPanel, read by I) | verified (read, Enable/Disable with a temporary test mod, focus kept after the list rebuild) |
| Start DLC legacy confirm | `StartDLCLegacyConfirm` | Enter on an installed DLC legacy | UiNavigator (dialog body read on open) | legacy name, description, Keep save / Begin | – | verified (read, default focus Keep save; Play Legacy not pressed: it replaces the save) |
| News | `OverlayWindow_VersionNews`, `CardDisplayWindow` | side button | UiNavigator; each news card read with its text | version news entries (cards) | card text on click | verified |
| Settings (menu and in game) | `OptionsPanel`, `OptionsPanelTab`, `SliderSettingControl`, `KeybindSettingControl` | side button / Escape in game | UiNavigator; Left/Right adjust sliders; Enter on a key binding waits for the new key | tabs, sliders with value labels, key bindings, Resume, Browse files, Save & Exit, Restart | – | verified (tabs selected with Enter, slider changed and restored, key rebinding, Resume, Save & Exit, Escape) |
| Language | `OverlayWindow_Language`, `LanguageChoice` | side button | UiNavigator | note, languages | – | verified (read) |
| Collection (achievements) | `AchievementsPanel`, `AchievementCategoryTab`, `AchievementEntry` | side button | UiNavigator; achievements as one row each with locked/unlocked | category tabs, achievements with unlock dates | – | verified |
| Credits | `OverlayWindow_Credits` | "Who is responsible?" | UiNavigator; each credits card read with its names | credits cards (role → names) | names shown on click | verified |
| Purge save confirm | `OverlayWindow_PurgeConfirm` | Purge Save | UiNavigator (question read on open; default focus Keep) | question, Keep, Purge | – | verified |
| Menu notification | `MenuNotifier` (`NotificationWindow`) | `MenuScreenController.ShowNotification` | read by NotificationPatches; Escape dismisses (game) | title, description | – | verified (read on open, Escape and Enter dismiss) |
| Save problem / safe mode messages | `MenuScreenController.brokenSaveMessage/suspiciousSaveMessage/noBackupSaveMessage/safeModeMessage` (`HintsHolder`) | conditional on save state / crash (`UpdateAndShowMenu`, `RestoreFromBackup`) | spoken when shown (MenuMessages); UiNavigator reaches text rows + buttons | message, restore/browse/restore mods/discard buttons | – | verified (each combination the game produces shown with the developer key Ctrl+Shift+F3; read on appearance, every text and button reachable; buttons not pressed) |
| Table: board | `TabletopSphere` (S4Tabletop) | scene load | TabletopNavigator Board: Verbs / Cards / Controls | verbs with timers and badges, cards with decay timers, status bar | card/aspect/slot details windows (I key) | verified |
| Table: verb window | `SituationWindow`, `ThresholdSphere`, `NotesSphere`, `OutputSphere` | Enter on verb, or game opens it | TabletopNavigator Window | story text (pages), slots, Start, timer, stored cards, deck draws, results, Collect all, aspect totals | slot requirements (details), aspect meanings, deck descriptions | verified (unstarted, running, complete, face-down results); a slot opened by a running recipe verified with Dream + Passion ("Dream wants a card: Lore slot...", "Lore slot open" on the board, in T and in I); the "will become" prediction and deck descriptions are not yet walked in the game (they need a Lore card and a recipe that draws from a deck) |
| Table: pickers | mod | Enter on slot / card / portal | modal list | – | – | verified |
| Table: Mansus | `Numa`, `Otherworld`, `OtherworldDominion`, `EgressThreshold` | portal opens itself | TabletopNavigator Mansus | map, face-down choices, egress | – | verified |
| Table: portal with dream card | `Ingress`, `PortalManifestation` | after the Mansus | Verbs group entry + picker | journal text, card, close button | – | verified |
| Table: status bar and controls | `StatusBar`, `ElementOverview`, `SpeedControlUI`, `OptionsButton`, `StackButton` | always | Controls group; H key | name, profession, counts, speed/options buttons | – | verified (read) |
| Table: notifications | `Notifier` → `NotificationWindow` | game events | read automatically | title, description | – | verified |
| Table: save error | `Notifier.SaveErrorWindow` (a `NotificationWindow`; the `SaveErrorWindow` class is not used on the table) | failed save (`GameGateway.TryDefaultSave` → `Notifier.ShowCustomWindow`) | message read by NotificationPatches; UiNavigator takes over while it is up | message, Continue, Reload | – | verified (shown through the game's own call with the developer key Ctrl+Shift+F3; message read once, then the focused button, all texts and buttons reachable, Continue returns to the table) |
| Table: ending sequence | `GameGateway.EndGame` | ending recipe | announced ("This life is ending") | camera zoom, effect, fade | – | verified |
| Ending | `GameOverScreenController` (S5GameOver) | scene load | UiNavigator (ending read on open) | title, description, Main Menu, Begin Another Descent | – | verified |
| Choose legacy | `NewGameScreenController` (S6NewGame) | scene load | UiNavigator; legacy toggles read with the game's real selection | 3 legacy images, info panel, Back, Start New Game | – | verified (selection, info) |
| Error screen | `InfoSceneController` (S7UhO) | any error logged outside dev mode (`NoonUtility.Log` level > 1 → `StageHand.LoadInfoScene`) | UiNavigator (whole message read on open) | error text, Log Files, Exit | – | verified (loaded with the developer key Ctrl+Shift+F8 from the menu; message read once, all items reachable, Exit quits) |
