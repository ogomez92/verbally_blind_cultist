using System;
using System.Collections.Generic;

namespace CultistAccessibility.Core.Buffers
{
    /// <summary>
    /// A named list with a review cursor. Index 0 is the top item (newest event / first detail line).
    /// Cursor -1 means "before the top", so the first "next" press reads item 0. No wrap.
    /// </summary>
    internal sealed class AnnouncementBuffer
    {
        private readonly List<string> _items = new List<string>();
        private readonly int _maxItems;
        private int _cursor = -1;

        public string Name { get; }
        public bool FollowLatest { get; set; }

        /// <summary>Rebuilds content from game state on demand. Returning null marks the buffer unavailable.</summary>
        public Func<List<string>> Refresher { get; set; }

        public AnnouncementBuffer(string name, int maxItems = 200)
        {
            Name = name;
            _maxItems = maxItems;
        }

        public int Count => _items.Count;
        public bool IsAvailable { get; private set; } = true;

        public void Add(string item)
        {
            if (string.IsNullOrEmpty(item)) return;
            _items.Insert(0, item);
            if (_cursor >= 0) _cursor++;
            while (_items.Count > _maxItems) _items.RemoveAt(_items.Count - 1);
            if (_cursor >= _items.Count) _cursor = _items.Count - 1;
        }

        /// <summary>Replaces the content; the cursor resets only when the content changed.</summary>
        public void SetItems(IList<string> items)
        {
            bool same = items != null && items.Count == _items.Count;
            if (same)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] != _items[i]) { same = false; break; }
                }
            }
            if (same) return;
            _items.Clear();
            if (items != null)
            {
                foreach (var i in items)
                    if (!string.IsNullOrEmpty(i)) _items.Add(i);
            }
            _cursor = -1;
        }

        public void Clear()
        {
            _items.Clear();
            _cursor = -1;
        }

        public void Refresh()
        {
            if (Refresher == null) return;
            List<string> content = null;
            try { content = Refresher(); }
            catch (Exception ex) { Plugin.LogWarning("Buffer " + Name + " refresh failed: " + ex.Message); }
            IsAvailable = content != null;
            SetItems(content ?? new List<string>());
        }

        public void OnFocused()
        {
            if (FollowLatest) _cursor = -1;
        }

        /// <summary>Ctrl+Up: older events / further detail lines.</summary>
        public string MoveDeeper()
        {
            if (_items.Count == 0) return null;
            if (_cursor < _items.Count - 1)
            {
                _cursor++;
                return _items[_cursor];
            }
            return Strings.BufferEnd + " " + _items[_cursor];
        }

        /// <summary>Ctrl+Down: back toward the top item.</summary>
        public string MoveTowardTop()
        {
            if (_items.Count == 0) return null;
            if (_cursor > 0)
            {
                _cursor--;
                return _items[_cursor];
            }
            _cursor = 0;
            return Strings.BufferTop + " " + _items[0];
        }

        public string Current => _cursor >= 0 && _cursor < _items.Count ? _items[_cursor] : (_items.Count > 0 ? _items[0] : null);

        public IReadOnlyList<string> Items => _items;
    }
}
