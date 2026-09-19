using System;
using CultistAccessibility.Core.Buffers;
using CultistAccessibility.Help;
using CultistAccessibility.Navigation;
using CultistAccessibility.Tabletop;
using UnityEngine;

namespace CultistAccessibility.Core
{
    /// <summary>
    /// The per-frame router. Order: speech housekeeping, screen tracking, mod modals (help), global
    /// hotkeys and review buffers, then the navigator that owns the current screen.
    /// </summary>
    internal sealed class AccessibilityController : MonoBehaviour
    {
        public static AccessibilityController Instance { get; private set; }

        public UiNavigator Ui { get; } = new UiNavigator();
        public TabletopNavigator Tabletop { get; } = new TabletopNavigator();

        private void Awake()
        {
            Instance = this;
            ScreenTracker.ScreenChanged += OnScreenChanged;
        }

        private void OnDestroy()
        {
            ScreenTracker.ScreenChanged -= OnScreenChanged;
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            Speech.Shutdown();
        }

        private void OnScreenChanged(GameScreen from, GameScreen to)
        {
            HelpClosedOnScreenChange();
            MenuMessages.Reset();
            Patches.CardPatches.ClearPendingArrivals();
            Ui.Reset();
            Ui.ScreenSpaceOnly = to == GameScreen.Tabletop;
            Ui.PendingScreenTitle = Strings.ScreenName(to);
            Tabletop.OnScreenChanged(to);
            ScreenAnnouncer.OnScreenChanged(from, to, Ui);
        }

        private static void HelpClosedOnScreenChange()
        {
            if (HelpSystem.IsOpen) HelpSystem.Close();
        }

        private void Update()
        {
            try
            {
                Speech.Update();
                ScreenTracker.Update();
                ScreenAnnouncer.Update();
                Patches.CardPatches.FlushArrivals();
                InputGate.ArrowsClaimed = true;
                // The mod does not use uGUI selection; an object left selected (the game selects the options tab
                // itself) would also receive Enter as Submit and Arrow keys as Move from the input module.
                if (!KeyInput.TextFieldFocused && !InputGate.PassThrough) Navigation.UiActions.ClearSelection();
                Route();
            }
            catch (Exception ex)
            {
                Plugin.LogError("Controller update failed: " + ex);
            }
        }

        private void Route()
        {
            GameScreen screen = ScreenTracker.Current;

            // Background work that must run whatever owns the keyboard.
            if (screen == GameScreen.Tabletop) Tabletop.Tick();
            // After the menu's own announcement, so a save problem is heard right after "Main menu".
            if (screen == GameScreen.Menu && string.IsNullOrEmpty(Ui.PendingScreenTitle)) MenuMessages.Update();

            if (HelpSystem.IsOpen)
            {
                HelpSystem.HandleInput();
                return;
            }

            if (KeyInput.Ctrl && KeyInput.Shift && KeyInput.Pressed(UnityEngine.InputSystem.Key.D))
            {
                Plugin.LogInfo("==== Debug dump: screen " + screen + ", tabletop mode " + Tabletop.ModeName);
                Ui.DumpToLog();
                Tabletop.DumpToLog();
                Speech.Say("Debug information written to the log.");
                return;
            }

            if (DeveloperTools.HandleGlobalInput(screen)) return;

            bool typing = KeyInput.TextFieldFocused || InputGate.PassThrough;
            bool consumed = false;
            if (!typing)
            {
                if (BufferManager.HandleInput())
                {
                    consumed = true;
                }
                else if (KeyInput.Pressed(ModConfig.KeyHelp.Value))
                {
                    HelpSystem.Open();
                    return;
                }
                else if (KeyInput.Pressed(ModConfig.KeyCycleVerbosity.Value))
                {
                    ModConfig.CycleVerbosity();
                    Speech.Say(Strings.VerbosityIs(ModConfig.Level));
                    consumed = true;
                }
            }

            switch (screen)
            {
                case GameScreen.None:
                case GameScreen.Logo:
                    return;
                case GameScreen.Quote:
                    // Any key continues (the game handles it); nothing to navigate.
                    return;
                case GameScreen.Tabletop:
                    if (Tabletop.WantsUiNavigator())
                    {
                        Ui.ScreenSpaceOnly = true;
                        Tabletop.SuspendForUi();
                        Ui.Update(!consumed && !typing);
                    }
                    else
                    {
                        Ui.Reset();
                        Tabletop.Update(!consumed && !typing);
                    }
                    return;
                default:
                    Ui.Update(!consumed && !typing);
                    return;
            }
        }
    }
}
