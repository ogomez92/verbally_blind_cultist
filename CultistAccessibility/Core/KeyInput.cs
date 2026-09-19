using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CultistAccessibility.Core
{
    /// <summary>
    /// Keyboard polling on the new Input System (the game uses it; legacy Input is not reliable here).
    /// Uses unscaled time so repeat keeps working while the game is paused.
    /// </summary>
    internal static class KeyInput
    {
        private const float InitialRepeatDelay = 0.45f;
        private const float RepeatInterval = 0.08f;
        private static readonly Dictionary<Key, float> NextRepeat = new Dictionary<Key, float>();

        private static Keyboard Kb => Keyboard.current;

        public static bool Ctrl => Kb != null && (Kb.leftCtrlKey.isPressed || Kb.rightCtrlKey.isPressed);
        public static bool Shift => Kb != null && (Kb.leftShiftKey.isPressed || Kb.rightShiftKey.isPressed);
        public static bool Alt => Kb != null && (Kb.leftAltKey.isPressed || Kb.rightAltKey.isPressed);

        public static bool Pressed(Key key)
        {
            if (Kb == null || key == Key.None) return false;
            try { return Kb[key].wasPressedThisFrame; }
            catch { return false; }
        }

        public static bool Held(Key key)
        {
            if (Kb == null || key == Key.None) return false;
            try { return Kb[key].isPressed; }
            catch { return false; }
        }

        /// <summary>True on the first press, then again after a delay while held.</summary>
        public static bool Repeat(Key key)
        {
            if (Kb == null || key == Key.None) return false;
            float now = Time.unscaledTime;
            var control = Kb[key];
            if (control.wasPressedThisFrame)
            {
                NextRepeat[key] = now + InitialRepeatDelay;
                return true;
            }
            if (control.isPressed && NextRepeat.TryGetValue(key, out float next) && now >= next)
            {
                NextRepeat[key] = now + RepeatInterval;
                return true;
            }
            return false;
        }

        /// <summary>Plain press of a configured hotkey with no Ctrl/Alt held.</summary>
        public static bool Hotkey(Key key) => !Ctrl && !Alt && Pressed(key);

        public static bool AnyKeyPressed => Kb != null && Kb.anyKey.wasPressedThisFrame;

        /// <summary>True while a text input field has keyboard focus: letter hotkeys must stay quiet.</summary>
        public static bool TextFieldFocused
        {
            get
            {
                var es = EventSystem.current;
                if (es == null || es.currentSelectedGameObject == null) return false;
                var tmp = es.currentSelectedGameObject.GetComponent<TMP_InputField>();
                if (tmp != null && tmp.isFocused) return true;
                var legacy = es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>();
                return legacy != null && legacy.isFocused;
            }
        }
    }
}
