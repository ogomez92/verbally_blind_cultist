using System;
using CultistAccessibility.Core;
using CultistAccessibility.Tabletop;
using HarmonyLib;
using SecretHistories.Constants;
using SecretHistories.Enums;

namespace CultistAccessibility.Patches
{
    /// <summary>
    /// Announces pause / normal / fast when the effective game speed changes (Heart.RespondToSpeedControlCommand
    /// receives every speed command: keys, buttons, force-pauses from menus and the Mansus).
    /// </summary>
    internal static class SpeedPatches
    {
        public static void TryPatch(Harmony harmony)
        {
            PatchRegistry.Patch(harmony, typeof(SpeedPatches), "Heart", "RespondToSpeedControlCommand", new[] { typeof(SpeedControlEventArgs) },
                prefix: nameof(Prefix), postfix: nameof(Postfix));
            PatchRegistry.Patch(harmony, typeof(SpeedPatches), "SecretHistories.Infrastructure.GameGateway", "EndGame",
                new[] { typeof(SecretHistories.Entities.Ending), typeof(SecretHistories.UI.Token) }, prefix: nameof(EndGamePrefix));
        }

        /// <summary>The ending sequence (camera zoom, effect, fade) runs for several seconds before the ending screen.</summary>
        public static void EndGamePrefix()
        {
            try { Speech.SayEvent(Strings.LifeEnding); }
            catch { }
        }

        public static void Prefix(Heart __instance, out GameSpeed __state)
        {
            __state = GameSpeed.DeferToNextLowestCommand;
            try { __state = __instance.GetEffectiveGameSpeed(); }
            catch { }
        }

        public static void Postfix(Heart __instance, GameSpeed __state)
        {
            try
            {
                if (!ModConfig.AnnounceSpeedChanges.Value) return;
                if (ScreenTracker.Current != GameScreen.Tabletop) return;
                GameSpeed now = __instance.GetEffectiveGameSpeed();
                if (now == __state) return;
                string name = StatusReader.SpeedName(now);
                if (!string.IsNullOrEmpty(name)) Speech.SayEvent(name);
            }
            catch (Exception ex)
            {
                Plugin.LogError("Speed patch failed: " + ex.Message);
            }
        }
    }
}
