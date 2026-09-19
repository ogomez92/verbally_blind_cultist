using System.Collections.Generic;
using CultistAccessibility.Core;

namespace CultistAccessibility.Help
{
    /// <summary>
    /// One help context per screen and mode. Game keys listed here come from the game's Keybindings
    /// (Default action map): Space pause, N normal speed, M fast forward, S start recipe, C collect all,
    /// Tab stack cards, 1/2/3 and Q/E zoom, Escape close window or open options.
    /// </summary>
    internal static class HelpContexts
    {
        private static string Key(UnityEngine.InputSystem.Key k) => k.ToString();

        private static string TabletopMode => Core.AccessibilityController.Instance?.Tabletop.ModeName ?? "";

        public static void RegisterAll()
        {
            HelpSystem.Register(new HelpContext("Logo and title quote", 10,
                () => ScreenTracker.Current == GameScreen.Logo || ScreenTracker.Current == GameScreen.Quote,
                () => new[] { "Press any key to continue to the main menu." }));

            HelpSystem.Register(new HelpContext("Main menu", 10,
                () => ScreenTracker.Current == GameScreen.Menu,
                () => new[]
                {
                    "Up and down arrows move between buttons and text. Home and End jump to the first and last item.",
                    "Enter activates the focused button. Left and right arrows change sliders and choices.",
                    "Escape closes an open panel such as settings, credits or mods.",
                    "Continue resumes your saved game. Begin Game starts a new one. Purge Save deletes your save after a confirmation.",
                    "DLC and Mods lists the extra legacies you can start and the installed mods.",
                    "In settings, choose a tab with Enter, then move down to its sliders. A key binding field waits for the new key after Enter.",
                    Key(ModConfig.KeyReadScreen.Value) + " reads everything on the screen. " + Key(ModConfig.KeyRepeat.Value) + " repeats the focused item."
                }));

            HelpSystem.Register(new HelpContext("Choosing a legacy", 10,
                () => ScreenTracker.Current == GameScreen.NewGame,
                () => new[]
                {
                    "Up and down arrows move between the legacies on offer, their descriptions and the buttons.",
                    "Enter on a legacy selects it; its description is then read in the text rows below it.",
                    "Start New Game begins a new life with the selected legacy. Back to Menu returns to the main menu."
                }));

            HelpSystem.Register(new HelpContext("The end", 10,
                () => ScreenTracker.Current == GameScreen.GameOver,
                () => new[]
                {
                    "The ending text was read when the screen opened. Up and down arrows read it again, line by line.",
                    "Begin Another Descent chooses a new legacy. Main Menu returns to the menu.",
                    "The full ending text is also in the Story review buffer: Control plus left or right arrow to reach it."
                }));

            HelpSystem.Register(new HelpContext("Error screen", 10,
                () => ScreenTracker.Current == GameScreen.Crash,
                () => new[] { "The game hit an error. Log Files opens the folder with the game's logs. Exit quits the game." }));

            HelpSystem.Register(new HelpContext("Options menu", 25,
                () => ScreenTracker.Current == GameScreen.Tabletop && (Core.AccessibilityController.Instance?.Tabletop.WantsUiNavigator() ?? false),
                () => new[]
                {
                    "The game is paused while the options are open. Up and down arrows move, Enter activates, Escape closes the options.",
                    "The first items are the tabs: Enter on a tab shows its settings below it. Left and right arrows change a slider.",
                    "Enter on a key binding waits for the new key.",
                    "Resume closes the options. Save and Exit saves and returns to the main menu. Restart asks for confirmation, then restarts this life from its beginning.",
                    "Browse Files opens the game's save folder in Windows."
                }));

            HelpSystem.Register(new HelpContext("The table", 20,
                () => ScreenTracker.Current == GameScreen.Tabletop && TabletopMode == "Board",
                () => new[]
                {
                    "The table has three groups: Verbs, Cards and Controls. Left and right arrows switch group, up and down move inside it. Home and End jump to the first and last item.",
                    "Enter on a verb opens its window. Enter on a card lists the verbs that can take it; choose one with Enter to put the card in.",
                    "Controls holds your status, the stack cards button, the speed buttons, options and your character's name.",
                    Key(ModConfig.KeyInspect.Value) + " reads everything about the focused item: description, aspects and their meaning, slots. The lines also go to the Details buffer.",
                    Key(ModConfig.KeyRepeat.Value) + " repeats the focused item. " + Key(ModConfig.KeyReadScreen.Value) + " reads the whole group.",
                    Key(ModConfig.KeyStatus.Value) + " reads your character and the status bar: health, passion, reason, funds.",
                    Key(ModConfig.KeyTimers.Value) + " reads the game speed, every busy verb with its time left, and cards that are decaying.",
                    Key(ModConfig.KeyNextCompleted.Value) + " jumps to the next verb with results waiting.",
                    "Game keys: Space pauses and unpauses, N normal speed, M fast forward, Tab stacks cards, Escape opens the options menu.",
                    "Time runs while the game is not paused. Pausing with Space is the safest way to think."
                }));

            HelpSystem.Register(new HelpContext("A verb window", 30,
                () => ScreenTracker.Current == GameScreen.Tabletop && TabletopMode == "Window",
                () => new[]
                {
                    "Up and down arrows move through the window: the story text, the slots, Start, results and Collect all.",
                    "Enter on a slot lists the cards on the table that fit it. Choose one with Enter. If the slot is full, the first choice takes its card out.",
                    Key(ModConfig.KeyEmptySlot.Value) + " or Backspace on a slot returns its card to the table.",
                    "When cards are placed the window's prediction changes; the new recipe name is read, with 'ready to start' when Start will work.",
                    "Enter on Start, or the game key S, begins the recipe. Enter on a result takes that card to the table; Collect all, or the game key C, takes them all.",
                    "Enter on the story text reads it in full. Page up and page down, or left and right on the story text, turn the pages of a long story.",
                    "Escape closes the window; cards left in slots of an unstarted verb return to the table."
                }));

            HelpSystem.Register(new HelpContext("The Mansus", 40,
                () => ScreenTracker.Current == GameScreen.Tabletop && TabletopMode == "Mansus",
                () => new[]
                {
                    "You are dreaming in the Mansus. Face-down cards wait at the places you can reach.",
                    "Up and down arrows move between them. Enter turns a card over; the others fade away.",
                    "Enter again takes the revealed card back with you and the dream ends."
                }));

            HelpSystem.Register(new HelpContext("Choosing from a list", 50,
                () => ScreenTracker.Current == GameScreen.Tabletop && TabletopMode == "picker",
                () => new[]
                {
                    "Up and down arrows move through the choices, Enter picks one, Escape cancels.",
                    Key(ModConfig.KeyInspect.Value) + " reads everything about the focused choice."
                }));
        }

        public static IEnumerable<string> GlobalLines()
        {
            return new[]
            {
                "Keys that work everywhere:",
                "F1 opens this help. " + Key(ModConfig.KeyCycleVerbosity.Value) + " changes how much is said.",
                "Control plus up and down arrows read the review buffers; control plus left and right switch buffer: Events, Details, Story, Verbs, Table and Status.",
                "Settings, including every key, are in BepInEx, config, accessibility.cultistsimulator.screenreader.cfg."
            };
        }
    }
}
