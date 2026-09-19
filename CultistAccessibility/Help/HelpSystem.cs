using System;
using System.Collections.Generic;
using System.Linq;
using CultistAccessibility.Core;
using UnityEngine.InputSystem;

namespace CultistAccessibility.Help
{
    internal interface IHelpContext
    {
        string ContextName { get; }
        /// <summary>Higher wins. Modes outrank screens; mod modals outrank everything.</summary>
        int Priority { get; }
        bool IsActive();
        IEnumerable<string> GetHelpLines();
    }

    /// <summary>A help context built from a predicate and a line provider.</summary>
    internal sealed class HelpContext : IHelpContext
    {
        private readonly Func<bool> _isActive;
        private readonly Func<IEnumerable<string>> _lines;

        public HelpContext(string name, int priority, Func<bool> isActive, Func<IEnumerable<string>> lines)
        {
            ContextName = name;
            Priority = priority;
            _isActive = isActive;
            _lines = lines;
        }

        public string ContextName { get; }
        public int Priority { get; }
        public bool IsActive() => _isActive();
        public IEnumerable<string> GetHelpLines() => _lines();
    }

    /// <summary>
    /// F1 opens a browsable list for the active context: Up/Down read one line, Home/End jump,
    /// F1, Enter or Escape close. While open it owns the keyboard (InputGate.ModalOpen).
    /// </summary>
    internal static class HelpSystem
    {
        private static readonly List<IHelpContext> Contexts = new List<IHelpContext>();
        private static List<string> _lines = new List<string>();
        private static int _index;

        public static bool IsOpen { get; private set; }

        public static void Register(IHelpContext context)
        {
            Contexts.Add(context);
        }

        public static void RegisterDefaultContexts()
        {
            HelpContexts.RegisterAll();
        }

        public static void Open()
        {
            var active = Contexts.Where(c => SafeIsActive(c)).OrderByDescending(c => c.Priority).FirstOrDefault();
            _lines = new List<string>();
            if (active != null)
            {
                _lines.Add(Strings.HelpTitle(active.ContextName));
                try { _lines.AddRange(active.GetHelpLines().Where(l => !string.IsNullOrWhiteSpace(l))); }
                catch (Exception ex) { Plugin.LogWarning("Help context failed: " + ex.Message); }
            }
            _lines.AddRange(HelpContexts.GlobalLines());
            _index = 0;
            IsOpen = true;
            InputGate.HelpOpen = true;
            Speech.Say(_lines[0] + ". " + Strings.HelpNavigationHint);
        }

        public static void Close()
        {
            IsOpen = false;
            InputGate.HelpOpen = false;
            Speech.Say(Strings.HelpClosed);
        }

        private static bool SafeIsActive(IHelpContext c)
        {
            try { return c.IsActive(); }
            catch { return false; }
        }

        /// <summary>Called every frame while open. Consumes all input.</summary>
        public static void HandleInput()
        {
            if (KeyInput.Pressed(ModConfig.KeyHelp.Value) || KeyInput.Pressed(Key.Escape) || KeyInput.Pressed(Key.Enter) || KeyInput.Pressed(Key.NumpadEnter))
            {
                Close();
                return;
            }
            if (KeyInput.Repeat(Key.DownArrow))
            {
                if (_index < _lines.Count - 1) _index++;
                else { Speech.SayFocus(Strings.EndOfList + " " + _lines[_index]); return; }
                Speech.SayFocus(_lines[_index]);
            }
            else if (KeyInput.Repeat(Key.UpArrow))
            {
                if (_index > 0) _index--;
                else { Speech.SayFocus(Strings.StartOfList + " " + _lines[_index]); return; }
                Speech.SayFocus(_lines[_index]);
            }
            else if (KeyInput.Pressed(Key.Home))
            {
                _index = 0;
                Speech.SayFocus(_lines[_index]);
            }
            else if (KeyInput.Pressed(Key.End))
            {
                _index = _lines.Count - 1;
                Speech.SayFocus(_lines[_index]);
            }
        }
    }
}
