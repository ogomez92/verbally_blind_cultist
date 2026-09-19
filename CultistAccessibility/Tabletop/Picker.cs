using System;
using System.Collections.Generic;
using CultistAccessibility.Core;
using CultistAccessibility.Core.Buffers;
using UnityEngine.InputSystem;

namespace CultistAccessibility.Tabletop
{
    internal sealed class PickerOption
    {
        public string Label;
        public Func<List<string>> Details;
        public Action OnChoose;
    }

    /// <summary>
    /// A modal list: Up/Down browse, Enter chooses, Escape cancels, I reads details.
    /// While open it owns the keyboard and the game's hotkeys are blocked (InputGate.ModalOpen).
    /// </summary>
    internal sealed class Picker
    {
        private readonly List<PickerOption> _options;
        private readonly Action _onCancel;
        private int _index;

        public string Title { get; }
        public bool IsOpen { get; private set; }

        public Picker(string title, List<PickerOption> options, Action onCancel = null)
        {
            Title = title;
            _options = options ?? new List<PickerOption>();
            _onCancel = onCancel;
        }

        public void Open()
        {
            IsOpen = true;
            InputGate.PickerOpen = true;
            _index = 0;
            if (_options.Count == 0)
            {
                Speech.Say(Title);
                Close();
                return;
            }
            Speech.Say(Title + " " + _options[0].Label);
            UpdateDetails();
        }

        public void Close()
        {
            IsOpen = false;
            InputGate.PickerOpen = false;
        }

        public void HandleInput()
        {
            if (!IsOpen) return;
            if (KeyInput.Pressed(Key.Escape))
            {
                Close();
                Speech.Say(Strings.PickerCancelled);
                _onCancel?.Invoke();
                return;
            }
            if (KeyInput.Repeat(Key.DownArrow)) Move(1);
            else if (KeyInput.Repeat(Key.UpArrow)) Move(-1);
            else if (KeyInput.Pressed(Key.Home)) MoveTo(0);
            else if (KeyInput.Pressed(Key.End)) MoveTo(_options.Count - 1);
            else if (KeyInput.Pressed(Key.Enter) || KeyInput.Pressed(Key.NumpadEnter))
            {
                var opt = _options[_index];
                Close();
                try { opt.OnChoose?.Invoke(); }
                catch (Exception ex) { Plugin.LogError("Picker choice failed: " + ex); }
            }
            else if (KeyInput.Hotkey(ModConfig.KeyInspect.Value) || KeyInput.Hotkey(ModConfig.KeyRepeat.Value))
            {
                var d = _options[_index].Details?.Invoke();
                if (d != null && d.Count > 0) Speech.Say(TextCleaner.Sentences(d));
                else Speech.Say(_options[_index].Label);
            }
        }

        private void Move(int dir)
        {
            int next = _index + dir;
            if (next < 0) { Speech.SayFocus(Strings.StartOfList + " " + _options[_index].Label); return; }
            if (next >= _options.Count) { Speech.SayFocus(Strings.EndOfList + " " + _options[_index].Label); return; }
            MoveTo(next);
        }

        private void MoveTo(int i)
        {
            if (i < 0 || i >= _options.Count) return;
            _index = i;
            string label = _options[i].Label;
            if (ModConfig.Level == Verbosity.Verbose) label += ", " + Strings.PositionOf(i + 1, _options.Count);
            Speech.SayFocus(label);
            UpdateDetails();
        }

        private void UpdateDetails()
        {
            try
            {
                var d = _options[_index].Details?.Invoke();
                if (d != null) BufferManager.SetDetails(d);
            }
            catch { }
        }
    }
}
