using System;
using CultistAccessibility.Core;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace CultistAccessibility.Patches
{
    /// <summary>
    /// Suppression of the game's own key handlers when the mod claims the key.
    /// UIController (tabletop) and MenuScreenController receive Input System action callbacks via
    /// KeyboundGameEvent UnityEvents; a prefix returning false skips the game's handling.
    /// Arrow keys (camera truck/pedestal) belong to the mod's navigation; everything else is blocked only
    /// while a mod modal (help, picker) is open.
    /// </summary>
    internal static class InputBlockPatches
    {
        private static readonly string[] HotkeyHandlers =
        {
            "Input_Zoom_Key", "Input_ZoomClose", "Input_ZoomMid", "Input_ZoomFar", "Input_Pause",
            "Input_NormalSpeed", "Input_FastSpeed", "Input_Slower", "Input_Faster", "Input_GroupAllStacks",
            "Input_StartRecipe", "Input_NextComplete", "Input_CollectAll", "Input_Abort"
        };

        public static void TryPatch(Harmony harmony)
        {
            var ctx = new[] { typeof(InputAction.CallbackContext) };
            const string ui = "SecretHistories.Infrastructure.UIController";
            PatchRegistry.Patch(harmony, typeof(InputBlockPatches), ui, "Input_Truck_Key", ctx, prefix: nameof(CameraPrefix));
            PatchRegistry.Patch(harmony, typeof(InputBlockPatches), ui, "Input_Pedestal_Key", ctx, prefix: nameof(CameraPrefix));
            foreach (var name in HotkeyHandlers)
                PatchRegistry.Patch(harmony, typeof(InputBlockPatches), ui, name, ctx, prefix: nameof(HotkeyPrefix));
            PatchRegistry.Patch(harmony, typeof(InputBlockPatches), "SecretHistories.Infrastructure.MenuScreenController", "OnAbort", Type.EmptyTypes, prefix: nameof(HotkeyPrefix));
            PatchRegistry.Patch(harmony, typeof(InputBlockPatches), ui, "Input_StartRecipe", ctx, prefix: nameof(CaptureOpenVerb), postfix: nameof(StartFeedback));
            PatchRegistry.Patch(harmony, typeof(InputBlockPatches), ui, "Input_CollectAll", ctx, prefix: nameof(CaptureOpenVerb), postfix: nameof(CollectFeedback));
        }

        /// <summary>The game's S and C keys do nothing, silently, when there is nothing to start or collect: say so.</summary>
        /// <remarks>State is captured before the game acts (Inchoate = no verb window open).</remarks>
        public static void CaptureOpenVerb(out SecretHistories.Enums.StateEnum __state)
        {
            __state = SecretHistories.Enums.StateEnum.Inchoate;
            try
            {
                var s = CultistAccessibility.Tabletop.GameAccess.OpenSituation();
                if (s != null) __state = s.StateIdentifier;
            }
            catch { }
        }

        private static bool GameWouldIgnore()
        {
            try
            {
                if (InputGate.BlockGameHotkeys) return true;
                var nexus = SecretHistories.UI.Watchman.Get<SecretHistories.Infrastructure.LocalNexus>();
                if (nexus != null && nexus.PlayerInputDisabled()) return true;
                var heart = SecretHistories.UI.Watchman.Get<Heart>();
                return heart != null && heart.IsForcePaused();
            }
            catch
            {
                return true;
            }
        }

        public static void StartFeedback(SecretHistories.Enums.StateEnum __state)
        {
            try
            {
                if (GameWouldIgnore() || KeyInput.TextFieldFocused) return;
                var now = CultistAccessibility.Tabletop.GameAccess.OpenSituation();
                if (__state == SecretHistories.Enums.StateEnum.Inchoate) Speech.Say(Strings.NoWindowOpen);
                else if (__state == SecretHistories.Enums.StateEnum.Unstarted && now != null && now.StateIdentifier == SecretHistories.Enums.StateEnum.Unstarted)
                    Speech.Say(Strings.CannotStart);
            }
            catch { }
        }

        public static void CollectFeedback(SecretHistories.Enums.StateEnum __state)
        {
            try
            {
                if (GameWouldIgnore() || KeyInput.TextFieldFocused) return;
                if (__state == SecretHistories.Enums.StateEnum.Inchoate) Speech.Say(Strings.NoWindowOpen);
                else if (__state != SecretHistories.Enums.StateEnum.Complete) Speech.Say(Strings.NothingToCollect);
                else Speech.Say(Strings.CollectedToTable);
            }
            catch { }
        }

        public static bool CameraPrefix()
        {
            try { return !InputGate.BlockCameraKeys; }
            catch { return true; }
        }

        public static bool HotkeyPrefix()
        {
            try { return !InputGate.BlockGameHotkeys; }
            catch { return true; }
        }
    }
}
