# MOD_TEMPLATE.md

A playbook for an AI coding agent building a **screen reader accessibility mod for a Unity roguelike / deckbuilder**. It is distilled from a shipped mod of this kind, but it deliberately contains **no game-specific facts**. Every class name, method name, screen, keyword and key binding for the target game must be discovered by you, from the game itself. Names in the code samples (`SomeScreen`, `CardUI`, `GetTitle`) are placeholders, not predictions.

The goal is not "the mod speaks some text". The goal is: **a blind player can start the game, finish a run, and use every screen in between without sighted help.**

---

## 0. Non-negotiable rules

1. **Extract the game before writing code.** Decompile the game assemblies (ILSpy or equivalent) and rip the assets (AssetRipper). Never guess a method signature, field name or enum value; read it. A Harmony patch against a method that does not exist fails silently and costs hours.
2. **Traverse every screen in the game.** Build a complete screen inventory (section 3) and drive each entry to "verified". A screen you did not look at is a screen a blind player gets stuck on. Popups, confirmation dialogs, tutorials, pause menus, end-of-run summaries, credits, DLC prompts and error dialogs all count.
3. **Speak through Prism** (`C:\Users\Nitropc\code\libs\prism`), via one wrapper class. No other code calls the native library.
4. **Never interrupt speech by default.** Always queue (`interrupt = false`). Combat events arrive in bursts, and interrupting cuts off information the player has no other way to get.
5. **Announcements are concise; details go to review buffers** (section 8). Focus on a card says name, cost and stats. Rarity, rules text, keyword explanations and flavor text go into a buffer the player reads on demand.
6. **Prefer game state over UI labels.** Read from managers, state and data objects. UI text is a fallback: it contains prefab placeholders, stale values and rich-text markup.
7. **Review features never change game state.** Virtual cursors (board review, map review) only read; they never move the game's real selection.
8. **Everything a sighted player learns by hovering must be reachable by keyboard.** Tooltips, intent icons, status icons, and the map preview are all information a blind player needs.
9. **If the keyboard cannot reach it, build the path.** Many games in this genre are mouse-first (hover, click, drag). Where the game has no keyboard navigation, the mod writes the navigation layer itself (section 7.4). "The game does not support it" is never a reason to leave a screen unreachable.
10. **Write down what you learn.** Maintain `CLAUDE.md` (architecture plus pitfalls), `PATCH_TARGETS.md` (verified patch targets) and `SCREENS.md` (the inventory). A future session starts with no memory of this one.
11. **Do not invent game facts in documentation or code comments.** If you did not read it in the decompiled source or observe it in the log, it is a hypothesis; label it as one and verify it.

---

## 1. Phase 1: Reconnaissance

Answer these questions before anything else, and record the answers in `CLAUDE.md`.

| Question | How to find out |
|---|---|
| Scripting backend: Mono or IL2CPP? | `<Game>_Data\Managed\Assembly-CSharp.dll` exists = Mono. `GameAssembly.dll` + `il2cpp_data` = IL2CPP. |
| Bitness | x64 vs x86 game executable. Prism ships Windows binaries for **x64 and arm64 only**. If the game is 32-bit, stop and tell the user. |
| Unity version | File properties of the game exe / `UnityPlayer.dll`, or `globalgamemanagers` via AssetRipper. |
| Mod loader | Mono: BepInEx 5.x (matching bitness). IL2CPP: BepInEx 6 (IL2CPP build) or MelonLoader. Check whether the game's modding community already standardized on one. |
| UI framework | uGUI + TextMeshPro, UI Toolkit, NGUI, or custom. Determines how focus and text extraction work. |
| Input path(s) | Legacy `Input`, the new Input System, Rewired, or a custom mapping layer. There are often **two** paths (EventSystem navigation *and* a game-specific control mapping). You must know both to suppress keys correctly (section 9). |
| Which **navigation tier** is the game? | **Tier A**: full keyboard navigation through the uGUI EventSystem, so your work is mostly *reading* focus (7.1). **Tier B**: the game has its own controller navigation system (custom navigation items, layers, a virtual cursor) that the keyboard does not drive, or drives only partly, so you drive *that system* from the keyboard (7.4). **Tier C**: mouse-only, so you build the whole focus layer (7.4). Decide this per game **and re-check per screen**: tier A games still have mouse-only corners, and tier B games still have pages with nothing focusable on them. |
| Is anything gated on mouse/gamepad-only input? | Grep the source for drag handlers, hover-dependent logic, right-click actions, and **polled waits** (`while (!IsPressed("Select"))`-style loops in coroutines). Each one is a place a keyboard player gets hard-stuck. |
| Localization system | I2 Localization, Unity Localization, or custom. Find the single "localize this key" entry point. |
| Narrative engine | Ink, Yarn, custom. Event/story text usually flows through it. |
| Existing modding toolkit | A community API for the game is a free index of useful patch targets, even if you do not depend on it. |

**IL2CPP note:** you get type and method *signatures* (via Cpp2IL / Il2CppDumper dummy DLLs) but not method bodies. Harmony still works through the loader's interop layer, but reflection over game objects goes through generated wrapper types. Expect more runtime logging and less reading. The rest of this document assumes Mono; adapt the mechanics, keep the architecture.

---

## 2. Phase 2: Extract the game

### 2.1 Code: ILSpy

Decompile the game's own assemblies into a `game/` folder in the repo, used **purely as reference** (never compiled into the mod).

```bash
dotnet tool install -g ilspycmd
ilspycmd -p --nested-directories -r "<Game>_Data\Managed" -o game "<Game>_Data\Managed\Assembly-CSharp.dll"
```

