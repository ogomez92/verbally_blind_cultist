using CultistAccessibility.Core;
using SecretHistories.Infrastructure;
using UnityEngine;

namespace CultistAccessibility.Navigation
{
    /// <summary>Per-screen knowledge the generic navigator cannot infer, such as the main button to focus first.</summary>
    internal static class ScreenHints
    {
        public static GameObject DefaultFocusObject()
        {
            try
            {
                if (ScreenTracker.Current == GameScreen.Menu)
                {
                    var menu = Object.FindObjectOfType<MenuScreenController>();
                    if (menu == null) return null;
                    if (menu.continueGameButton != null && menu.continueGameButton.gameObject.activeInHierarchy) return menu.continueGameButton.gameObject;
                    if (menu.newGameButton != null && menu.newGameButton.gameObject.activeInHierarchy) return menu.newGameButton.gameObject;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Screens whose one panel is a message that must be heard on arrival, whatever its length: the error
        /// screen (S7UhO) explains what happened and where the log files are.
        /// </summary>
        public static bool ReadWholeBodyOnOpen => ScreenTracker.Current == GameScreen.Crash;
    }
}
