using System;
using System.Collections.Generic;
using System.Linq;
using CultistAccessibility.Core;
using CultistAccessibility.Core.Buffers;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CultistAccessibility.Navigation
{
    /// <summary>
    /// Generic keyboard layer for uGUI screens (menus, overlays, options, legacy choice, endings).
    /// Items are the Selectables a real mouse could click right now (raycast test at their centre, so modal
    /// blockers and hidden overlays are respected) plus read-only text rows in the same windows.
    /// Up/Down walk them in reading order, Left/Right adjust sliders and combo boxes, Enter activates.
    /// Focus drives the game's hover state so what Enter clicks is always what was announced.
    /// </summary>
    internal sealed class UiNavigator
    {
        private readonly List<UiItem> _items = new List<UiItem>();
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private UiItem _focused;
        private float _nextRefresh;
        private string _contextKey = "";
        private bool _announcePending = true;
        private GameObject _pendingKeybind;
        // A plain flag: Unity's null check on _pendingKeybind turns false when the field is destroyed mid-capture,
        // which would leave InputGate.PassThrough on and the mod's keys dead.
        private bool _capturingKeybind;
        private int _fadeWaits;
        private float _pendingSince = -1f;
        private string _lastAnnouncement;
        private float _lastAnnouncementAt;
        private string _candidateKey;
        private int _candidateCount;

        /// <summary>Restrict to screen-space canvases (the tabletop's world-space canvas has its own navigator).</summary>
        public bool ScreenSpaceOnly { get; set; }

        /// <summary>Optional screen title spoken before the first announcement.</summary>
        public string PendingScreenTitle { get; set; }

        public IReadOnlyList<UiItem> Items => _items;
        public UiItem Focused => _focused;

        public void Reset()
        {
            if (_capturingKeybind) EndKeybindCapture();
            _items.Clear();
            _focused = null;
            _lastSpokenFocus = null;
            _contextKey = "";
            _announcePending = true;
            _pendingSince = -1f;
            _nextRefresh = 0;
            UiActions.ClearHover();
        }

        /// <summary>Per-frame tick. Returns nothing; speaks context changes.</summary>
        public void Update(bool handleInput)
        {
            if (_capturingKeybind)
            {
                UpdateKeybindCapture();
                return;
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                Refresh();
                _nextRefresh = Time.unscaledTime + 0.25f;
            }

            if (_postActivationItem != null && Time.unscaledTime >= _postActivationAt)
            {
                var pending = _postActivationItem;
                _postActivationItem = null;
                // Only when the same windows are still in front: opening an overlay is announced by the context.
                if (!_announcePending && _candidateKey == null && _contextKey == _postActivationContext)
                {
                    // The same control, or the one now at its place if the game rebuilt the list.
                    UiItem target = pending.IsValid && _items.Any(i => i.Go == pending.Go) ? pending : _focused;
                    if (target != null)
                    {
                        string now = Describe(target);
                        if (now != _postActivationLabel) Speech.SayFocus(now);
                    }
                }
            }

            if (handleInput) HandleInput();
        }

        private UiItem _postActivationItem;
        private string _postActivationLabel;
        private string _postActivationContext;
        private float _postActivationAt;

        public void Refresh()
        {
            GameObject focusedGo = _focused?.Go;
            bool hadFocus = _focused != null;
            Collect();
            string key = ComputeContextKey();
            bool contextChanged = false;
            if (key != _contextKey)
            {
                // A new set of windows must hold for two refreshes: fades and modal blockers flicker for a frame.
                if (key == _candidateKey) _candidateCount++;
                else { _candidateKey = key; _candidateCount = 1; }
                if (_candidateCount >= 2)
                {
                    _contextKey = key;
                    _candidateKey = null;
                    _candidateCount = 0;
                    contextChanged = true;
                    _announcePending = true;
                }
            }
            else
            {
                _candidateKey = null;
                _candidateCount = 0;
            }

            _focused = focusedGo != null ? _items.FirstOrDefault(i => i.Go == focusedGo) : null;

            // Announce once something can be used; a screen of text only (or of disabled controls) after a moment.
            bool hasUsable = _items.Any(i => i.Selectable != null && i.Interactable);
            bool hasControls = _items.Any(i => i.Selectable != null);
            if (_announcePending && _pendingSince < 0) _pendingSince = Time.unscaledTime;
            bool waitedLong = _pendingSince >= 0 && Time.unscaledTime - _pendingSince > 2f;
            bool settled = hasUsable || (!hasControls && ScreenTracker.TimeOnScreen > 1.5f) || waitedLong;
            if (_announcePending && _items.Count > 0 && settled)
            {
                if (_focused == null || contextChanged) _focused = DefaultFocus();
                // Wait for a fading window to become visible so its title can be read (at most ~1 second).
                if (_focused != null && (WindowAlpha(_focused.Window) < 0.6f || NotificationNotReadYet(_focused)) && _fadeWaits < 4)
                {
                    _fadeWaits++;
                    return;
                }
                _fadeWaits = 0;
                _announcePending = false;
                _pendingSince = -1f;
                // The announcement describes the current set of windows: adopt it, or the debounce would report it
                // as a change on the next refresh and the screen would be announced twice.
                _contextKey = key;
                _candidateKey = null;
                _candidateCount = 0;
                AnnounceContext();
            }
            else if (_focused == null && _items.Count > 0 && hadFocus)
            {
                // The focused object vanished (a list the game rebuilt, like the mods list after Enable):
                // keep the same position; the post-activation readout then says the new state.
                UiItem samePath = null;
                if (!string.IsNullOrEmpty(_focusedPath))
                {
                    samePath = _items.Where(i => i.Go != null && PathOf(i.Go.transform) == _focusedPath)
                        .OrderBy(i => Math.Abs(_items.IndexOf(i) - _focusedIndex)).FirstOrDefault();
                }
                _focused = samePath ?? _items[Mathf.Clamp(_focusedIndex, 0, _items.Count - 1)];
                SyncHover();
            }
            if (_focused != null)
            {
                _focusedIndex = _items.IndexOf(_focused);
                _focusedPath = _focused.Go != null ? PathOf(_focused.Go.transform) : null;
            }
        }

        private int _focusedIndex;
        private string _focusedPath;
        // The control the player last heard as focused. A context change that leaves focus on it (a panel without
        // controls appeared or went, such as a save message) does not read it again.
        private GameObject _lastSpokenFocus;

        private UiItem DefaultFocus()
        {
            var hint = ScreenHints.DefaultFocusObject();
            if (hint != null)
            {
                var hinted = _items.FirstOrDefault(i => i.Go == hint);
                if (hinted != null) return hinted;
            }
            var firstAction = _items.FirstOrDefault(i => i.Selectable != null && i.Interactable
                && i.Go.name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) < 0);
            var firstSelectable = _items.FirstOrDefault(i => i.Selectable != null && i.Interactable);
            if (firstAction != null || firstSelectable != null) return firstAction ?? firstSelectable;
            // Only text (a notification over a blocked screen): start in the frontmost panel, which is drawn last.
            var front = _items.LastOrDefault()?.Window;
            return _items.FirstOrDefault(i => i.Window == front) ?? _items.FirstOrDefault();
        }

        private void AnnounceContext()
        {
            // NotificationPatches reads every notification as it opens; do not read it a second time.
            if (NotificationReadByPatch(_focused) && !_items.Any(i => i.Selectable != null && i.Interactable))
            {
                if (!string.IsNullOrEmpty(PendingScreenTitle)) Speech.Say(PendingScreenTitle);
                PendingScreenTitle = null;
                // Focus is in the notification now; when it closes, say where focus lands even if it is unchanged.
                _lastSpokenFocus = _focused.Go;
                SyncHover();
                return;
            }
            // A notification dialog with buttons (the save error window): NotificationPatches reads its text, so
            // only the focus is said here.
            if (NotificationReadByPatch(_focused) && _items.Any(i => i.Selectable != null && i.Interactable))
            {
                if (!string.IsNullOrEmpty(PendingScreenTitle)) Speech.Say(PendingScreenTitle);
                PendingScreenTitle = null;
                Speech.Say(Describe(_focused));
                _lastSpokenFocus = _focused.Go;
                SyncHover();
                UpdateDetails();
                return;
            }
            string title = FindContextTitle();
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(PendingScreenTitle))
            {
                parts.Add(PendingScreenTitle);
                PendingScreenTitle = null;
            }
            if (!string.IsNullOrEmpty(title) && !parts.Contains(title)) parts.Add(title);
            string body = WindowBodyText(title);
            if (!string.IsNullOrEmpty(body)) parts.Add(body);
            bool focusOnly = parts.Count == 0;
            if (_focused != null && !(_focused.Kind == UiItemKind.Text && body.Contains(UiReader.GetLabel(_focused)))) parts.Add(Describe(_focused));
            string text = TextCleaner.Sentences(parts);
            if (focusOnly && _focused?.Go != null && _focused.Go == _lastSpokenFocus) { SyncHover(); return; }
            _lastSpokenFocus = _focused?.Go;
            // A window set that settles in two steps (background controls disabled a moment later) would
            // announce the same thing twice.
            if (text == _lastAnnouncement && Time.unscaledTime - _lastAnnouncementAt < 3f) { SyncHover(); return; }
            _lastAnnouncement = text;
            _lastAnnouncementAt = Time.unscaledTime;
            Speech.Say(text);
            SyncHover();
            UpdateDetails();
        }

        private static SecretHistories.UI.NotificationWindow NotificationOf(UiItem item)
        {
            return item?.Go != null ? item.Go.GetComponentInParent<SecretHistories.UI.NotificationWindow>() : null;
        }

        private static bool NotificationReadByPatch(UiItem item)
        {
            return NotificationOf(item) != null && ModConfig.AnnounceNotifications.Value;
        }

        /// <summary>The patch reads a notification one frame after Show(); let it speak first (at most ~1 second).</summary>
        private static bool NotificationNotReadYet(UiItem item)
        {
            var note = NotificationOf(item);
            if (note == null || !ModConfig.AnnounceNotifications.Value) return false;
            return !(note.GetInstanceID() == Patches.NotificationPatches.LastReadWindow
                && Time.unscaledTime - Patches.NotificationPatches.LastReadAt < 10f);
        }

        /// <summary>Short explanatory text of a dialog (for example a confirmation question), read when it opens.</summary>
        private string WindowBodyText(string title)
        {
            bool wholeScreen = ScreenHints.ReadWholeBodyOnOpen;
            if (_focused?.Window == null) return "";
            if (!wholeScreen && _focused.Window.GetComponent<SecretHistories.UI.CanvasGroupFader>() == null) return "";
            var texts = _items.Where(i => i.Kind == UiItemKind.Text && i.Window == _focused.Window)
                .Select(i => UiReader.GetLabel(i))
                .Where(s => !string.IsNullOrEmpty(s) && s != title)
                .ToList();
            string joined = TextCleaner.Sentences(texts);
            return wholeScreen || joined.Length <= 400 ? joined : "";
        }

        private string FindContextTitle()
        {
            // The frontmost window's own title: a text called TitleText/Title/Header near the top of its panel
            // (breadth first, so a list entry's "Title" deeper down is never mistaken for it).
            Transform window = _focused?.Window;
            if (window == null) return "";
            var level = new List<Transform> { window };
            for (int depth = 0; depth < 3 && level.Count > 0; depth++)
            {
                var next = new List<Transform>();
                foreach (var parent in level)
                {
                    foreach (Transform child in parent)
                    {
                        if (!child.gameObject.activeInHierarchy) continue;
                        string n = child.name;
                        if (n == "TitleText" || n == "Title" || n == "Header")
                        {
                            var text = child.GetComponent<TMP_Text>();
                            if (text != null && IsVisible(child.gameObject) && WindowAlpha(child) > 0.05f
                                && child.GetComponentInParent<Selectable>() == null && !UiReader.IsPartOfCompositeControl(child.gameObject))
                            {
                                string s = TextCleaner.Clean(UiReader.ReadText(text));
                                if (!TextCleaner.IsPlaceholder(s)) return s;
                            }
                        }
                        next.Add(child);
                    }
                }
                level = next;
            }
            return "";
        }

        private string ComputeContextKey()
        {
            var ids = new SortedSet<int>();
            foreach (var i in _items)
                if (i.Window != null && i.Selectable != null) ids.Add(i.Window.GetInstanceID());
            return string.Join(",", ids.Select(x => x.ToString()).ToArray());
        }

        /// <summary>
        /// The panel an object belongs to: the nearest CanvasGroupFader (overlays, dialogs, details windows)
        /// or nested canvas, or else the top-level child of its canvas.
        /// </summary>
        public static Transform WindowOf(Transform t)
        {
            Transform cur = t;
            while (cur != null)
            {
                // Panels the game shows and hides fade with a CanvasGroupFader; notification dialogs slide in
                // (NavigationAnimation) inside a canvas that is itself a fader, and are panels of their own.
                if (cur.GetComponent<SecretHistories.UI.CanvasGroupFader>() != null
                    || cur.GetComponent<SecretHistories.UI.NotificationWindow>() != null) return cur;
                var parent = cur.parent;
                if (parent == null) return cur;
                if (parent.GetComponent<SecretHistories.UI.CanvasGroupFader>() != null
                    || parent.GetComponent<SecretHistories.UI.NotificationWindow>() != null) return parent;
                // Otherwise the top-level panel of a root canvas (nested canvases are only sorting helpers).
                var canvas = parent.GetComponent<Canvas>();
                if (canvas != null && canvas.isRootCanvas) return cur;
                cur = parent;
            }
            return t;
        }

        // ---------------------------------------------------------------- collection

        private void Collect()
        {
            _items.Clear();
            var es = EventSystem.current;
            int order = 0;
            var selectableWindows = new HashSet<Transform>();

            foreach (var sel in Selectable.allSelectablesArray)
            {
                if (sel == null || !sel.isActiveAndEnabled) continue;
                if (sel is Scrollbar) continue;
                if (IsPersistentDebugUi(sel.gameObject)) continue;
                var canvas = sel.GetComponentInParent<Canvas>();
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                if (ScreenSpaceOnly && canvas.rootCanvas.renderMode == RenderMode.WorldSpace) continue;
                var rt = sel.transform as RectTransform;
                if (rt == null) continue;
                if (!IsVisible(sel.gameObject)) continue;
                var scroll = sel.GetComponentInParent<ScrollRect>();
                if (!IsReachable(rt, sel.gameObject, scroll, es)) continue;

                var item = new UiItem
                {
                    Go = sel.gameObject,
                    Selectable = sel,
                    Kind = KindOf(sel),
                    Rect = rt,
                    Canvas = canvas,
                    Window = WindowOf(sel.transform),
                    Scroll = scroll,
                    ScreenPos = ScreenCenter(rt, canvas),
                    Order = order++
                };
                _items.Add(item);
                selectableWindows.Add(item.Window);
            }

            // Windows whose controls are all disabled are background (the status bar under the options menu):
            // when any window has a usable control, drop them with their texts.
            var liveWindows = new HashSet<Transform>(_items.Where(i => i.Selectable != null && i.Interactable).Select(i => i.Window));
            if (liveWindows.Count > 0)
            {
                _items.RemoveAll(i => i.Selectable != null && !liveWindows.Contains(i.Window));
                selectableWindows.IntersectWith(liveWindows);
            }

            // Achievements: one row each, with their locked or unlocked state.
            foreach (var entry in UnityEngine.Object.FindObjectsOfType<SecretHistories.AchievementEntry>())
            {
                if (entry == null || !entry.isActiveAndEnabled || IsPersistentDebugUi(entry.gameObject)) continue;
                var window = WindowOf(entry.transform);
                if (!BelongsToAny(window, selectableWindows) || WindowAlpha(window) < 0.05f) continue;
                var rt = entry.transform as RectTransform;
                var canvas = entry.GetComponentInParent<Canvas>();
                var e = entry;
                _items.Add(new UiItem
                {
                    Go = entry.gameObject,
                    Kind = UiItemKind.Custom,
                    Rect = rt,
                    Canvas = canvas,
                    Window = window,
                    Scroll = entry.GetComponentInParent<ScrollRect>(),
                    ScreenPos = rt != null ? ScreenCenter(rt, canvas) : Vector2.zero,
                    Order = order++,
                    CustomLabel = () => UiReader.AchievementText(e),
                    CustomActivate = () => Speech.SayFocus(UiReader.AchievementText(e))
                });
            }

            // Card exhibits (credits, news): each card is a thing a sighted player clicks to read.
            foreach (var exhibit in UnityEngine.Object.FindObjectsOfType<ExhibitCardsSphere>())
            {
                if (exhibit == null || !exhibit.isActiveAndEnabled || IsPersistentDebugUi(exhibit.gameObject)) continue;
                var window = WindowOf(exhibit.transform);
                if (!BelongsToAny(window, selectableWindows) || WindowAlpha(window) < 0.05f) continue;
                var canvas = exhibit.GetComponentInParent<Canvas>();
                foreach (var token in exhibit.Tokens)
                {
                    if (token == null || token.Defunct) continue;
                    var tok = token;
                    var rt = tok.TokenRectTransform;
                    _items.Add(new UiItem
                    {
                        Go = tok.gameObject,
                        Kind = UiItemKind.Custom,
                        Rect = rt,
                        Canvas = canvas,
                        Window = window,
                        ScreenPos = rt != null ? ScreenCenter(rt, canvas) : Vector2.zero,
                        Order = order++,
                        CustomLabel = () => TextCleaner.Join(TextCleaner.Clean(tok.Payload.Label), TextCleaner.CleanMultiline(tok.Payload.Description)),
                        CustomActivate = () =>
                        {
                            // Same as clicking the card: the window shows its text (CardDisplayWindow listens).
                            UiActions.Click(tok.gameObject);
                            Speech.SayFocus(TextCleaner.Join(TextCleaner.Clean(tok.Payload.Label), TextCleaner.CleanMultiline(tok.Payload.Description)));
                        }
                    });
                }
            }

            // Text rows: visible texts that belong to the same windows as the reachable controls.
            bool noControls = _items.Count == 0;
            var seen = new List<KeyValuePair<string, Vector2>>();
            foreach (var t in UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>())
            {
                if (t == null || !t.isActiveAndEnabled) continue;
                if (IsPersistentDebugUi(t.gameObject)) continue;
                if (t.GetComponentInParent<Selectable>() != null) continue;
                if (UiReader.IsPartOfCompositeControl(t.gameObject)) continue;
                if (t.GetComponentInParent<SecretHistories.UI.Token>() != null) continue;
                var canvas = t.canvas;
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                if (ScreenSpaceOnly && canvas.rootCanvas.renderMode == RenderMode.WorldSpace) continue;
                Transform textWindow = WindowOf(t.transform);
                if (!noControls && !BelongsToAny(textWindow, selectableWindows)) continue;
                if (!IsVisible(t.gameObject)) continue;
                if (t.color.a < 0.05f) continue;
                string clean = TextCleaner.Clean(UiReader.ReadText(t));
                if (TextCleaner.IsPlaceholder(clean) || clean.Length < 2) continue;
                var rt = t.rectTransform;
                var scroll = t.GetComponentInParent<ScrollRect>();
                Vector2 pos = ScreenCenter(rt, canvas);
                if (scroll == null && !OnScreen(pos)) continue;
                if (scroll != null && !IsReachable((RectTransform)scroll.transform, scroll.gameObject, null, es) && !OnScreen(pos)) continue;
                bool dup = false;
                foreach (var kv in seen)
                {
                    if (kv.Key == clean && Vector2.Distance(kv.Value, pos) < 40f) { dup = true; break; }
                }
                if (dup) continue;
                seen.Add(new KeyValuePair<string, Vector2>(clean, pos));
                _items.Add(new UiItem
                {
                    Go = t.gameObject,
                    Text = t,
                    Kind = UiItemKind.Text,
                    Rect = rt,
                    Canvas = canvas,
                    Window = textWindow,
                    Scroll = scroll,
                    ScreenPos = pos,
                    Order = order++
                });
            }

            SortHierarchyOrder(_items);
        }

        /// <summary>A text belongs to the front windows when its panel is one of them or nested inside one.</summary>
        private static bool BelongsToAny(Transform textWindow, HashSet<Transform> windows)
        {
            foreach (var w in windows)
            {
                if (w == null) continue;
                if (textWindow == w || textWindow.IsChildOf(w)) return true;
            }
            return false;
        }

        /// <summary>
        /// The master scene holds the developer console, the bug-report panels and the hover hint panel;
        /// none of it is a screen the player navigates.
        /// </summary>
        private static bool IsPersistentDebugUi(GameObject go)
        {
            return go.scene.name == "S0Master";
        }

        /// <summary>
        /// Designer order: the order of the objects in the hierarchy. Layout groups make it match what is
        /// on screen, and it keeps a screen's main buttons before its side panels.
        /// </summary>
        public static void SortHierarchyOrder(List<UiItem> items)
        {
            var keys = new Dictionary<UiItem, List<int>>();
            foreach (var i in items) keys[i] = HierarchyKey(i.Go.transform);
            items.Sort((a, b) =>
            {
                var ka = keys[a];
                var kb = keys[b];
                int n = Math.Min(ka.Count, kb.Count);
                for (int x = 0; x < n; x++)
                {
                    int c = ka[x].CompareTo(kb[x]);
                    if (c != 0) return c;
                }
                int len = ka.Count.CompareTo(kb.Count);
                return len != 0 ? len : a.Order.CompareTo(b.Order);
            });
        }

        private static List<int> HierarchyKey(Transform t)
        {
            var path = new List<int>();
            while (t != null)
            {
                path.Add(t.GetSiblingIndex());
                t = t.parent;
            }
            path.Reverse();
            return path;
        }

        public static void SortReadingOrder(List<UiItem> items)
        {
            const float rowTolerance = 14f;
            items.Sort((a, b) =>
            {
                float dy = b.ScreenPos.y - a.ScreenPos.y;
                if (Math.Abs(dy) > rowTolerance) return dy > 0 ? 1 : -1;
                int dx = a.ScreenPos.x.CompareTo(b.ScreenPos.x);
                if (dx != 0) return dx;
                return a.Order.CompareTo(b.Order);
            });
        }

        private static UiItemKind KindOf(Selectable s)
        {
            if (s is Toggle) return UiItemKind.Toggle;
            if (s is Slider) return UiItemKind.Slider;
            if (s is TMP_Dropdown) return UiItemKind.Dropdown;
            if (s is TMP_InputField) return UiItemKind.InputField;
            return UiItemKind.Button;
        }

        /// <summary>Product of the CanvasGroup alphas from the window up to the root.</summary>
        private static float WindowAlpha(Transform window)
        {
            float alpha = 1f;
            Transform t = window;
            while (t != null)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    alpha *= cg.alpha;
                    if (cg.ignoreParentGroups) break;
                }
                t = t.parent;
            }
            return alpha;
        }

        private static bool IsVisible(GameObject go)
        {
            var graphic = go.GetComponent<Graphic>();
            if (graphic != null && graphic.canvasRenderer != null)
            {
                try
                {
                    if (graphic.canvasRenderer.GetInheritedAlpha() < 0.05f) return false;
                }
                catch { }
            }
            else
            {
                // No own graphic: check CanvasGroups up the chain.
                float alpha = 1f;
                Transform t = go.transform;
                while (t != null)
                {
                    var cg = t.GetComponent<CanvasGroup>();
                    if (cg != null)
                    {
                        alpha *= cg.alpha;
                        if (cg.ignoreParentGroups) break;
                    }
                    t = t.parent;
                }
                if (alpha < 0.05f) return false;
            }
            return true;
        }

        public static Vector2 ScreenCenter(RectTransform rt, Canvas canvas)
        {
            Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            Vector3 world = rt.TransformPoint(rt.rect.center);
            return RectTransformUtility.WorldToScreenPoint(cam, world);
        }

        private static bool OnScreen(Vector2 p) => p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;

        /// <summary>
        /// True when a mouse click at the element's centre would reach it (or, inside a scroll view, when the
        /// scroll view itself is reachable, because the element can be scrolled into view).
        /// </summary>
        private bool IsReachable(RectTransform rt, GameObject owner, ScrollRect scroll, EventSystem es)
        {
            if (es == null) return true;
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            Vector2 pos = ScreenCenter(rt, canvas);
            if (OnScreen(pos) && HitsOwner(pos, owner, es)) return true;
            if (scroll != null)
            {
                var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
                Vector2 vp = ScreenCenter(viewport, canvas);
                if (OnScreen(vp) && HitsOwner(vp, scroll.gameObject, es)) return true;
            }
            return false;
        }

        private bool HitsOwner(Vector2 screenPos, GameObject owner, EventSystem es)
        {
            var ped = new PointerEventData(es) { position = screenPos };
            _hits.Clear();
            try { es.RaycastAll(ped, _hits); }
            catch { return true; }
            if (_hits.Count == 0) return false;
            var top = _hits[0].gameObject;
            return top == owner || top.transform.IsChildOf(owner.transform);
        }

        // ---------------------------------------------------------------- input

        private void HandleInput()
        {
            if (KeyInput.TextFieldFocused)
            {
                // Typing into a field: leave every key to it except Escape/Enter handling by the field itself.
                return;
            }

            if (_items.Count == 0)
            {
                if (KeyInput.Repeat(Key.UpArrow) || KeyInput.Repeat(Key.DownArrow) || KeyInput.Pressed(Key.Enter))
                    Speech.SayFocus(Strings.NothingToNavigate);
                return;
            }

            if (KeyInput.Repeat(Key.DownArrow)) Move(1);
            else if (KeyInput.Repeat(Key.UpArrow)) Move(-1);
            else if (KeyInput.Pressed(Key.Home)) MoveTo(0);
            else if (KeyInput.Pressed(Key.End)) MoveTo(_items.Count - 1);
            else if (KeyInput.Repeat(Key.RightArrow)) Adjust(1);
            else if (KeyInput.Repeat(Key.LeftArrow)) Adjust(-1);
            else if (KeyInput.Pressed(Key.Enter) || KeyInput.Pressed(Key.NumpadEnter)) Activate();
            else if (KeyInput.Hotkey(ModConfig.KeyRepeat.Value)) ReadFocusedFull();
            else if (KeyInput.Hotkey(ModConfig.KeyInspect.Value)) ReadFocusedFull();
            else if (KeyInput.Hotkey(ModConfig.KeyReadScreen.Value)) ReadAll();
        }

        private void Move(int dir)
        {
            int idx = _focused != null ? _items.IndexOf(_focused) : -1;
            int next = idx + dir;
            if (idx < 0) next = dir > 0 ? 0 : _items.Count - 1;
            if (next < 0)
            {
                Speech.SayFocus(Strings.StartOfList + " " + Describe(_items[0]));
                return;
            }
            if (next >= _items.Count)
            {
                Speech.SayFocus(Strings.EndOfList + " " + Describe(_items[_items.Count - 1]));
                return;
            }
            MoveTo(next);
        }

        private void MoveTo(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            _focused = _items[index];
            if (_focused.Scroll != null) UiActions.ScrollIntoView(_focused.Rect, _focused.Scroll);
            SyncHover();
            Speech.SayFocus(Describe(_focused));
            _lastSpokenFocus = _focused.Go;
            UpdateDetails();
        }

        private void SyncHover()
        {
            if (_focused == null || _focused.Selectable == null)
            {
                UiActions.ClearHover();
                return;
            }
            UiActions.Hover(_focused.Go);
        }

        private void Adjust(int dir)
        {
            if (_focused == null) return;
            switch (_focused.Kind)
            {
                case UiItemKind.Slider:
                {
                    var s = (Slider)_focused.Selectable;
                    if (!s.IsInteractable()) { Speech.SayFocus(Strings.Unavailable); return; }
                    float step = s.wholeNumbers ? 1f : Math.Max((s.maxValue - s.minValue) / 20f, 0.0001f);
                    float before = s.value;
                    s.value = Mathf.Clamp(s.value + dir * step, s.minValue, s.maxValue);
                    if (Math.Abs(before - s.value) < 0.00001f)
                    {
                        Speech.SayFocus(dir > 0 ? Strings.Maximum : Strings.Minimum);
                        return;
                    }
                    // The label updates through the control's own onValueChanged listener.
                    Speech.SayFocus(Describe(_focused));
                    return;
                }
                case UiItemKind.Dropdown:
                {
                    var d = (TMP_Dropdown)_focused.Selectable;
                    if (d.options == null || d.options.Count == 0) return;
                    int v = Mathf.Clamp(d.value + dir, 0, d.options.Count - 1);
                    if (v == d.value) { Speech.SayFocus(dir > 0 ? Strings.EndOfList : Strings.StartOfList); return; }
                    d.value = v;
                    Speech.SayFocus(Describe(_focused));
                    return;
                }
                default:
                    Move(dir);
                    return;
            }
        }

        private void Activate()
        {
            if (_focused == null)
            {
                Speech.SayFocus(Strings.NothingFocused);
                return;
            }
            var item = _focused;
            if (item.Kind == UiItemKind.Custom)
            {
                item.CustomActivate?.Invoke();
                return;
            }
            if (!item.IsValid)
            {
                // The game rebuilt the list since the last refresh (0.25 s): never click a destroyed control.
                // Refresh moves focus to the same place in the new list; say where that is.
                Refresh();
                _nextRefresh = Time.unscaledTime + 0.25f;
                if (_focused != null && _focused.IsValid) { Speech.SayFocus(Describe(_focused)); _lastSpokenFocus = _focused.Go; }
                else Speech.SayFocus(Strings.NothingFocused);
                return;
            }
            if (item.Kind == UiItemKind.Text)
            {
                // Notification windows close on a click anywhere on them.
                var note = item.Go.GetComponentInParent<SecretHistories.UI.NotificationWindow>();
                if (note != null)
                {
                    UiActions.Click(note.gameObject);
                    _nextRefresh = 0;
                    return;
                }
                ReadFocusedFull();
                return;
            }
            if (!item.Interactable)
            {
                Speech.SayFocus(Strings.Unavailable);
                return;
            }
            switch (item.Kind)
            {
                case UiItemKind.InputField:
                {
                    var f = (TMP_InputField)item.Selectable;
                    if (f.GetComponentInParent<KeybindSettingControl>() != null)
                    {
                        BeginKeybindCapture(f);
                        return;
                    }
                    UiActions.Select(f.gameObject);
                    f.ActivateInputField();
                    Speech.Say(Strings.EditingField);
                    return;
                }
                case UiItemKind.Slider:
                    Speech.SayFocus(Strings.SliderHint);
                    return;
                case UiItemKind.Toggle:
                {
                    UiActions.Click(item.Go);
                    // Speak the new state after the toggle processed the click, plus what the choice means.
                    string extra = UiReader.GetActivationText(item);
                    Speech.SayFocus(TextCleaner.Sentences(new[] { Describe(item), extra }));
                    return;
                }
                default:
                {
                    string label = Describe(item);
                    UiActions.Click(item.Go);
                    Plugin.LogDebug("Activated: " + label);
                    _nextRefresh = 0;
                    // If the control is still there a moment later with a different label (a tab now selected,
                    // Pause now Unpause, "Restart" now asking for confirmation), say the new state.
                    _postActivationItem = item;
                    _postActivationLabel = label;
                    _postActivationContext = _contextKey;
                    _postActivationAt = Time.unscaledTime + 0.35f;
                    return;
                }
            }
        }

        // Key rebinding: selecting the field starts the game's interactive rebind, which waits for a key.
        private void BeginKeybindCapture(TMP_InputField field)
        {
            _pendingKeybind = field.gameObject;
            _capturingKeybind = true;
            _keybindStart = 0;
            InputGate.PassThrough = true;
            UiActions.Select(field.gameObject);
            Speech.Say(Strings.PressNewKey);
        }

        private float _keybindStart;

        private void UpdateKeybindCapture()
        {
            if (_keybindStart <= 0) _keybindStart = Time.unscaledTime;
            var field = _pendingKeybind != null ? _pendingKeybind.GetComponent<TMP_InputField>() : null;
            bool done = field == null || (!string.IsNullOrEmpty(TextCleaner.Clean(field.text)) && Time.unscaledTime - _keybindStart > 0.2f);
            bool timeout = Time.unscaledTime - _keybindStart > 15f;
            if (!done && !timeout) return;
            EndKeybindCapture();
            if (field != null) Speech.Say(Strings.KeyBoundTo(TextCleaner.Clean(field.text)));
        }

        private void EndKeybindCapture()
        {
            InputGate.PassThrough = false;
            _capturingKeybind = false;
            _pendingKeybind = null;
            _keybindStart = 0;
            UiActions.ClearSelection();
        }

        // ---------------------------------------------------------------- reading

        public string Describe(UiItem item)
        {
            if (item == null) return "";
            string label = UiReader.GetLabel(item);
            if (ModConfig.Level == Verbosity.Verbose && item.Selectable != null)
            {
                int idx = _items.IndexOf(item);
                if (idx >= 0) label = label + ", " + Strings.PositionOf(idx + 1, _items.Count);
            }
            return label;
        }

        private void UpdateDetails()
        {
            if (_focused == null) return;
            var lines = new List<string> { UiReader.GetLabel(_focused) };
            string hint = UiReader.GetHint(_focused.Go);
            if (!string.IsNullOrEmpty(hint)) lines.Add(hint);
            lines.AddRange(TextCleaner.SplitIntoSpeechItems(UiReader.GetActivationText(_focused)));
            BufferManager.SetDetails(lines);
        }

        public void ReadFocusedFull()
        {
            if (_focused == null)
            {
                Speech.Say(Strings.NothingFocused);
                return;
            }
            var parts = new List<string> { UiReader.GetLabel(_focused) };
            string hint = UiReader.GetHint(_focused.Go);
            if (!string.IsNullOrEmpty(hint)) parts.Add(hint);
            parts.Add(UiReader.GetActivationText(_focused));
            Speech.Say(TextCleaner.Sentences(parts));
        }

        public void ReadAll()
        {
            if (_items.Count == 0)
            {
                Speech.Say(Strings.NothingToNavigate);
                return;
            }
            Speech.Say(TextCleaner.Sentences(_items.Select(i => UiReader.GetLabel(i))));
        }

        public void DumpToLog()
        {
            Plugin.LogInfo("---- UI navigator: " + _items.Count + " items, focused: " + (_focused != null ? _focused.Go.name : "none") + ", title: " + FindContextTitle());
            foreach (var i in _items)
            {
                Plugin.LogInfo("  [" + i.Kind + "] " + PathOf(i.Go.transform) + " window=" + (i.Window != null ? i.Window.name : "-") + " :: " + UiReader.GetLabel(i));
            }
        }

        public static string PathOf(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        /// <summary>Forces a context announcement on the next refresh (screen entered, overlay changed).</summary>
        public void RequestAnnouncement()
        {
            _announcePending = true;
            _nextRefresh = 0;
        }
    }
}