- Also decompile `Assembly-CSharp-firstpass.dll` and any game-specific assemblies in `Managed\` (ignore `UnityEngine.*`, `System.*`, third-party libs unless you need them).
- dnSpyEx or the ILSpy GUI are fine alternatives. What matters is that the result is greppable text on disk.
- Decide with the user whether `game/` is committed. It is copyrighted material: keep it out of releases, and gitignore it if the repo is public.

### 2.2 Assets: AssetRipper

An AssetRipper install is available at:

```
S:\games\software\AssetRipper_win_x64\AssetRipper.GUI.Free.exe
```

It starts a local web server and opens a browser UI. Load the game folder (File, Open Folder), then export (Export, Export All Files) to a directory **outside the repo or gitignored** (the export is large and copyrighted). Recent versions accept `--port <n>` and `--launch-browser false` and expose the same actions as HTTP form posts (`/LoadFolder`, `/Export/UnityProject`, each with a `path` field), which allows driving it headlessly. Verify this against the running instance; if it does not work, ask the user to click through the GUI once. It writes timestamped logs next to the exe, so check them if an export silently produces nothing.

What to mine from the export:

- **Scenes and screen prefabs**: the component hierarchy of each screen: which MonoBehaviour sits on which GameObject, and what its serialized fields point at. This tells you where text lives *before* you ever run the game.
- **Localization tables**: all player-facing strings and their keys. Essential for keywords, status effects and screen titles.
- **TextAssets / ScriptableObjects**: card, unit, relic, event and enemy definitions; narrative scripts; tutorial text.
- **Input assets**: default key bindings. You need the ground truth of which keys the game already uses before choosing hotkeys.
- **Sprite/icon names**: games embed icons inline in text (`<sprite name="...">`). You need the full list to convert them to words.

### 2.3 Build a map of the code

From `game/`, locate and document (in `CLAUDE.md`) the following. Every game in this genre has an equivalent of each:

- The **screen manager** and the enum or registry of screens; the base class of screens; how screens open, close and stack; how the game decides which screen is interactable.
- The **managers**: deck/hand/piles, combat/turn phases, player resources, run/save state, map/progression, shop, rewards, status effects, relics/artifacts.
- **State vs data classes**: runtime instances (a card in hand, a unit on the board) versus definitions/templates. Find the name, description, cost and stat getters on each.
- The **combat event surface**: methods for damage, death, spawn, status add/remove, card draw/play/discard/exhaust, turn and phase changes, heal, buff, relic trigger.
- **Preview/simulation mode**: many deckbuilders simulate outcomes (damage previews) by running real combat code with a flag set. Find that flag now; it is the source of phantom announcements later.
- The **tooltip system**: how hover text is assembled. It is usually the best source of keyword explanations.
- The **targeting flow**: how a card is selected, how a target (unit, slot, row, floor, lane) is chosen and confirmed or cancelled.

---

## 3. Phase 3: Screen inventory (mandatory)

Create `SCREENS.md` and enumerate **every** screen, overlay and modal. Use three independent sources, because each one misses things:

1. **Code**: the screen enum/registry, every subclass of the screen base class, and every class named like `*Screen`, `*Dialog`, `*Popup`, `*Panel`, `*Overlay`, `*UI`.
2. **Assets**: scene list and screen prefabs from the AssetRipper export.
3. **Runtime**: a temporary logging patch on the screen manager's open/show method, then play: start a run, win a fight, lose a run, open every menu. Log every screen name that appears.

Beware of "screens" that are not screens: a victory panel or reward summary may be a component of the battle screen rather than a separate entry in the screen manager. Your state tracking must handle those explicitly.

One row per screen:

| Screen | Class | Entry method (verified) | Focus model | Info a sighted player sees | Hover-only info | Status |
|---|---|---|---|---|---|---|
| … | … | … | tier A (EventSystem) / tier B (game's nav system) / tier C (mouse-only), plus any mouse-only interactions on it (drag, right-click, hover) | … | … | not started / announced / navigable / verified |

Status definitions:

- **announced**: opening the screen says its name and a one-line orientation.
- **navigable**: every interactive element can be reached by keyboard and reads correctly on focus; every piece of visible information is reachable (focus, hotkey, buffer or read-all).
- **verified**: tested in the running game with a screen reader, including the edge cases (empty lists, locked items, disabled buttons, not enough currency).

Genre prompts to make sure nothing is forgotten (this is a checklist of *questions*, not the inventory; the inventory comes from the game): splash and main menu, profile/save slots, run setup (character/class/deck/difficulty/mutators/seed), map or path choice, battle, targeting modes, pile views (deck, draw, discard, exhaust), card reward/draft, relic reward, shop, upgrade/remove/duplicate/transform, rest or campfire, narrative events, treasure, boss intro, victory and defeat summaries, unlocks and meta progression, compendium/collection, statistics, leaderboards, daily/challenge modes, multiplayer lobbies, settings (every tab, including key rebinding), pause menu, confirmation dialogs, tutorial popups, credits, DLC and news panels, error dialogs.

**The mod is not done while any row is below "verified".**

---

## 4. Phase 4: Project scaffold

```
<Game>Accessibility/
├── Core/              # Speech wrapper, input, config, keywords, buffers, focus helpers
├── Core/Buffers/      # AnnouncementBuffer, BufferManager, FocusReadout, focus/battle buffer sets
├── Battle/            # Manager cache, hand/board/enemy/resource readers, targeting, board review
├── Navigation/        # Tier B/C only: item collection, focus + hover sync, activation, input repeat, select simulator
├── Screens/           # Coordinators (menu focus poller, battle coordinator, map navigator) or per-screen handlers (7.4)
├── Screens/Readers/   # One text extractor per UI domain (cards, shop, relics, map, settings, events, dialogs, tooltips, ...)
├── Patches/Screens/   # One patch per screen transition
├── Patches/Combat/    # Combat event patches
├── Patches/           # Card events, targeting, input suppression
├── Help/              # Context-sensitive help + screen state tracker
├── Help/Contexts/     # One help provider per screen or mode
└── Utilities/         # Text cleanup, localization, reflection, UI text helpers
```

`csproj` essentials (BepInEx 5 / Mono):

```xml
<PropertyGroup>
  <TargetFramework>netstandard2.0</TargetFramework>  <!-- or net35/net46 for old Unity; match the game's Mono profile -->
  <LangVersion>8.0</LangVersion>
  <GamePath Condition="'$(GAME_PATH)' != ''">$(GAME_PATH)</GamePath>
  <GamePath Condition="'$(GamePath)' == ''">DEFAULT INSTALL PATH</GamePath>
</PropertyGroup>

<!-- Reference from the game folder; never copy these to output -->
<Reference Include="BepInEx"><HintPath>$(GamePath)\BepInEx\core\BepInEx.dll</HintPath><Private>false</Private></Reference>
<Reference Include="0Harmony"><HintPath>$(GamePath)\BepInEx\core\0Harmony.dll</HintPath><Private>false</Private></Reference>
<!-- plus UnityEngine.CoreModule, UnityEngine.UI, the input module(s) in use, TextMeshPro if separate, Assembly-CSharp -->

