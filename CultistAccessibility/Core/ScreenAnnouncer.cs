using System.Collections.Generic;
using CultistAccessibility.Core.Buffers;
using CultistAccessibility.Navigation;
using SecretHistories.Entities;
using SecretHistories.UI;
using TMPro;
using UnityEngine;

namespace CultistAccessibility.Core
{
    /// <summary>
    /// One-off announcements when a screen opens, for screens whose content is text rather than controls
    /// (logo, title quote, ending). Screens with controls are announced by the navigators.
    /// </summary>
    internal static class ScreenAnnouncer
    {
        private static GameScreen _pending = GameScreen.None;

        public static void OnScreenChanged(GameScreen from, GameScreen to, UiNavigator ui)
        {
            _pending = to;
            switch (to)
            {
                case GameScreen.Logo:
                    Speech.Say(Strings.ScreenName(to));
                    _pending = GameScreen.None;
                    break;
                case GameScreen.GameOver:
                    ui.PendingScreenTitle = BuildEndingText();
                    _pending = GameScreen.None;
                    break;
                case GameScreen.Tabletop:
                    // The tabletop navigator announces itself once the table is laid out.
                    ui.PendingScreenTitle = null;
                    _pending = GameScreen.None;
                    break;
            }
        }

        /// <summary>Called every frame; waits a moment so Babelfish has localised the texts.</summary>
        public static void Update()
        {
            if (_pending != GameScreen.Quote) return;
            if (ScreenTracker.TimeOnScreen < 0.3f) return;
            _pending = GameScreen.None;
            var parts = new List<string> { Strings.ScreenName(GameScreen.Quote) };
            foreach (var t in Object.FindObjectsOfType<TextMeshProUGUI>())
            {
                if (!t.isActiveAndEnabled) continue;
                if (t.gameObject.scene.name != "S2Quote") continue;
                if (t.canvas == null || !t.canvas.isActiveAndEnabled) continue;
                string s = TextCleaner.CleanMultiline(t.text);
                if (TextCleaner.IsPlaceholder(s)) continue;
                parts.Add(s);
            }
            parts.Add(Strings.PressAnyKey);
            Speech.Say(TextCleaner.Sentences(parts));
        }

        private static string BuildEndingText()
        {
            try
            {
                Ending ending = Watchman.Get<Stable>()?.Protag()?.EndingTriggered;
                if (ending == null || !ending.IsValid()) ending = Ending.DefaultEnding();
                string label = TextCleaner.Clean(ending.Label);
                string desc = TextCleaner.CleanMultiline(ending.Description);
                BufferManager.AddStory(label + ". " + desc);
                return TextCleaner.Sentences(new[] { Strings.ScreenName(GameScreen.GameOver), label, desc });
            }
            catch
            {
                return Strings.ScreenName(GameScreen.GameOver);
            }
        }
    }
}
