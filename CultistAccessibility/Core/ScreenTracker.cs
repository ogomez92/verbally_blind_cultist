using System;
using CultistAccessibility.Core.Buffers;
using UnityEngine.SceneManagement;

namespace CultistAccessibility.Core
{
    internal enum GameScreen
    {
        None,
        Logo,
        Quote,
        Menu,
        Tabletop,
        GameOver,
        NewGame,
        Crash
    }

    /// <summary>
    /// Derives the current screen from the loaded content scene every frame (not from patches), so it can
    /// never go stale. Scene names come from content/core/dicta/dicta.json (S1Logo ... S6NewGame) and
    /// StageHand's UHOSCENE constant (S7UhO).
    /// </summary>
    internal static class ScreenTracker
    {
        public static GameScreen Current { get; private set; } = GameScreen.None;
        public static GameScreen Previous { get; private set; } = GameScreen.None;
        public static float EnteredAt { get; private set; }

        public static event Action<GameScreen, GameScreen> ScreenChanged;

        public static void Update()
        {
            GameScreen detected = Detect();
            if (detected == Current) return;
            Previous = Current;
            Current = detected;
            EnteredAt = UnityEngine.Time.unscaledTime;
            BufferManager.ClearFocusFed();
            Plugin.LogInfo("Screen: " + Previous + " -> " + Current);
            try { ScreenChanged?.Invoke(Previous, Current); }
            catch (Exception ex) { Plugin.LogError("ScreenChanged handler failed: " + ex); }
        }

        public static float TimeOnScreen => UnityEngine.Time.unscaledTime - EnteredAt;

        private static GameScreen Detect()
        {
            GameScreen result = GameScreen.None;
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                GameScreen g = FromName(s.name);
                if (g != GameScreen.None) result = g;
            }
            return result;
        }

        private static GameScreen FromName(string name)
        {
            switch (name)
            {
                case "S1Logo": return GameScreen.Logo;
                case "S2Quote": return GameScreen.Quote;
                case "S3Menu": return GameScreen.Menu;
                case "S4Tabletop": return GameScreen.Tabletop;
                case "S5GameOver": return GameScreen.GameOver;
                case "S6NewGame": return GameScreen.NewGame;
                case "S7UhO": return GameScreen.Crash;
                default: return GameScreen.None;
            }
        }
    }
}