<!-- Deploy on build so the test loop is: build, launch -->
<Target Name="CopyToPlugins" AfterTargets="Build">
  <Copy SourceFiles="$(OutputPath)$(AssemblyName).dll" DestinationFolder="$(GamePath)\BepInEx\plugins\" />
  <Copy SourceFiles="PATH\TO\prism.dll" DestinationFolder="$(GamePath)\BepInEx\plugins\" />
</Target>
```

Plugin entry (`BaseUnityPlugin.Awake`), in this order: config, speech, keyword dictionary, buffers, `ApplyPatches()`, `CreateHandlers()` (persistent `DontDestroyOnLoad` GameObjects hosting your MonoBehaviours), `RegisterHelpContexts()`. Expose static `LogInfo/LogWarning/LogError` helpers; you will call them from everywhere.

Even though you reference `Assembly-CSharp`, do most game access through **cached reflection** and patch by name (section 6). Game updates rename and move things. Reflection degrades to a logged warning; a hard compile-time reference degrades to a plugin that fails to load at all.

---

## 5. Speech: Prism

Prism is a C library that abstracts screen readers (NVDA, JAWS, and others) and TTS engines (SAPI, OneCore, …) behind one API and picks the best available backend. It lives at:

```
C:\Users\Nitropc\code\libs\prism\
├── prism-windows-x64\include\prism.h                ← the API. Read it; it is the ground truth.
├── prism-windows-x64\dynamic\release\bin\prism.dll  ← ship this next to the plugin DLL
└── prism-sdk-v0.18.2\doc\index.html                 ← full manual (conventions, thread safety, backend notes)
```

Two packages are present (`prism-windows-x64` and `prism-sdk-v0.18.2`). Take the header and the DLL **from the same package**. The `tolk.dll` beside it is a Tolk-compatibility shim and is not needed. There is no C# binding, so write a small P/Invoke layer. Facts from the header and manual that matter:

- Calling convention is **cdecl**. Strings are **null-terminated UTF-8**; the caller owns them and they only need to outlive the call.
- C `bool` is **1 byte**: marshal as `UnmanagedType.I1`. The default P/Invoke `bool` is a 4-byte Win32 `BOOL` and will misbehave.
- `prism_init(NULL)` is valid and uses the global backend registry.
- `prism_registry_create_best(ctx)` returns the highest-priority backend that initializes, **already initialized**; do not call `prism_backend_initialize` on it. The manual recommends `create_best` over `acquire_best` unless you need to share backend state.
- Pointers returned by the library (backend name, error strings) are library-owned: **never free them**. The context and the backend are the two things you *do* release (`prism_backend_free`, then `prism_shutdown`).
- **A backend instance is not thread-safe.** Call it only from Unity's main thread.
- `prism_backend_output` = speech + braille where the backend supports both. Check `prism_backend_get_features` for `PRISM_BACKEND_SUPPORTS_OUTPUT` (bit 5) / `SPEAK` (bit 2) / `BRAILLE` (bit 4) / `STOP` (bit 7).
- Most functions return `PrismError`; `0` is `PRISM_OK`. `prism_error_string` gives readable text for the log.

```csharp
internal static class PrismNative
{
    private const string Dll = "prism";

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr LoadLibrary(string path);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr prism_init(IntPtr cfg);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void prism_shutdown(IntPtr ctx);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr prism_registry_create_best(IntPtr ctx);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void prism_backend_free(IntPtr backend);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr prism_backend_name(IntPtr backend);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong prism_backend_get_features(IntPtr backend);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int prism_backend_speak(IntPtr backend, byte[] utf8, [MarshalAs(UnmanagedType.I1)] bool interrupt);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int prism_backend_output(IntPtr backend, byte[] utf8, [MarshalAs(UnmanagedType.I1)] bool interrupt);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int prism_backend_braille(IntPtr backend, byte[] utf8);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int prism_backend_stop(IntPtr backend);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr prism_error_string(int error);

