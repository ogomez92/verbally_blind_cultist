# Cultist Simulator Accessibility

A screen reader mod for Cultist Simulator (Steam / GOG, Windows). It lets you play the whole game with the keyboard
and a screen reader: the menus, the table of cards and verbs, verb windows, the Mansus, endings and new legacies.
Speech goes to NVDA, JAWS and other screen readers through the Prism library; with no screen reader running it uses
Windows speech.

## Installing

1. Unzip the whole release zip into the game folder, for example
   `C:\Program Files (x86)\Steam\steamapps\common\Cultist Simulator`, so that `winhttp.dll` ends up next to
   `cultistsimulator.exe`. The zip contains BepInEx 5 for **32-bit** games (Cultist Simulator is a 32-bit game; the
   64-bit BepInEx does not work with it) and the mod in `BepInEx\plugins\CultistAccessibility`.
   If you already use BepInEx x86, copy only `BepInEx\plugins\CultistAccessibility`.
2. The mod folder holds `CultistAccessibility.dll` and a `SpeechHost` folder: a small 64-bit helper program with
   `prism.dll`. The game itself is 32-bit and cannot load Prism, so the mod speaks through this helper.
3. Start the game. After a few seconds you hear "Cultist Simulator accessibility loaded".

Requirements: Windows 10 or 11 (64-bit), .NET Framework 4.8 (built into Windows 10 1903 and later and Windows 11).
If your screen reader starts after the game, the mod switches to it automatically within a few seconds.

To uninstall, delete `BepInEx\plugins\CultistAccessibility` (and `winhttp.dll` to remove BepInEx).

## Keys

Everywhere:

| Key | Action |
|---|---|
| Up / Down arrows | Move through the items of the current screen, window or list |
| Home / End | First / last item |
| Enter | Activate: press a button, open a verb, choose from a list |
| Left / Right arrows | Change sliders and choices; on the table, switch between Verbs, Cards and Controls |
| Escape | Close a window, list or help (on the table, Escape with nothing open opens the game's options) |
| F1 | Help for the current screen, as a list you read with the arrows |
| F2 | Cycle verbosity: terse, normal, verbose |
| R | Repeat the focused item |
| I | Inspect: everything about the focused item (description, every aspect and what it means, slot rules) |
| A | Read the whole screen, or the whole group on the table |
| Control + Up / Down | Read the review buffers (older / newer) |
| Control + Left / Right | Switch review buffer: Events, Details, Story, Verbs, Table, Status |

On the table:

| Key | Action |
|---|---|
| Left / Right | Switch group: Verbs (and Mansus portals), Cards, Controls |
| Enter on a verb | Open its window |
| Enter on a card | List the verbs that can take it; Enter puts the card in |
| H | Your character and the status bar: health, passion, reason, funds and the game speed |
| T | Game speed, every busy verb with its time left (soonest first), finished verbs, decaying cards |
| G | Jump to the next verb with results waiting |

In a verb window:

| Key | Action |
|---|---|
| Up / Down | Story text, slots, Start, cards inside, results, Collect all, aspect totals |
| Enter on a slot | List the cards on the table that fit it (the first choice empties a full slot) |
| Delete or Backspace | Take the card out of the focused slot |
| Enter on Start (or the game's S key) | Begin the recipe |
| Enter on a face-down result | Turn it over; Enter again takes it to the table |
| Enter on Collect all (or the game's C key) | Take every result to the table |
| Page Up / Page Down | Turn the pages of a long story (also Left / Right on the story line) |
| Escape | Close the window |

In the Mansus: Up / Down move between the places, Enter turns a face-down card over, Enter again takes it back.
The card then waits in the portal, listed with the verbs: Enter on the portal to read what you remember and take the
card.

The game's own keys still work: Space pause, N normal speed, M fast forward, S start, C collect all, Tab stack cards,
Escape options. The mod uses the arrow keys itself, so they no longer move the camera (the camera follows the focused
card or verb instead).

## What is announced

Verbs starting, continuing, finishing (with how many cards are waiting), appearing and vanishing; a running verb
that opens a slot and wants a card (the slot is also named when you focus the verb and in the T readout), and what
the verb will become when you put a card there; a warning when a dangerous countdown is about to run out; cards that decay, change or vanish on the table; cards that arrive on the
table; greedy slots taking cards; the game's pop-up messages; pause and speed changes; the Mansus. Everything
announced is also kept in the Events review buffer, and the story texts of finished verbs in the Story buffer.
The events are also written to `CultistAccessibility_events.log` next to the mod, which helps with bug reports.

## Languages

The mod speaks the language the game is set to (Options, Language): English, Spanish, German, French, Russian,
Japanese and Simplified Chinese. Changing the game's language changes the mod's speech at once. Your screen reader or
Windows voice must have a voice for that language.

To correct a translation or add one, put a text file named after the game's language id (`en`, `es`, `de`, `fr`,
`ru`, `jp`, `zh-hans`) in a `lang` folder next to the mod, for example
`BepInEx\plugins\CultistAccessibility\lang\es.txt`, with lines such as `Paused = En pausa`. Only the lines you want to
change are needed; anything missing falls back to the built-in text, then to English. The built-in files, with every
key, are in the `translations` folder of the release zip.

## Settings

Edit `BepInEx\config\accessibility.cultistsimulator.screenreader.cfg` with a text editor while the game is closed.

- General: `Verbosity`, `SpeechBackend` (`Auto` uses your screen reader, or Windows speech when none runs; you can
  force `NVDA`, `JAWS`, `SAPI`, `OneCore`...), `Braille`, `InterruptOnNavigation` (arrow keys cut off speech),
  `SpeakHints`, `MoveCameraToFocus`, `DebugLogging` (writes every spoken line to `BepInEx\LogOutput.log`).
- Announcements: turn each kind of announcement on or off; `ReadCompletionText` reads the whole story when a verb
  finishes; `TimerWarningSeconds` sets how early the danger warning comes.
- Keys: every mod key can be changed (names from Unity's key list, for example `F1`, `R`, `Delete`).

Leave `DeveloperTestKeys` set to false: it enables test shortcuts that change your game.

## Credits and licences

Speech uses Prism (MPL-2.0); its licence files are in `SpeechHost\LICENSES` together with its `NOTICE`. BepInEx is
LGPL-2.1. Cultist Simulator is © Weather Factory; no game code or assets are included in this mod.
