using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CultistAccessibility.Core;
using HarmonyLib;
using SecretHistories;
using SecretHistories.Assets.Scripts.Application.UI;
using SecretHistories.Constants;
using SecretHistories.Entities;
using SecretHistories.Enums.UI;
using SecretHistories.Infrastructure.Modding;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CultistAccessibility.Navigation
{
    /// <summary>
    /// Text extraction for uGUI items. Tries game-specific readers first (most specific wins),
    /// then generic label handling, then the cleaned GameObject name, so something is always spoken.
    /// Every label includes role and state: "checked", "selected", "unavailable", slider values.
    /// </summary>
    internal static class UiReader
    {
        private static readonly AccessTools.FieldRef<SliderSettingControl, TextMeshProUGUI> SliderHintRef =
            SafeFieldRef<SliderSettingControl, TextMeshProUGUI>("SliderHint");
        private static readonly AccessTools.FieldRef<SliderSettingControl, TextMeshProUGUI> SliderValueRef =
            SafeFieldRef<SliderSettingControl, TextMeshProUGUI>("SliderValueLabel");
        private static readonly AccessTools.FieldRef<OptionsPanelTab, TMP_Text> TabTextRef =
            SafeFieldRef<OptionsPanelTab, TMP_Text>("TabText");
        private static readonly AccessTools.FieldRef<AchievementCategoryTab, TMP_Text> AchTabTextRef =
            SafeFieldRef<AchievementCategoryTab, TMP_Text>("tabText");
        private static readonly AccessTools.FieldRef<ProemSlot, TextMeshProUGUI> ProemLabelRef =
            SafeFieldRef<ProemSlot, TextMeshProUGUI>("_label");
        private static readonly AccessTools.FieldRef<ProemSlot, TextMeshProUGUI> ProemDescRef =
            SafeFieldRef<ProemSlot, TextMeshProUGUI>("_desc");
        private static readonly AccessTools.FieldRef<NewGameScreenController, List<Legacy>> LegaciesRef =
            SafeFieldRef<NewGameScreenController, List<Legacy>>("AvailableLegaciesForEnding");

        internal static AccessTools.FieldRef<T, F> SafeFieldRef<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception ex)
            {
                Plugin.LogWarning("Field " + typeof(T).Name + "." + name + " not found: " + ex.Message);
                return null;
            }
        }

        private static readonly AccessTools.FieldRef<NewGameScreenController, int> SelectedLegacyRef =
            SafeFieldRef<NewGameScreenController, int>("selectedLegacy");

        /// <summary>The legacy offered by a toggle on the new-game screen, or null.</summary>
        private static Legacy LegacyForToggle(Toggle toggle, out bool isSelected)
        {
            isSelected = false;
            var newGame = UnityEngine.Object.FindObjectOfType<NewGameScreenController>();
            if (newGame == null || newGame.legacyButtons == null || toggle == null) return null;
            int idx = Array.IndexOf(newGame.legacyButtons, toggle);
            if (idx < 0) return null;
            if (SelectedLegacyRef != null) isSelected = SelectedLegacyRef(newGame) == idx;
            List<Legacy> legacies = LegaciesRef != null ? LegaciesRef(newGame) : null;
            return legacies != null && idx < legacies.Count ? legacies[idx] : null;
        }

        /// <summary>Longer text that belongs to a control (a legacy's description), read on activation and in details.</summary>
        public static string GetActivationText(UiItem item)
        {
            try
            {
                if (item?.Selectable is Toggle toggle)
                {
                    var legacy = LegacyForToggle(toggle, out _);
                    if (legacy != null) return TextCleaner.CleanMultiline(legacy.Description);
                }
                var mod = item?.Go != null ? item.Go.GetComponentInParent<ModEntry>() : null;
                if (mod != null) return TextCleaner.CleanMultiline(ReadText(mod.description));
            }
            catch { }
            return "";
        }

        private static readonly System.Reflection.PropertyInfo CurrentTabProp =
            AccessTools.Property(typeof(OptionsPanel), "currentTab");

        private static readonly AccessTools.FieldRef<AchievementEntry, TextMeshProUGUI> AchTitleRef =
            SafeFieldRef<AchievementEntry, TextMeshProUGUI>("title");
        private static readonly AccessTools.FieldRef<AchievementEntry, TextMeshProUGUI> AchDescRef =
            SafeFieldRef<AchievementEntry, TextMeshProUGUI>("description");
        private static readonly AccessTools.FieldRef<AchievementEntry, TextMeshProUGUI> AchTimeRef =
            SafeFieldRef<AchievementEntry, TextMeshProUGUI>("unlockTime");

        /// <summary>One line per achievement: title, unlocked date or "locked", description.</summary>
        public static string AchievementText(AchievementEntry entry)
        {
            if (entry == null) return "";
            string title = AchTitleRef != null ? TextCleaner.Clean(ReadText(AchTitleRef(entry))) : "";
            string desc = AchDescRef != null ? TextCleaner.CleanMultiline(ReadText(AchDescRef(entry))) : "";
            var time = AchTimeRef != null ? AchTimeRef(entry) : null;
            string state = time != null && time.gameObject.activeInHierarchy
                ? Strings.AchievementUnlocked(TextCleaner.Clean(ReadText(time)))
                : Strings.AchievementLocked;
            return TextCleaner.Join(title, state, desc);
        }

        /// <summary>Texts that are already spoken as part of a control's label (setting hint and value labels).</summary>
        public static bool IsPartOfCompositeControl(GameObject go)
        {
            return go.GetComponentInParent<AchievementEntry>() != null
                || go.GetComponentInParent<SliderSettingControl>() != null
                || go.GetComponentInParent<KeybindSettingControl>() != null
                || go.GetComponentInParent<MenuLegacyStartEntry>() != null
                || go.GetComponentInParent<ModEntry>() != null
                || go.GetComponentInParent<LanguageChoice>() != null;
        }

        private static bool IsCurrentOptionsTab(OptionsPanelTab tab)
        {
            try
            {
                var panel = tab.GetComponentInParent<OptionsPanel>();
                if (panel == null || CurrentTabProp == null) return false;
                return ReferenceEquals(CurrentTabProp.GetValue(panel, null), tab);
            }
            catch
            {
                return false;
            }
        }

        public static string GetLabel(UiItem item)
        {
            try
            {
                string text = GetLabelInner(item);
                if (string.IsNullOrWhiteSpace(text)) text = CleanName(item.Go != null ? item.Go.name : "");
                return text;
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("GetLabel failed: " + ex.Message);
                return item.Go != null ? CleanName(item.Go.name) : "";
            }
        }

        private static string GetLabelInner(UiItem item)
        {
            if (item.Kind == UiItemKind.Custom) return item.CustomLabel?.Invoke() ?? "";
            if (item.Kind == UiItemKind.Text)
                return TextCleaner.Join(TextCleaner.CleanMultiline(ReadText(item.Text)), FirstLink(item) != null ? Strings.RoleLink : "");

            GameObject go = item.Go;
            string state = StateSuffix(item);

            // 1. Settings controls.
            var slider = go.GetComponentInParent<SliderSettingControl>();
            if (slider != null && item.Selectable is Slider)
            {
                string hint = SliderHintRef != null ? ReadText(SliderHintRef(slider)) : "";
                string value = SliderValueRef != null ? ReadText(SliderValueRef(slider)) : "";
                return TextCleaner.Join(hint, value, Strings.RoleSlider, state);
            }
            var keybind = go.GetComponentInParent<KeybindSettingControl>();
            if (keybind != null)
            {
                string action = ReadText(keybind.ActionLabel);
                string binding = keybind.keybindingInputField != null ? keybind.keybindingInputField.text : "";
                if (string.IsNullOrWhiteSpace(TextCleaner.Clean(binding))) binding = Strings.NoKeyBound;
                return TextCleaner.Join(action, binding, Strings.RoleKeyBinding, state);
            }
            var optionsTab = go.GetComponent<OptionsPanelTab>();
            if (optionsTab != null)
            {
                string name = TabTextRef != null ? ReadText(TabTextRef(optionsTab)) : optionsTab.TabId;
                return TextCleaner.Join(name, Strings.RoleTab, IsCurrentOptionsTab(optionsTab) ? Strings.Selected : "", state);
            }

            // 2. Menu-specific entries.
            var legacyEntry = go.GetComponentInParent<MenuLegacyStartEntry>();
            if (legacyEntry != null)
            {
                string title = ReadText(legacyEntry.title);
                string status = legacyEntry.IsInstalled() ? Strings.LegacyInstalled : Strings.LegacyNotInstalled;
                return TextCleaner.Join(title, status, state);
            }
            var modEntry = go.GetComponentInParent<ModEntry>();
            if (modEntry != null)
            {
                string title = ReadText(modEntry.title);
                string what;
                if (item.Selectable == modEntry.higherPriorityButton) what = Strings.ModMoveUp;
                else if (item.Selectable == modEntry.lowerPriorityButton) what = Strings.ModMoveDown;
                else if (item.Selectable == modEntry.uploadButton) what = ReadText(modEntry.uploadText);
                else if (item.Selectable == modEntry.activationToggleButton) what = ReadText(modEntry.activationToggleText);
                else what = GenericText(go);
                string enabled = modEntry.EnabledMod ? Strings.ModEnabled : Strings.ModDisabled;
                return TextCleaner.Join(title, enabled, what, state);
            }
            var achTab = go.GetComponentInParent<AchievementCategoryTab>();
            if (achTab != null)
            {
                string name = AchTabTextRef != null ? ReadText(AchTabTextRef(achTab)) : achTab.CategoryId;
                bool on = item.Selectable is Toggle tabToggle && tabToggle.isOn;
                return TextCleaner.Join(name, Strings.RoleTab, on ? Strings.Selected : "", state);
            }
            var language = go.GetComponentInParent<LanguageChoice>();
            if (language != null)
            {
                return TextCleaner.Join(ReadText(language.Label), state);
            }
            var proem = go.GetComponent<ProemSlot>();
            if (proem != null)
            {
                string label = ProemLabelRef != null ? ReadText(ProemLabelRef(proem)) : "";
                string desc = ProemDescRef != null ? ReadText(ProemDescRef(proem)) : "";
                return TextCleaner.Join(label, desc, Strings.RoleLink, state);
            }
            if (item.Selectable is Toggle legacyToggle)
            {
                // New-game legacy choices: the game's own selectedLegacy, not the toggle group's default isOn.
                var legacy = LegacyForToggle(legacyToggle, out bool chosen);
                if (legacy != null)
                    return TextCleaner.Join(TextCleaner.Clean(legacy.Label), Strings.RoleLegacyChoice, chosen ? Strings.Selected : Strings.NotSelected, state);
            }
            if (go.GetComponent<StackButton>() != null) return TextCleaner.Join(Strings.StackCardsButton, state);

            // 3. Standard controls.
            string generic = GenericText(go);
            if (string.IsNullOrWhiteSpace(generic)) generic = KnownName(go.name);
            switch (item.Kind)
            {
                case UiItemKind.Toggle:
                {
                    var t = (Toggle)item.Selectable;
                    string on = t.group != null ? (t.isOn ? Strings.Selected : Strings.NotSelected) : (t.isOn ? Strings.Checked : Strings.Unchecked);
                    return TextCleaner.Join(generic, t.group != null ? Strings.RoleRadio : Strings.RoleCheckbox, on, state);
                }
                case UiItemKind.Slider:
                {
                    var s = (Slider)item.Selectable;
                    return TextCleaner.Join(generic, Strings.RoleSlider, FormatSliderValue(s), state);
                }
                case UiItemKind.Dropdown:
                {
                    var d = (TMP_Dropdown)item.Selectable;
                    string value = d.options != null && d.value >= 0 && d.value < d.options.Count ? d.options[d.value].text : "";
                    return TextCleaner.Join(generic, Strings.RoleCombo, value, state);
                }
                case UiItemKind.InputField:
                {
                    var f = (TMP_InputField)item.Selectable;
                    string label = LabelNearInputField(f);
                    return TextCleaner.Join(label, f.text, Strings.RoleEdit, state);
                }
                default:
                    return TextCleaner.Join(generic, state);
            }
        }

        /// <summary>The address of the first link in a text the game makes clickable (TextWithHyperlinks), or null.</summary>
        public static string FirstLink(UiItem item)
        {
            try
            {
                if (item?.Text == null || item.Go.GetComponent<TextWithHyperlinks>() == null) return null;
                var info = item.Text.textInfo;
                if (info == null || info.linkCount == 0) return null;
                string url = info.linkInfo[0].GetLinkID();
                return string.IsNullOrEmpty(url) ? null : url;
            }
            catch
            {
                return null;
            }
        }

        private static string StateSuffix(UiItem item)
        {
            if (item.Selectable != null && !item.Selectable.IsInteractable()) return Strings.Unavailable;
            return "";
        }

        public static string FormatSliderValue(Slider s)
        {
            if (s.wholeNumbers) return ((int)Math.Round(s.value)).ToString();
            float range = s.maxValue - s.minValue;
            if (range <= 0) return s.value.ToString("0.##");
            int pct = (int)Math.Round((s.value - s.minValue) / range * 100f);
            return Strings.PercentValue(pct);
        }

        private static string LabelNearInputField(TMP_InputField f)
        {
            if (f == null) return "";
            if (f.GetComponentInParent<StatusBar>() != null) return Strings.CharacterName;
            var parent = f.transform.parent;
            if (parent != null)
            {
                foreach (Transform sibling in parent)
                {
                    if (sibling == f.transform) continue;
                    var t = sibling.GetComponent<TMP_Text>();
                    if (t != null && t.gameObject.activeInHierarchy)
                    {
                        string s = TextCleaner.Clean(t.text);
                        if (!TextCleaner.IsPlaceholder(s)) return s;
                    }
                }
            }
            return CleanName(f.gameObject.name);
        }

        /// <summary>All visible TMP texts under the object, in hierarchy order, deduplicated.</summary>
        public static string GenericText(GameObject go)
        {
            var parts = new List<string>();
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!t.enabled) continue;
                if (t.GetComponentInParent<TMP_InputField>() != null && t.GetComponentInParent<TMP_InputField>().gameObject != go) continue;
                string s = TextCleaner.CleanMultiline(ReadText(t));
                if (TextCleaner.IsPlaceholder(s)) continue;
                if (parts.Contains(s)) continue;
                parts.Add(s);
            }
            if (parts.Count == 0)
            {
                foreach (var t in go.GetComponentsInChildren<Text>(false))
                {
                    string s = TextCleaner.Clean(t.text);
                    if (!TextCleaner.IsPlaceholder(s) && !parts.Contains(s)) parts.Add(s);
                }
            }
            return string.Join(", ", parts.ToArray());
        }

        public static string ReadText(TMP_Text t)
        {
            if (t == null) return "";
            try
            {
                string s = t.text;
                return s ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>Hover-only hint text (HasHintPanel) for the details buffer.</summary>
        public static string GetHint(GameObject go)
        {
            if (go == null) return "";
            var hint = go.GetComponentInParent<HasHintPanel>();
            if (hint == null) return "";
            try
            {
                if (!string.IsNullOrEmpty(hint.UI_LOC))
                {
                    string loc = Watchman.Get<ILocStringProvider>()?.Get(hint.UI_LOC);
                    if (!string.IsNullOrEmpty(loc) && !loc.StartsWith("MISSING_")) return TextCleaner.Clean(loc);
                }
            }
            catch { }
            return TextCleaner.Clean(hint.Hint);
        }

        private static string KnownName(string goName)
        {
            string n = goName ?? "";
            if (n.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0) return Strings.CloseButton;
            if (n.Equals("Higher", StringComparison.OrdinalIgnoreCase)) return Strings.ModMoveUp;
            if (n.Equals("Lower", StringComparison.OrdinalIgnoreCase)) return Strings.ModMoveDown;
            if (n.IndexOf("Stack", StringComparison.OrdinalIgnoreCase) >= 0) return Strings.StackCardsButton;
            return CleanName(n);
        }

        /// <summary>"Button_BeginGame (1)" becomes "Begin Game".</summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string n = name;
            int paren = n.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) n = n.Substring(0, paren);
            foreach (var prefix in new[] { "Button_", "Btn_", "Button", "Toggle_", "Overlay_", "OverlayWindow_" })
            {
                if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.Length > prefix.Length)
                {
                    n = n.Substring(prefix.Length);
                    break;
                }
            }
            n = n.Replace('_', ' ').Replace("Btn", "");
            var sb = new StringBuilder();
            for (int i = 0; i < n.Length; i++)
            {
                char c = n[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(n[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