    // Old Unity Mono runtimes do not reliably support UnmanagedType.LPUTF8Str; encode by hand.
    internal static byte[] Utf8Z(string s)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(s) + 1];
        Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
        return bytes; // last byte is already 0
    }
}
```

`ScreenReaderOutput` (the only class that touches `PrismNative`):

- **Where `prism.dll` lives**: normally next to the plugin DLL. Some mod loaders (including games' built-in mod systems) try to load *every* DLL in a mod folder as a managed assembly and choke on a native one. In that case ship `prism.dll` elsewhere (the game root, for example) and load it by full path.
- **Initialize**: the plugins folder is not on the DLL search path, so `LoadLibrary(Path.Combine(pluginDir, "prism.dll"))` first, then `prism_init(IntPtr.Zero)`, then `prism_registry_create_best`. Log the backend name. Announce "`<Mod>` loaded" so the player knows speech works. Catch `DllNotFoundException` / `BadImageFormatException` (wrong bitness) and log a clear message.
- **Speak(text, interrupt = false)**: bail on empty text or missing backend; run the text through the cleanup pipeline (section 11); call `prism_backend_output` if supported, otherwise `prism_backend_speak` (+ `prism_backend_braille` if enabled in config). On a non-OK result, log `prism_error_string`, and (rate-limited) free the backend and `create_best` again. That is how the mod recovers when the player starts or restarts their screen reader after the game.
- **Main thread only**: if any patch can fire off-thread, enqueue into a `ConcurrentQueue<string>` and drain it from a MonoBehaviour `Update()`.
- **Log every spoken line verbatim** ("Spoke: …") behind a debug category. A report of "it read this wrong" is undiagnosable if the log has nothing to match it against.
- **Two entry points**: `Say` for focus echoes and on-demand readouts, `SayEvent`/`LogEvent` for unsolicited announcements (combat, story, popups, screen changes). Only the second is recorded in the Events buffer; recording focus echoes would bury the events under navigation chatter.
- **LogEvent(text)**: append to the Events buffer and to a plain-text event log file next to the plugin (overwritten each launch). The file is invaluable for bug reports from blind players.
- **Shutdown** (`OnDestroy` / application quit): stop, `prism_backend_free`, `prism_shutdown`.

---

## 6. Patching pattern

Use **manual patching** by name, with no `[HarmonyPatch]` attributes. One static class per target with a `TryPatch(Harmony)` that can fail without taking the plugin down:

```csharp
public static class SomeScreenPatch
{
    public static void TryPatch(Harmony harmony)
    {
        try
        {
            var type = AccessTools.TypeByName("SomeScreen");            // verified in game/
            var method = type != null ? AccessTools.Method(type, "Initialize") : null;
            if (method == null)
            {
                Plugin.LogWarning("SomeScreen.Initialize not found - screen will not be announced");
                return;
            }
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(SomeScreenPatch).GetMethod(nameof(Postfix))));
            Plugin.LogInfo("Patched SomeScreen.Initialize");
        }
        catch (Exception ex) { Plugin.LogError($"Failed to patch SomeScreen: {ex.Message}"); }
    }

    public static void Postfix(object __instance)
    {
        try
        {
            ScreenStateTracker.SetScreen(GameScreen.Some);
            // read orientation info from __instance / managers, then Speak(..., false)
        }
        catch (Exception ex) { Plugin.LogError($"Error in SomeScreen patch: {ex.Message}"); }
    }
}
```

Rules:

- **Every patch body is wrapped in try/catch.** An exception escaping a Harmony patch can break the game method it is attached to.
- **Log success and failure of every `TryPatch`.** After each launch, grep the log for "not found" / "Failed to patch". A patch that silently did not apply looks exactly like a feature that does not work.
- **Record each verified target in `PATCH_TARGETS.md`** (type, method, signature, what the patch uses it for).
- Use positional args (`__0`, `__1`), `__instance`, `__result`, and `__state` to pass data from prefix to postfix.
- **Coroutines**: patching an `IEnumerator` method patches the *kickoff*, not the completion. Useful ("combat is about to stop") but know which one you are getting.
- **Timing pitfall**: when game method A synchronously calls patchable method B, B's postfix runs *before* A's postfix. If you need state "before A changed it", capture it in a **prefix**.
- **Overloads**: `AccessTools.Method(type, name)` throws on ambiguity; pass parameter types.
- Cache every `MethodInfo`/`FieldInfo` you look up. Reflection in a per-frame path without caching will cause visible stutter.

---

## 7. Reading focus and text

### 7.1 Focus polling

If the game uses uGUI navigation, a MonoBehaviour polls `EventSystem.current.currentSelectedGameObject` every frame and, when it changes, extracts text and speaks it. Polling (rather than patching every selectable) catches focus changes from every source: keys, mouse, and game code.

If the game tracks some selections **outside** the EventSystem (the selected card in hand, the selected board slot, the selected map node), poll that game state too. Do not rely on key detection alone: the game changes selection on its own (after a card resolves, on phase changes, on re-selection).

### 7.2 The extraction chain

`GetTextFromGameObject(go)` tries **domain readers in priority order**, most specific first, and falls through:

1. Special cases that would otherwise be misread (scrollbars, dialog buttons, opening screens)
2. Rich game objects: card UI, shop item, relic/artifact, unit, battle intro
3. Map nodes, settings controls (dropdown / slider / toggle, *with current value and state*), generic toggles
4. Compendium / collection / stats entries
5. Run-setup elements (character/class pickers, difficulty selectors, *locked* state and unlock requirement)
6. Tooltip-only elements, event choices
7. `GetTextWithContext()`: generic label handling
8. `CleanGameObjectName()`: last resort, so that *something* is always spoken

To support a new UI element: add a reader method and insert it at the right priority. One reader class per UI domain (`CardTextReader`, `ShopTextReader`, `RelicTextReader`, `MapTextReader`, `SettingsTextReader`, `EventTextReader`, `DialogTextReader`, `TooltipTextReader`, …).

`GetTextWithContext()` heuristics that generalize well: text of 1-2 characters is probably an icon glyph, so use the cleaned GameObject name instead; 3-4 characters or empty means look up the hierarchy for a label, **skipping container names** (container, panel, holder, group, content, root, layout, wrapper, …).

### 7.3 Traps in UI text extraction

- **The focused object is usually a child.** The EventSystem focuses the `Selectable`; the component that knows what the thing *is* sits on an ancestor. Always search self **and parents** (`FindComponentInSelfOrParents`).
- **Serialized references point down.** A parent component referencing a child button will not be found by searching up from the parent. Read the field by reflection or search children.
- **TMP `.text` can lie.** If the game sets text through `TMP_Text.SetText(...)` (directly or via an extension), the rendered buffer updates but the `text` property may still hold the **prefab's design-time placeholder**. Provide a helper that prefers `GetParsedText()` and falls back to `.text`. But note `GetParsedText()` is one frame stale for labels set this frame, so focused-item readers that run right after the game sets text should use `.text`-based reads, while "read the whole screen" dumps should use the rendered text with **no** `.text` fallback (or placeholders like "Card text goes here" resurface).
- **Tooltips keep their text when disabled.** Check `enabled` before trusting a tooltip provider.
- **Placeholder filter**: keep an `IsPlaceholderText()` for prefab junk ("New Text", "Lorem", "12345", …) found in this game's prefabs.
- **State is part of the text**: selected/unselected, on/off, locked (and why), disabled, affordable/unaffordable, "3 of 7". Sighted players get these from color and position; you must say them.

### 7.4 Writing a keyboard navigation layer (tier B and C games)

Some games cannot be made accessible by *reading* focus, because there is no keyboard focus to read: the game is played with a mouse (hover, click, right-click, drag and drop), possibly with a controller mode on the side. Then the mod must **own navigation**: decide what is focusable, move focus with the arrows, keep the game's own pointer state consistent with that focus, and activate things the way a mouse would. This is the largest single piece of work in such a mod, and every item below comes from a bug that shipped at least once.

#### Reuse the game's navigation system if one exists (tier B)

If the game supports a controller, it almost certainly has its own registry of navigable items (a component on each focusable object, grouped in layers for popups, with a "current item" and a "set current item" method). **Drive that system instead of building a parallel one**:

- The game already maintains the item list, enables and disables items as panels open, and scopes them to the active layer/popup. Your `GetItems()` is "the game's available items, filtered and sorted".
- Setting the game's current item gives you its highlight visuals, scrolling-into-view and sounds for free, which also keeps the mod usable by low-vision players and testable by sighted ones.
- The game may only process its navigation in **controller mode** and clear the current item whenever it thinks the mouse is in use. Find that flag and force controller mode whenever you move focus.
- Watch the game's **active navigation layer**. A change means a popup or panel opened inside the same screen: reset focus tracking, re-announce, and let the handler react.

Only in tier C (nothing to reuse) do you build your own registry: `FocusableItem` (label provider, activate action, optional adjust action, anchor transform), `FocusContext` (items and ordering for one screen or modal, stackable for popups), `VirtualFocusManager` (current context, movement, activation). Draw a visible focus indicator if you can.

#### Per-screen handlers with a generic fallback

When the mod owns navigation, organise it as one **handler object per screen** with `OnEnter` / `OnUpdate` / `OnExit`, and a router that picks the active handler every frame **from game state, not from patches**: the active scene or screen key; overlay scenes that load on top without changing the active key (check these first, in stacking order); flags such as "paused" for menus that live in an always-loaded scene.

- One handler may serve several scenes (a map split across a "systems" scene and a "visuals" scene).
- **Register a generic fallback handler** for every screen with no dedicated handler: it names the screen and offers default arrow navigation over whatever is focusable. A screen nobody wrote a handler for must degrade to "clumsy", never to "dead keyboard". It also tells you, through the log, which screens still need a real handler.

A `NavigableScreenHandler` base class runs the standard loop each frame:

1. **Announce the screen once the UI has settled**: `TryAnnounceScreen()` returns false to retry while content is still loading. Add a **timeout** that speaks the plain screen name anyway; a precondition that never becomes true must not leave the screen unnamed forever.
2. Detect navigation-layer changes.
3. Ignore input for a short grace period after entry (a few hundred milliseconds), so the key press that opened the screen does not also act inside it.
4. **Route input through a priority chain**: blocking cinematics/overlays, then help or tutorial popups, then inspect/detail views, then inventory-style overlays, then the handler's own `HandleInput()`. Each stage returns true when it owns the keys this frame.
5. Pump any pending refocus request (below).
6. Announce the focused item if it changed.

Virtual hooks keep handlers small: `GetItems()`, `Navigate(dir)`, `Confirm()`, `DefaultFocusItem()`, `GetItemDescription(item)`, `ShouldAnnounceFocus(item)`, `SuppressFocusAnnouncements`, `OnNavigationLayerChanged(layer)`.

Use **`Time.unscaledTime`** for every timer in this layer. Pause menus set `timeScale` to 0, and scaled timers simply stop.

#### Collecting and ordering items

- **Collect**: controls with a real activation or value-change handler (buttons, sliders, toggles, dropdowns, tabs), plus game objects that are meaningful to inspect (cards, units, slots, map nodes, shop goods).
- **Skip**: inactive objects, objects whose parent panel is hidden or non-interactable, decorative elements, and internal containers.
- **Order spatially**, because that is the order a sighted player perceives. Default linear model: Up/Down walks items sorted top to bottom, Left/Right walks items sorted left to right. Give grids real row and column movement, and multi-column pages an explicit column-major or row-major order. `List.Sort` is unstable, so add a tie-breaker (an insertion index) for items that share an anchor position.
- **Never answer a key press with silence.** Nothing focusable: say so ("nothing to navigate here"). At the end of a list: say "first item" / "last item" or repeat the item. A silent arrow key is indistinguishable from a broken mod.
- **Hold-to-repeat**: fire on the first press, then after an initial delay, then at a repeat rate. While Ctrl is held the arrows belong to the review buffers and the real focus must not move.
- **Text-field guard**: while a text input has focus (naming a run, a console), all letter hotkeys are inactive.

#### Focus is hover is the armed target

The most dangerous class of bug in a navigation layer: **the item the player heard is not the item the game will act on.** Mouse-driven games act on whatever is *hovered* when the select input fires. If focus moves but the game's hover does not follow, Enter activates something the player never heard: a button behind the open menu, the previously focused unit, a leftover tab.

- After every focus move, push the game's hover state onto the focused item. Make this idempotent and call it freely.
- **Unhover the old object first, then hover the new one.** Game event systems often send pointer-enter to the new object without pointer-exit on the old one. If the game then refuses the new hover (not a legal target for the held card), the previous target stays armed. Clearing first makes a refused hover **fail closed**: the card goes back to hand instead of hitting the wrong unit.
- If the focused item has nothing to hover, **clear** the stale hover.
- **Clear Unity's selected object** after any simulated pointer-down. uGUI marks the pressed `Button` as selected, and the input module's Submit then re-clicks that old button on every later Enter, invisibly. If you do not use uGUI selection for navigation, keep it empty (except while a text field is being edited).
- **A real mouse hovers everything under the ray**: the unit, the slot beneath it *and* the row or lane behind it. A single hover call reaches only the first handler it finds. If the game's play logic reads a hovered slot or container, mirror the focus onto those as well, through the game's own hover methods so its legality gates still apply.
- The game also moves focus on its own (default-item systems). Re-sync the hover when you *describe* the focused item, not only when you move it.

#### Activation ladder

Try these in order and use the first that works for that kind of object:

1. **The game's own handler** for the action (the method a click ultimately reaches). Best option: sounds, validation and side effects all run.
2. **A simulated pointer sequence**: `ExecuteEvents.ExecuteHierarchy` with `pointerDownHandler` now, then `pointerUpHandler` and `pointerClickHandler` **on the next frame** (a coroutine). Many buttons need the full sequence, and same-frame down+up is often ignored.
3. **The component's press/release path by reflection**, for objects that implement only pointer enter/exit and rely on a global "select" input while hovered (cards frequently work this way). Mirror what the game's own update loop does: set the hovered entity, press now, release next frame, and check on release that the game's own input polling has not already released it, so the action never fires twice.

**Ask before pressing.** If the game can refuse an action (a tutorial gate, an illegal choice), call its validation first and **speak the refusal**, including the game's prompt text when there is one. The game's own refusal feedback is usually a visual shake, which is silence to a blind player.

#### Drag and drop becomes pick up and place

Re-express dragging as a two-step keyboard action built on the game's own drag state:

- **Enter picks up** the focused card (put the game into its real "dragging this entity" state).
- Arrows move across the possible destinations. While a card is held, the focus description switches to a **target-aware** form: side, row and slot position, the occupant, and an explicit **"not a valid target"** whenever the game would refuse the drop. Ask the game's own legality check (the same call its release code makes); do not reimplement the rules.
- Focus may still rest on illegal cells, because browsing them is how the player learns the board. They must be *labelled*, or a card ends up somewhere the player never chose.
- **Enter places** (run the game's release path against the mirrored hover). **Escape cancels** and returns the card.
- Cards that need no target should play from anywhere without demanding a destination.

Right-click and hover-to-inspect map to a dedicated **inspect key** that opens the game's own inspect view when it has one (then route input to that view while it is open) or reads full details when it does not.

#### Polled waits and blocking overlays

- **Polled input waits**: cinematics and reveal sequences that spin on "is Select pressed?" where that action is bound only to mouse or gamepad. A keyboard player is hard-stuck there. Fix it with a small simulator: postfix-patch the game's single input-poll function so that, for a **two-frame armed window**, it reports the action as pressed. Arm it only from code that knows the game is waiting, **clear all hover state first** (a true "select" also clicks whatever is hovered), and respect the game's own input-disabled flag.
- **Blocking overlays that are not screens** (full-screen sequences layered over the active scene): a watcher detects them cheaply (a shared visual marker first, throttled object searches second) and **owns the keyboard while they run**. Enter answers the prompt; arrows are swallowed so they cannot drive the invisible screen underneath.

#### Virtual rows

Pages often contain content with **no navigable item at all**: statistics, logs, challenge lists, lore entries. Build virtual rows (spoken text, an optional activate action, an anchor transform) and merge them into the navigation order by position, skipping any entry that already has a real item so nothing is read twice.

- While focus sits on a virtual row, **clear the game's hover** so a game-side select cannot click a leftover object.
- Games render tables as a few large text blocks (a names column and a parallel values column, separated by line breaks). Split the blocks and re-pair the lines into one row per entry.

#### Focus recovery and quiet periods

- Overlays that close often leave the game with **no focus at all**. Provide `RequestRefocus()`: for about a second, wait until the default item is registered as available again (the close animation may still own the layer, and focusing too early just nulls the selection), then focus it and force a fresh announcement.
- The game moves focus by itself while it resolves actions (enemy turns, a played card). Track those changes **silently** (`SuppressFocusAnnouncements`) so focus chatter does not bury combat narration.
- Offer `SuppressFocusFor(seconds)` so an important announcement (an item gained, a menu opening) is not immediately talked over by the focus echo that follows it.
- Some items are destinations rather than places to browse; `ShouldAnnounceFocus(item)` lets a handler keep them silent.

#### Controls announce role, value and how to change it

| Control | Announcement | Keys |
|---|---|---|
| Button | label | Enter |
| Slider | label, "slider", value | Left/Right adjust by the game's own step (or 5-10% of the range); speak the new value |
| Toggle | label, "checkbox", checked / unchecked | Enter flips; speak the new state |
| Dropdown / selector | label, current choice | Left/Right cycle; speak the new choice |
| Tabs | label, "tab", selected state | Enter opens the tab's content; a back key returns to the tab list |

Label resolution order for unlabeled controls: the game's localization component, then a parent's title or label, then tooltip text, then a small known-name map, then the cleaned GameObject name.

#### Rewrite the game's mouse wording

Tutorials and help popups teach mouse controls ("right click a card", "drag it onto an enemy"). Read verbatim, they teach a blind player controls they do not have. Add a text pass that rewrites them into the mod's model: "right click" becomes the inspect key, "click" becomes Enter, "drag X onto Y" becomes "select X and place it on Y" (keep the verb form: "dragging" becomes "selecting … and placing"), and append a one-line keyboard how-to whenever a drag sentence was rewritten. Bound each pattern by sentence punctuation so a stray "to" in a later sentence cannot swallow text, and do it **per supported language**.

---

## 8. Concise focus + review buffers

### 8.1 FocusReadout

Readers for rich objects return three views of the same thing:

```csharp
public class FocusReadout
{
    public string Summary;                        // spoken on focus: "Name, cost, attack/health"
    public List<string> Details = new List<string>(); // one buffer item each: type, rarity, rules text, each keyword, upgrades, lore
    public string FullText;                       // everything in one string, for "re-read" and non-focus callers
}
```

**Card reading**, in this order: name; cost (including X/variable/unplayable, and *modified* cost, read from the state object rather than the definition); for units, attack/health/size or the game's equivalent; then details: card type, rarity, faction/class, rules text with sprite tags turned into words and dynamic numbers resolved, applied upgrades/enchantments, each referenced keyword with its explanation, and flavor text last (respect the game's own "show lore" preference if one exists). In hand, also say playability and why not (not enough energy, no valid target, no room).

The same split applies to shop items ("Name, price, can afford / not enough gold"), relics ("Artifact: Name"), units on the board, map nodes and rewards.

**Keyword explanations live only in buffer content, never in live announcements.** Do not build "explain it the first time only" tracking; it is unpredictable for the player and was removed from the reference mod. Give description builders a `bool includeKeywords` parameter: buffers pass true, hotkey reads and combat events pass false.

### 8.2 AnnouncementBuffer

A named list of strings with a cursor:

- Items are stored in **review order**: index 0 is the current/top item (the newest event, or the first detail line).
- The cursor starts at **-1** ("before the top"), so the first *next* press reads the top item.
- `MoveDeeper()` (Ctrl+Up) goes to older events / further detail lines; `MoveTowardTop()` (Ctrl+Down) goes back. **No wrap**; at either end, repeat the item or say "top"/"end".
- `Add(item)` inserts at 0, shifts the cursor so a player mid-review stays on the same item, and trims past `maxItems`.
- Optional `Refresher` (`Func<List<string>>`) rebuilds content from game state on demand; returning `null` marks the buffer unavailable. **Reset the cursor only when the content actually changed**; otherwise re-reading the same focus resets the player's position.
- `FollowLatest` (Events buffer): focusing the buffer returns the cursor to the newest item.

### 8.3 BufferManager

Owns the ordered buffer list. Ctrl+Up/Down move within the current buffer; Ctrl+Left/Right switch buffers, **skipping empty ones**, announcing "Name, N items". A good default set and order:

- Focus-fed: **UI** (details of whatever generic thing has focus), **Events** (every combat event, capped at around 200), **Card**, **Creature/Unit**, **Artifact/Relic**, **Reward**, **Story** (narrative event text)
- Battle-only, refresher-driven: **Hand**, **Board** (rows/floors/lanes), **Units**, **Resources**

Feed points: the focus poller (UI/Card/Artifact/Reward), target selection (Creature), the board review cursor (UI + Creature), the narrative screen patch (Story), and `LogEvent` (Events). **Clear focus-fed content on screen change and on battle exit**, or stale details will be read on the wrong screen.

---

## 9. Input

### 9.1 Hotkeys

One `InputInterceptor` MonoBehaviour reads keys in `Update()` and routes **by mode, in strict priority order**: help browser open, then Ctrl combos, then targeting mode, then review cursor, then plain hotkeys. Every modal mode your mod adds must claim its keys *before* lower layers see them.

Baseline key set (adapt letters to the game; all rebindable through config):

| Key | Action |
|---|---|
| F1 | Context help for the current screen, as a browsable list |
| one letter | Re-read the focused item (full text, not the concise summary) |
| one letter | Read all text on screen (a structured summary where a raw dump would be noise) |
| one letter each (battle) | Hand; board or current row; all units in detail; energy/resources; enemy intents |
| Ctrl+letter | Run-wide resources (gold, health, …), which **work on every screen**, so read them from save/run state, not a battle cache |
| Ctrl+arrows | Review buffers (or the map cursor on the map screen) |
| one letter | Cycle verbosity |

**Before choosing letters, read the game's default bindings from the extracted source/assets and list them in `CLAUDE.md`.** Sharing a letter with a game action that is inert on most screens is acceptable; never share with one that is disruptive (opens a pile, ends the turn, toggles speed). Do not take keys screen readers need (Insert/CapsLock combos, NumPad when used as the review cursor).

### 9.2 Suppression

When your mod claims a key, the game must not *also* act on it, or the player gets double actions and double announcements. The game's input code usually ignores modifiers, so Ctrl+Up still moves the game's selection.

- Find **every** input path (recon, section 1). Typically: (a) the EventSystem input module (`BaseInput.GetAxisRaw` / `GetButtonDown` for legacy input, or the UI input actions for the new Input System), and (b) the game's own control-mapping dispatcher. Prefix-patch each; when a claim is active, return the neutral value (`0f` / `false`) and skip the original.
- Suppress only what you claim: arrows while Ctrl is held; arrows + Submit + Cancel while a mod modal (help list, review cursor) is open; mapped letters for your Ctrl+letter combos while Ctrl is held. Leave gamepad and non-claimed keys alone.
- **Key off physical key state, not your mod's state flags**, for the press that *opens* a mode. Script execution order is undefined, so the game may process that key press before your `Update()` sets the flag.
- In tier B/C games the conflict runs the other way too: keys you use for navigation (Enter, Escape, arrows, letters) may be bound to game actions that fire *alongside* your handling. Check the game's bindings for each one, and either suppress the game's handling or make your own action idempotent with it (see the press/release guard in 7.4).
- When a mod modal closes on Escape/Enter, latch a `ClosedThisFrame` flag so the same key press does not also reach the game.

### 9.3 "Is this screen really in front?"

Mode gates like "plain arrow opens board review during battle" need a reliable answer to *is the battle screen frontmost and interactable right now*. Use **the game's own notion of interactability** (whatever its screen manager uses to decide which screen receives input). Two designs that fail in practice: a **denylist of overlay names** (you will miss one, and that screen loses its arrow keys), and **"top of the active-screen stack"** (hidden-but-still-registered screens such as a loading screen can sit on top forever). Your own `ScreenStateTracker` is fed by screen *open* patches and does not see screens close, so treat it as a fallback and patch close/hide where a stale value would hurt.

---

## 10. Battle

- **ManagerCache**: locate the game's managers once per battle (via a screen component's private fields, a service locator, or `FindObjectOfType`), cache `MethodInfo`s, and invalidate on battle exit. Give it an `IsInBattle` flag driven by verified start/stop patches, and **confirm the stop patch actually fires**; a wrong method name there is a silent no-op that leaves battle keys live on the reward screen.
- **Readers**: `HandReader` (index, name, cost, type, playability), `BoardReader` (per row/floor/lane: capacity, your units then enemies in a *consistent, documented* order, hazards/enchantments), `UnitReader` (stats, statuses with stacks, triggers/abilities, **intent / next action**, boss specifics), `ResourceReader`.
- **Event announcements** (`Patches/Combat/`): turn start/end, phase changes, draw/play/discard/exhaust/shuffle, damage (who, to whom, how much, remaining HP), death, spawn, movement, status add/remove, heal, buffs, relic triggers, player health damage, victory/defeat, enemy dialogue or barks (config-gated). Every one goes through `LogEvent` so it lands in the Events buffer.
- **Phantom events**: if the game previews outcomes by running real combat code, *every* combat patch must check the preview flag (and your own "targeting in progress" state) and stay silent. Centralize this in one `PreviewModeDetector`.
- **Floods**: a single card can generate a dozen events in one frame. Coalesce where it is natural ("3 enemies take 5 damage") and consider an end-of-turn summary. Keep the raw per-event lines in the buffer.
- **Internal names mislead.** Team enums, room/slot indices and "hero/monster" naming are frequently the reverse of what the player sees. Verify by observation, then document the mapping and the conversion formula once, in `CLAUDE.md`.
- **Targeting systems**: when a card needs a target, enter a mod-side targeting mode: arrows or number keys move between valid targets, announcing each (name, stats, position, and the previewed outcome if the game computes one), Enter confirms, Escape cancels. Drive the game's *own* selection/confirm methods. Poll game state while targeting, because the game changes the selected target by itself.
- **Board review cursor**: a virtual, read-only cursor over rows and units (for example, a plain arrow opens it in battle; arrows move; Enter reads full details; Escape closes). Re-read game state on every key press so it never goes stale. It feeds the UI/Creature buffers, and must auto-close when targeting starts, when the screen changes, and when the battle ends.

**Map review cursor**: same idea on the map screen, where Ctrl+arrows step through rings/columns and the nodes within them without moving the real selection. Build the node list from the **live map UI first** (it reflects exactly what a sighted player sees, including caps and special nodes) and fall back to run-state data when the UI is not loaded. Say what each node is, whether it is reachable from the current position, and which paths lead to it.

---

## 11. Text pipeline, keywords, localization

All speech passes through one cleanup function:

1. Convert inline icon tags (`<sprite name="X">`) to words, using the icon list from the asset export.
2. Strip rich text tags (`<b>`, `<color=…>`, `<size=…>`, `<nobr>`, …).
3. Resolve leaked localization keys (many games render a missing key in a recognizable wrapper pattern; detect it and localize the inner key).
4. In tier B/C games, rewrite mouse wording into the mod's keyboard model (end of section 7.4).
5. Collapse whitespace; drop empty results.

A `SplitIntoSpeechItems(text)` helper (sentence and line boundaries) turns any long text into buffer items and help entries.

**Localization**: find the game's single localize entry point and wrap it in a `LocalizationHelper.TryLocalize(key)` with cached reflection. Pattern: try `GetName()` / `GetDescription()` first; if the result still looks like a key, fetch the key getter and localize it; fall back to a cleaned type name. Your mod's *own* strings should be centralized too, so they can be translated later.

**KeywordManager**: build the keyword dictionary **from the game's localization at runtime**: status effects, triggers, card traits, whatever registries the game has. It then stays correct across languages and patches. Hardcode a small fallback dictionary only for mechanics the game never formally defines.

---

## 12. Help system

- `IHelpContext { ContextId, ContextName, Priority, IsActive(), GetHelpText() }`, one per screen or mode. `HelpSystem` picks the highest-priority active context; modes outrank screens (targeting > battle; dialog > everything).
- `ScreenStateTracker.CurrentScreen` (set by every screen patch) is the default `IsActive()` input; mod modes check their own state.
- F1 opens a **browsable list**: help text split into entries, Up/Down read one at a time, F1/Enter/Escape close. While open, the interceptor routes all input to it and suppression keeps those keys from the game.
- Every screen in `SCREENS.md` needs a help context. Help text lists the *game's* keys for that screen as well as the mod's.
- Each screen's open announcement ends with a one-line hint for first-time users; make hints suppressible through verbosity.

---

## 13. Configuration

BepInEx config file: verbosity level, every hotkey, braille on/off, per-category announcement toggles (damage, status, draws, dialogue, …), tutorial hints. Blind players edit this in a text editor; write clear descriptions, and document the file in the README.

---

## 14. Test and debug loop

There are no automated tests; the loop is build, launch, read the log, listen.

1. `dotnet build -c Release` (auto-deploys to the plugins folder).
2. Launch; check `BepInEx\LogOutput.log` for patch failures and exceptions first.
3. Walk the screen under test with a screen reader running (NVDA is free). With no screen reader, Prism falls back to a TTS backend, which is fine for smoke tests.
4. For an unreadable element, add temporary diagnostic logging on focus: the GameObject path, every component on it and on its parents, and all fields of the interesting component:

```csharp
foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
    Plugin.LogInfo($"  {f.Name} = {f.GetValue(component)?.GetType().Name ?? "null"}");
