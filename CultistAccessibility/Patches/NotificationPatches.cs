using System;
using System.Collections;
using System.Collections.Generic;
using CultistAccessibility.Core;
using HarmonyLib;
using SecretHistories.UI;
using UnityEngine;

namespace CultistAccessibility.Patches
{
    /// <summary>
    /// Reads the game's pop-up notifications (NotificationWindow): card mismatch messages, "can't merge",
    /// save errors, menu notifications. Show() is patched; the text is read one frame later because the
    /// menu sets the text after calling Show().
    /// </summary>
    internal static class NotificationPatches
    {
        // Same window and text within a moment: one Show() reported twice. Later repeats are read again,
        // because the menu notifier and the save error window are permanent windows that get reused.
        private static readonly Dictionary<int, KeyValuePair<string, float>> LastSpoken = new Dictionary<int, KeyValuePair<string, float>>();
        private const float RepeatWindow = 2f;

        /// <summary>The notification read last and when: the navigator does not read the same dialog again.</summary>
        public static int LastReadWindow { get; private set; }
        public static float LastReadAt { get; private set; } = -100f;

        public static void TryPatch(Harmony harmony)
        {
            PatchRegistry.Patch(harmony, typeof(NotificationPatches), "SecretHistories.UI.NotificationWindow", "Show", Type.EmptyTypes, postfix: nameof(ShowPostfix));
        }

        public static void ShowPostfix(NotificationWindow __instance)
        {
            try
            {
                if (!ModConfig.AnnounceNotifications.Value) return;
                var host = AccessibilityController.Instance;
                if (host != null) host.StartCoroutine(ReadNextFrame(__instance));
            }
            catch (Exception ex)
            {
                Plugin.LogError("Notification patch failed: " + ex.Message);
            }
        }

        private static IEnumerator ReadNextFrame(NotificationWindow window)
        {
            yield return null;
            if (window == null) yield break;
            string text;
            try
            {
                text = TextCleaner.Sentences(new[]
                {
                    TextCleaner.CleanMultiline(window.Title),
                    TextCleaner.CleanMultiline(window.Description),
                    TextCleaner.CleanMultiline(window.AdditionalText)
                });
            }
            catch
            {
                yield break;
            }
            if (string.IsNullOrEmpty(text)) yield break;
            int id = window.GetInstanceID();
            float now = Time.unscaledTime;
            if (LastSpoken.TryGetValue(id, out var last) && last.Key == text && now - last.Value < RepeatWindow) yield break;
            LastSpoken[id] = new KeyValuePair<string, float>(text, now);
            LastReadWindow = id;
            LastReadAt = now;
            if (LastSpoken.Count > 200) LastSpoken.Clear();
            Speech.SayEvent(text);
        }
    }
}
