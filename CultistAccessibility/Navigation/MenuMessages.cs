using System;
using System.Collections.Generic;
using CultistAccessibility.Core;
using SecretHistories.Infrastructure;
using TMPro;
using UnityEngine;

namespace CultistAccessibility.Navigation
{
    /// <summary>
    /// The main menu's save and safe-mode messages (MenuScreenController.brokenSaveMessage, suspiciousSaveMessage,
    /// noBackupSaveMessage, safeModeMessage under HintsHolder) are large panels a sighted player sees at once.
    /// Each one is spoken when it becomes visible; the navigator reaches its text and buttons with the arrows.
    /// </summary>
    internal static class MenuMessages
    {
        private static readonly HashSet<int> Announced = new HashSet<int>();
        private static MenuScreenController _menu;
        private static float _nextCheck;

        public static void Reset()
        {
            Announced.Clear();
            _menu = null;
            _nextCheck = 0f;
        }

        public static void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.3f;
            try
            {
                if (_menu == null) _menu = UnityEngine.Object.FindObjectOfType<MenuScreenController>();
                if (_menu == null) return;
                Check(_menu.brokenSaveMessage);
                Check(_menu.suspiciousSaveMessage);
                Check(_menu.noBackupSaveMessage);
                Check(_menu.safeModeMessage);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("MenuMessages failed: " + ex.Message);
            }
        }

        private static void Check(GameObject message)
        {
            if (message == null) return;
            int id = message.GetInstanceID();
            if (!message.activeInHierarchy)
            {
                Announced.Remove(id);
                return;
            }
            if (!Announced.Add(id)) return;
            var parts = new List<string>();
            foreach (var t in message.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                // Button labels are read by the navigator as buttons; only the title and message are spoken here.
                if (t.GetComponentInParent<UnityEngine.UI.Button>() != null) continue;
                string s = TextCleaner.Clean(t.text);
                if (!string.IsNullOrEmpty(s)) parts.Add(s);
            }
            if (parts.Count > 0) Speech.SayEvent(string.Join(". ", parts));
        }
    }
}