```

   Then go back to `game/` with those names. Remove or gate the logging afterwards.
5. You cannot hear the output. Treat the event log file and `LogOutput.log` as your ears, and ask the user to confirm what was actually spoken whenever timing or ordering matters.

---

## 15. Pitfalls checklist

- Patch target name guessed instead of read: silent no-op.
- Postfix order surprises when patched methods call each other; use a prefix to capture earlier state.
- Preview/simulation firing combat announcements.
- Game changes selection without a key press; poll state.
- Focus is on a child; the component is on a parent. A serialized reference is on a parent; the target is a child.
- TMP `.text` returning prefab placeholders; `GetParsedText()` being one frame late.
- Disabled tooltip providers still holding text.
- "Screens" that are really panels inside another screen; screens that stay registered after they hide.
- State tracker that only sees opens, never closes.
- Modifier keys ignored by the game's input layer; keys processed twice.
- The key that opens a mod mode racing the game's own handling of the same key.
- Internal naming reversed relative to what the player sees (teams, indices, floors).
- Per-frame reflection without caching.
- `interrupt = true` anywhere.
- Keyword explanations leaking into live announcements.
- Stale buffer content after a screen change.
- Navigation layer: focus moved but the game's hover did not (Enter hits something the player never heard); old hover left armed after a refused hover; uGUI selected object left armed after a simulated pointer-down; slot/lane under a focused unit never hovered; silent arrow keys on empty screens; focus left null after an overlay closes; scaled timers frozen by a paused `timeScale`; a polled "press Select" wait with no keyboard binding; tutorial text teaching mouse controls.
- Prism called off the main thread; `bool` marshalled as 4 bytes; wrong-bitness `prism.dll`; freeing library-owned strings.

---

## 16. Definition of done

- [ ] Every row in `SCREENS.md` is **verified**.
- [ ] A full run (start, battles, shop, event, boss, win *and* loss) is playable with the monitor off.
- [ ] Every hover-only piece of information has a keyboard path.
- [ ] Every interactive element announces name, role-relevant state and value.
- [ ] No double actions: claimed keys never reach the game.
- [ ] No mouse needed anywhere: every click, right-click, hover and drag has a keyboard path, and no cinematic or prompt waits on an input the keyboard cannot produce.
- [ ] What Enter acts on is always the item that was last announced; illegal targets are labelled as such.
- [ ] No arrow key is ever answered with silence, and screens without a dedicated handler still get the generic fallback.
- [ ] No phantom announcements during previews or targeting.
- [ ] Buffers: Events fills during combat; Card/Creature/Artifact/Reward/Story fill on focus; all clear on screen change.
- [ ] F1 help exists and is accurate for every screen and mode.
- [ ] Works with at least NVDA and with no screen reader (TTS fallback); recovers if the screen reader starts after the game.
- [ ] Log is free of patch failures and recurring exceptions.
- [ ] `CLAUDE.md`, `PATCH_TARGETS.md`, `SCREENS.md` and a player-facing README (install steps, full key list, config) are current.
- [ ] Release bundle: mod loader + plugin DLL + `prism.dll` (with Prism's `LICENSES` and `NOTICE`), no game code or assets.
