using System.Collections.Generic;
using CultistAccessibility.Core;

namespace CultistAccessibility.Help
{
    /// <summary>
    /// One help context per screen and mode; the texts are in Lang/*.txt. Game keys named there come from the
    /// game's Keybindings (Default action map): Space pause, N normal speed, M fast forward, S start recipe,
    /// C collect all, Tab stack cards, 1/2/3 and Q/E zoom, Escape close window or open options.
    /// </summary>
    internal static class HelpContexts
    {
        private static string TabletopMode => Core.AccessibilityController.Instance?.Tabletop.ModeName ?? "";

        /// <summary>The key names every help line may use, in the order documented at the top of Lang/en.txt.</summary>
        private static object[] Keys()
        {
            return new object[]
            {
                Strings.KeyName(ModConfig.KeyReadScreen.Value), Strings.KeyName(ModConfig.KeyRepeat.Value),
                Strings.KeyName(ModConfig.KeyInspect.Value), Strings.KeyName(ModConfig.KeyStatus.Value),
                Strings.KeyName(ModConfig.KeyTimers.Value), Strings.KeyName(ModConfig.KeyNextCompleted.Value),
                Strings.KeyName(ModConfig.KeyEmptySlot.Value), Strings.KeyName(ModConfig.KeyCycleVerbosity.Value),
                Strings.KeyName(ModConfig.KeyHelp.Value)
            };
        }

        /// <summary>Title and lines are Help.&lt;id&gt;.Title and Help.&lt;id&gt;.1, .2... of the current language.</summary>
        private static void Register(string id, int priority, System.Func<bool> isActive)
        {
            HelpSystem.Register(new HelpContext(() => Loc.Get("Help." + id + ".Title"), priority, isActive,
                () => Loc.Lines("Help." + id, Keys())));
        }

        public static void RegisterAll()
        {
            Register("Logo", 10, () => ScreenTracker.Current == GameScreen.Logo || ScreenTracker.Current == GameScreen.Quote);
            Register("Menu", 10, () => ScreenTracker.Current == GameScreen.Menu);
            Register("NewGame", 10, () => ScreenTracker.Current == GameScreen.NewGame);
            Register("GameOver", 10, () => ScreenTracker.Current == GameScreen.GameOver);
            Register("Crash", 10, () => ScreenTracker.Current == GameScreen.Crash);
            Register("Options", 25, () => ScreenTracker.Current == GameScreen.Tabletop
                && (Core.AccessibilityController.Instance?.Tabletop.WantsUiNavigator() ?? false));
            Register("Board", 20, () => ScreenTracker.Current == GameScreen.Tabletop && TabletopMode == "Board");
            Register("Window", 30, () => ScreenTracker.Current == GameScreen.Tabletop && TabletopMode == "Window");
            Register("Mansus", 40, () => ScreenTracker.Current == GameScreen.Tabletop && TabletopMode == "Mansus");
            Register("Picker", 50, () => ScreenTracker.Current == GameScreen.Tabletop && TabletopMode == "picker");
        }

        public static IEnumerable<string> GlobalLines()
        {
            return Loc.Lines("Help.Global", Keys());
        }
    }
}
