using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace CultistAccessibility.Core.Buffers
{
    /// <summary>
    /// Review buffers. Ctrl+Up/Down move inside the current buffer, Ctrl+Left/Right switch buffers
    /// (skipping empty ones) and announce "Name, N items". Refresher-driven buffers rebuild when switched to.
    /// </summary>
    internal static class BufferManager
    {
        public static AnnouncementBuffer Events { get; private set; }
        public static AnnouncementBuffer Details { get; private set; }
        public static AnnouncementBuffer Story { get; private set; }
        public static AnnouncementBuffer Verbs { get; private set; }
        public static AnnouncementBuffer Table { get; private set; }
        public static AnnouncementBuffer Status { get; private set; }

        private static readonly List<AnnouncementBuffer> All = new List<AnnouncementBuffer>();
        private static int _current;

        public static void Initialize()
        {
            Events = new AnnouncementBuffer(Strings.BufferEvents, 200) { FollowLatest = true };
            Details = new AnnouncementBuffer(Strings.BufferDetails, 200);
            Story = new AnnouncementBuffer(Strings.BufferStory, 100) { FollowLatest = true };
            Verbs = new AnnouncementBuffer(Strings.BufferVerbs, 200);
            Table = new AnnouncementBuffer(Strings.BufferTable, 400);
            Status = new AnnouncementBuffer(Strings.BufferStatus, 50);
            All.Clear();
            All.Add(Events);
            All.Add(Details);
            All.Add(Story);
            All.Add(Verbs);
            All.Add(Table);
            All.Add(Status);
            _current = 0;
        }

        public static AnnouncementBuffer Current => All.Count > 0 ? All[_current] : null;

        public static void AddEvent(string text) => Events?.Add(text);

        public static void AddStory(string text)
        {
            if (Story == null) return;
            // Newest entry on top, but its own sentences in reading order: add the chunks last-first.
            var chunks = TextCleaner.SplitIntoSpeechItems(text);
            for (int i = chunks.Count - 1; i >= 0; i--)
                Story.Add(chunks[i]);
        }

        public static void SetDetails(IList<string> lines) => Details?.SetItems(lines);

        /// <summary>Called on screen changes so details from one screen are not read on another.</summary>
        public static void ClearFocusFed()
        {
            Details?.Clear();
        }

        /// <summary>Handles Ctrl+arrows. Returns true when the key press was consumed.</summary>
        public static bool HandleInput()
        {
            if (!KeyInput.Ctrl) return false;
            if (KeyInput.Repeat(Key.UpArrow))
            {
                Refresh(Current, onlyIfEmpty: true);
                SpeakItem(Current?.MoveDeeper());
                return true;
            }
            if (KeyInput.Repeat(Key.DownArrow))
            {
                Refresh(Current, onlyIfEmpty: true);
                SpeakItem(Current?.MoveTowardTop());
                return true;
            }
            if (KeyInput.Pressed(Key.LeftArrow))
            {
                Switch(-1);
                return true;
            }
            if (KeyInput.Pressed(Key.RightArrow))
            {
                Switch(1);
                return true;
            }
            return false;
        }

        private static void SpeakItem(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Speech.Say(Strings.BufferEmpty(Current?.Name ?? ""));
                return;
            }
            Speech.Say(text);
        }

        private static void Refresh(AnnouncementBuffer b, bool onlyIfEmpty)
        {
            if (b == null || b.Refresher == null) return;
            if (onlyIfEmpty && b.Count > 0) return;
            b.Refresh();
        }

        private static void Switch(int dir)
        {
            int n = All.Count;
            for (int step = 1; step <= n; step++)
            {
                int idx = ((_current + dir * step) % n + n) % n;
                var b = All[idx];
                if (b.Refresher != null) b.Refresh();
                if (b.Count == 0 || !b.IsAvailable) continue;
                _current = idx;
                b.OnFocused();
                Speech.Say(Strings.BufferSummary(b.Name, b.Count));
                return;
            }
            Speech.Say(Strings.AllBuffersEmpty);
        }
    }
}
