using System;
using System.Collections.Generic;
using System.Linq;
using CultistAccessibility.Core;
using CultistAccessibility.Core.Buffers;
using CultistAccessibility.Navigation;
using HarmonyLib;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Otherworlds;
using SecretHistories.Spheres;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CultistAccessibility.Tabletop
{
    /// <summary>
    /// Keyboard layer for the table (Cultist Simulator is mouse-only here, so the mod owns navigation).
    /// Board mode: groups Verbs / Cards / Controls (Left/Right), items (Up/Down), Enter acts.
    /// Window mode follows the game's open verb window: story text, slots, Start, results, Collect all.
    /// Mansus mode: the face-down choices of the Mansus. Pickers are modal lists on top of any mode.
    /// The mode is derived from game state every frame, never from key presses, so it cannot go stale.
    /// </summary>
    internal sealed class TabletopNavigator
    {
        private enum Mode
        {
            Loading,
            Board,
            Window,
            Mansus
        }

        private const int GroupVerbs = 0;
        private const int GroupCards = 1;
        private const int GroupControls = 2;

        private static readonly AccessTools.FieldRef<Numa, Otherworld> CurrentOtherworldRef =
            UiReader.SafeFieldRef<Numa, Otherworld>("_currentOtherworld");

        private Mode _mode = Mode.Loading;
        private int _group;
        private readonly object[] _groupFocus = new object[3];
        private Situation _windowSituation;
        private object _windowFocus;
        private object _mansusFocus;
        private Picker _picker;
        private float _readySince = -1f;
        private bool _announcedTable;
        private bool _suspended;
        private string _lastPrediction;
        private StateEnum _lastWindowState;
        private float _nextWindowPoll;
        private float _nextUiCheck;
        private bool _uiWanted;
        private readonly TabletopEvents _events = new TabletopEvents();
        private int _completedCycle;

        public TabletopEvents Events => _events;

        // ------------------------------------------------------------------ lifecycle

        public void OnScreenChanged(GameScreen to)
        {
            ClosePicker();
            _mode = Mode.Loading;
            _group = GroupVerbs;
            for (int i = 0; i < _groupFocus.Length; i++) _groupFocus[i] = null;
            _windowSituation = null;
            _windowFocus = null;
            _mansusFocus = null;
            _readySince = -1f;
            _announcedTable = false;
            _suspended = false;
            _lastPrediction = "0000";
            _events.Reset();
            InputGate.PickerOpen = false;
        }

        private static readonly AccessTools.FieldRef<Notifier, NotificationWindow> NotifierSaveErrorRef =
            UiReader.SafeFieldRef<Notifier, NotificationWindow>("SaveErrorWindow");

        /// <summary>The options menu or a blocking dialog is up: the generic uGUI navigator takes over.</summary>
        public bool WantsUiNavigator()
        {
            if (Time.unscaledTime < _nextUiCheck) return _uiWanted;
            _nextUiCheck = Time.unscaledTime + 0.15f;
            _uiWanted = false;
            try
            {
                var options = UnityEngine.Object.FindObjectOfType<OptionsPanel>();
                if (options != null && options.IsVisible()) _uiWanted = true;
                // The table's save error dialog is the Notifier's own NotificationWindow field (not the SaveErrorWindow
                // class, which only the save/load panel uses); it has Continue and Reload buttons.
                var notifier = Watchman.Get<Notifier>();
                var saveError = notifier != null && NotifierSaveErrorRef != null ? NotifierSaveErrorRef(notifier) : null;
                if (saveError != null && saveError.gameObject.activeInHierarchy) _uiWanted = true;
                var saveErrorClass = UnityEngine.Object.FindObjectOfType<SaveErrorWindow>();
                if (saveErrorClass != null && saveErrorClass.isActiveAndEnabled) _uiWanted = true;
            }
            catch { }
            return _uiWanted;
        }

        public void SuspendForUi()
        {
            if (_suspended) return;
            _suspended = true;
            ClosePicker();
        }

        /// <summary>Runs every frame on the table whatever owns the keyboard: events and the arrival announcement.</summary>
        public void Tick()
        {
            if (!GameAccess.TableReady)
            {
                _readySince = -1f;
                return;
            }
            if (_readySince < 0) _readySince = Time.unscaledTime;
            _events.Tick();
            if (!_announcedTable && Time.unscaledTime - _readySince > 1.2f)
            {
                _announcedTable = true;
                _events.Arm();
                AnnounceTable();
            }
        }

        public void Update(bool handleInput)
        {
            if (_suspended)
            {
                _suspended = false;
                // Back from the options menu: re-announce where we are.
                if (_announcedTable) Speech.Say(CurrentSummary());
            }
            if (!_announcedTable) return;
            SyncMode();

            if (_picker != null && _picker.IsOpen)
            {
                if (handleInput) _picker.HandleInput();
                return;
            }

            if (_mode == Mode.Window) PollWindow();
            if (_pendingPortalReport > 0 && Time.unscaledTime >= _pendingPortalReport)
            {
                _pendingPortalReport = 0;
                ReportPortals();
            }
            if (!handleInput) return;

            if (HandleCommonHotkeys()) return;
            if (DeveloperTools.HandleInput()) return;
            switch (_mode)
            {
                case Mode.Board:
                    HandleBoardInput();
                    break;
                case Mode.Window:
                    HandleWindowInput();
                    break;
                case Mode.Mansus:
                    HandleMansusInput();
                    break;
            }
        }

        private float _pendingPortalReport;

        /// <summary>After the Mansus: read the dream's journal text and say where the chosen card waits.</summary>
        private void ReportPortals()
        {
            foreach (var portal in GameAccess.TablePortals())
            {
                var cards = GameAccess.PortalCards(portal);
                if (cards.Count == 0) continue;
                string text = TextCleaner.CleanMultiline(portal.Payload.Description);
                if (!string.IsNullOrEmpty(text)) BufferManager.AddStory(TextCleaner.Clean(portal.Payload.Label) + ": " + text);
                string names = string.Join(", ", cards.Select(Describer.CardSummary).ToArray());
                Speech.SayEvent(TextCleaner.Sentences(new[] { text, Strings.PortalWaiting(names) }));
                _group = GroupVerbs;
                _groupFocus[GroupVerbs] = portal;
            }
        }

        private void ClosePicker()
        {
            if (_picker != null && _picker.IsOpen) _picker.Close();
            _picker = null;
        }

        private void AnnounceTable()
        {
            var verbs = GameAccess.TableSituations();
            var cards = GameAccess.TableCards();
            var parts = new List<string> { Strings.ScreenName(GameScreen.Tabletop) };
            parts.Add(verbs.Count + (verbs.Count == 1 ? " verb" : " verbs") + ", " + cards.Count + (cards.Count == 1 ? " card" : " cards"));
            var busy = verbs.Where(v => v.StateIdentifier == StateEnum.Ongoing).ToList();
            if (busy.Count > 0) parts.Add(busy.Count + " busy");
            var done = verbs.Where(v => v.StateIdentifier == StateEnum.Complete).ToList();
            if (done.Count > 0) parts.Add(done.Count + " with results waiting");
            try { if (GameAccess.Heart.IsPaused()) parts.Add(Strings.Paused); } catch { }
            if (ModConfig.SpeakHints.Value) parts.Add("Arrows move, left and right switch between verbs, cards and controls, Enter acts, F1 for help.");
            SyncMode();
            string first = CurrentSummary();
            if (!string.IsNullOrEmpty(first) && _mode == Mode.Board) parts.Add(first);
            Speech.Say(TextCleaner.Sentences(parts));
        }

        // ------------------------------------------------------------------ mode tracking

        private void SyncMode()
        {
            Mode newMode;
            Situation open = null;
            if (GameAccess.MansusActive) newMode = Mode.Mansus;
            else
            {
                open = GameAccess.OpenSituation();
                newMode = open != null ? Mode.Window : Mode.Board;
            }

            if (newMode == _mode && (newMode != Mode.Window || open == _windowSituation)) return;

            Mode old = _mode;
            Situation oldWindow = _windowSituation;
            _mode = newMode;
            ClosePicker();

            switch (newMode)
            {
                case Mode.Window:
                    _windowSituation = open;
                    // Keep focus on a slot we just filled (placing a card opens the window); otherwise start fresh.
                    if (!(_windowFocus is Sphere focusedSlot && ReferenceEquals(focusedSlot.GetContainer(), open))) _windowFocus = null;
                    _lastPrediction = PredictionKey(open);
                    _lastWindowState = open.StateIdentifier;
                    AnnounceWindow(open);
                    break;
                case Mode.Board:
                    _windowSituation = null;
                    if (old == Mode.Window && oldWindow != null)
                    {
                        _group = GroupVerbs;
                        _groupFocus[GroupVerbs] = oldWindow;
                        Speech.Say(Strings.WindowClosed + " " + CurrentSummary());
                    }
                    else if (old == Mode.Mansus)
                    {
                        _pendingPortalReport = Time.unscaledTime + 1.0f;
                        Speech.SayEvent(Strings.MansusLeft);
                    }
                    break;
                case Mode.Mansus:
                    _windowSituation = null;
                    _mansusFocus = null;
                    AnnounceMansus();
                    break;
            }
        }

        private string CurrentSummary()
        {
            switch (_mode)
            {
                case Mode.Board:
                {
                    var list = BuildGroup(_group);
                    var e = FindFocused(list, _groupFocus[_group]);
                    return e != null ? e.Summary() : EmptyGroupText(_group);
                }
                case Mode.Window:
                {
                    var list = BuildWindow(_windowSituation);
                    var e = FindFocused(list, _windowFocus);
                    return e != null ? e.Summary() : "";
                }
                case Mode.Mansus:
                {
                    var list = BuildMansus();
                    var e = FindFocused(list, _mansusFocus);
                    return e != null ? e.Summary() : Strings.MansusNoCards;
                }
            }
            return "";
        }

        // ------------------------------------------------------------------ list helpers

        private static NavEntry FindFocused(List<NavEntry> list, object key)
        {
            if (list.Count == 0) return null;
            if (key != null)
            {
                var e = list.FirstOrDefault(x => Equals(x.Key, key));
                if (e != null) return e;
            }
            return list[0];
        }

        /// <summary>Moves focus within a list; returns the new focus key. Never silent.</summary>
        private static object MoveIn(List<NavEntry> list, object key, int dir, bool toEnd = false)
        {
            if (list.Count == 0) return null;
            int idx = key != null ? list.FindIndex(x => Equals(x.Key, key)) : -1;
            int next;
            if (toEnd) next = dir < 0 ? 0 : list.Count - 1;
            else if (idx < 0) next = 0;
            else next = idx + dir;
            if (next < 0)
            {
                Speech.SayFocus(Strings.StartOfList + " " + list[0].Summary());
                return list[0].Key;
            }
            if (next >= list.Count)
            {
                Speech.SayFocus(Strings.EndOfList + " " + list[list.Count - 1].Summary());
                return list[list.Count - 1].Key;
            }
            var e = list[next];
            string text = e.Summary();
            if (ModConfig.Level == Verbosity.Verbose) text += ", " + Strings.PositionOf(next + 1, list.Count);
            Speech.SayFocus(text);
            FocusSideEffects(e);
            return e.Key;
        }

        private static void FocusSideEffects(NavEntry e)
        {
            if (e == null) return;
            try
            {
                if (e.Details != null) BufferManager.SetDetails(e.Details());
            }
            catch { }
            if (e.Token != null) GameAccess.PointCameraAt(e.Token);
        }

        private static void Inspect(NavEntry e)
        {
            if (e == null)
            {
                Speech.Say(Strings.NothingFocused);
                return;
            }
            List<string> lines = null;
            try { lines = e.Details?.Invoke(); } catch { }
            if (lines == null || lines.Count == 0) lines = new List<string> { e.Summary() };
            BufferManager.SetDetails(lines);
            Speech.Say(TextCleaner.Sentences(lines));
        }

        // ------------------------------------------------------------------ common hotkeys

        private bool HandleCommonHotkeys()
        {
            if (KeyInput.Hotkey(ModConfig.KeyStatus.Value))
            {
                Speech.Say(StatusReader.StatusText());
                return true;
            }
            if (KeyInput.Hotkey(ModConfig.KeyTimers.Value))
            {
                Speech.Say(StatusReader.TimersText());
                return true;
            }
            if (KeyInput.Hotkey(ModConfig.KeyNextCompleted.Value))
            {
                FocusNextCompleted();
                return true;
            }
            return false;
        }

        private void FocusNextCompleted()
        {
            var done = GameAccess.TableSituations().Where(s => s.StateIdentifier == StateEnum.Complete).ToList();
            if (done.Count == 0)
            {
                Speech.Say(Strings.NothingCompleted);
                return;
            }
            // From an open window, go to another finished verb; if this one is the only one, say so in place
            // (switching to the board with the window still open would announce the window again).
            bool inWindow = _mode == Mode.Window && _windowSituation != null;
            if (inWindow && done.Count > 1) done.Remove(_windowSituation);
            var s = done[_completedCycle % done.Count];
            _completedCycle = (_completedCycle + 1) % 1000;
            if (inWindow && _windowSituation == s)
            {
                Speech.Say(Describer.VerbSummary(s));
                return;
            }
            if (_mode == Mode.Window && _windowSituation != null && _windowSituation != s)
            {
                GameActions.CloseSituation(_windowSituation);
            }
            _mode = Mode.Board;
            _windowSituation = null;
            _group = GroupVerbs;
            _groupFocus[GroupVerbs] = s;
            var entry = FindFocused(BuildGroup(GroupVerbs), s);
            Speech.Say(entry != null ? entry.Summary() : Describer.VerbSummary(s));
            FocusSideEffects(entry);
        }

        // ------------------------------------------------------------------ board

        private List<NavEntry> BuildGroup(int group)
        {
            switch (group)
            {
                case GroupVerbs: return BuildVerbs();
                case GroupCards: return BuildCards();
                default: return BuildControls();
            }
        }

        private static string GroupName(int group)
        {
            switch (group)
            {
                case GroupVerbs: return Strings.GroupVerbs;
                case GroupCards: return Strings.GroupCards;
                default: return Strings.GroupControls;
            }
        }

        private static string EmptyGroupText(int group)
        {
            switch (group)
            {
                case GroupVerbs: return Strings.NoVerbs;
                case GroupCards: return Strings.NoCards;
                default: return Strings.NothingToNavigate;
            }
        }

        private List<NavEntry> BuildVerbs()
        {
            var verbs = GameAccess.TableSituations();
            // Reading order on the table: top row first, left to right.
            verbs.Sort((a, b) =>
            {
                Vector3 pa = a.Token.transform.position, pb = b.Token.transform.position;
                if (Math.Abs(pa.y - pb.y) > 40f) return pb.y.CompareTo(pa.y);
                return pa.x.CompareTo(pb.x);
            });
            var list = new List<NavEntry>();
            foreach (var s in verbs)
            {
                var sit = s;
                list.Add(new NavEntry
                {
                    Key = sit,
                    Token = sit.Token,
                    Summary = () => Describer.VerbSummary(sit),
                    Details = () => Describer.VerbDetails(sit),
                    Activate = () => GameActions.OpenSituation(sit)
                });
            }
            foreach (var p in GameAccess.TablePortals())
            {
                var portal = p;
                list.Add(new NavEntry
                {
                    Key = portal,
                    Token = portal,
                    Summary = () => Strings.PortalSummary(TextCleaner.Clean(portal.Payload.Label), GameAccess.PortalCards(portal).Sum(c => c.Quantity)),
                    Details = () => PortalDetails(portal),
                    Activate = () => OpenPortalPicker(portal)
                });
            }
            return list;
        }

        private static List<string> PortalDetails(Token portal)
        {
            var lines = new List<string> { Strings.PortalSummary(TextCleaner.Clean(portal.Payload.Label), GameAccess.PortalCards(portal).Sum(c => c.Quantity)) };
            lines.AddRange(TextCleaner.SplitIntoSpeechItems(portal.Payload.Description));
            foreach (var c in GameAccess.PortalCards(portal)) lines.Add(Describer.CardSummary(c));
            return lines;
        }

        private void OpenPortalPicker(Token portal)
        {
            var options = new List<PickerOption>();
            string text = TextCleaner.CleanMultiline(portal.Payload.Description);
            if (!string.IsNullOrEmpty(text))
            {
                options.Add(new PickerOption
                {
                    Label = Strings.PortalReadText,
                    Details = () => PortalDetails(portal),
                    OnChoose = () => Speech.Say(text)
                });
            }
            foreach (var c in GameAccess.PortalCards(portal))
            {
                var card = c;
                options.Add(new PickerOption
                {
                    Label = Strings.PortalTake(Describer.CardSummary(card)),
                    Details = () => Describer.CardDetails(card),
                    OnChoose = () =>
                    {
                        string n = Describer.CardName(card);
                        if (card.Payload.IsShrouded) GameActions.Unshroud(card);
                        if (GameActions.TakeToTable(card)) Speech.Say(Strings.TakenToTable(n));
                    }
                });
            }
            options.Add(new PickerOption
            {
                Label = Strings.PortalCollectAll,
                Details = () => PortalDetails(portal),
                OnChoose = () =>
                {
                    try { portal.Payload.Conclude(); } catch (Exception ex) { Plugin.LogError("Portal conclude failed: " + ex); }
                    Speech.Say(Strings.CollectedToTable);
                }
            });
            _picker = new Picker(Strings.PortalSummary(TextCleaner.Clean(portal.Payload.Label), GameAccess.PortalCards(portal).Sum(c => c.Quantity)) + ".", options);
            _picker.Open();
        }

        private List<NavEntry> BuildCards()
        {
            var cards = GameAccess.TableCards();
            cards = cards
                .OrderBy(t => t.Payload.IsShrouded ? 1 : 0)
                .ThenBy(t => Describer.CardName(t), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.Payload.GetTimeshadow() != null && t.Payload.GetTimeshadow().Transient ? t.Payload.GetTimeshadow().LifetimeRemaining : float.MaxValue)
                .ToList();
            var list = new List<NavEntry>();
            foreach (var t in cards)
            {
                var card = t;
                list.Add(new NavEntry
                {
                    Key = card,
                    Token = card,
                    Summary = () => Describer.CardSummary(card),
                    Details = () => Describer.CardDetails(card),
                    Activate = () => OpenVerbPicker(card)
                });
            }
            return list;
        }

        private List<NavEntry> BuildControls()
        {
            var list = new List<NavEntry>();
            list.Add(new NavEntry
            {
                Key = "status",
                Summary = StatusReader.StatusText,
                Details = () => new List<string> { StatusReader.StatusText(), StatusReader.TimersText() },
                Activate = () => Speech.Say(StatusReader.StatusText())
            });
            try
            {
                var bar = UnityEngine.Object.FindObjectOfType<StatusBar>();
                if (bar != null)
                {
                    var items = new List<UiItem>();
                    int order = 0;
                    foreach (var sel in bar.GetComponentsInChildren<Selectable>(false))
                    {
                        if (!sel.isActiveAndEnabled) continue;
                        var canvas = sel.GetComponentInParent<Canvas>();
                        var rt = (RectTransform)sel.transform;
                        items.Add(new UiItem
                        {
                            Go = sel.gameObject,
                            Selectable = sel,
                            Kind = sel is TMP_InputField ? UiItemKind.InputField : UiItemKind.Button,
                            Rect = rt,
                            Canvas = canvas,
                            ScreenPos = UiNavigator.ScreenCenter(rt, canvas),
                            Order = order++
                        });
                    }
                    UiNavigator.SortReadingOrder(items);
                    foreach (var it in items)
                    {
                        var item = it;
                        list.Add(new NavEntry
                        {
                            Key = item.Go,
                            Summary = () => UiReader.GetLabel(item),
                            Details = () => new List<string> { UiReader.GetLabel(item) },
                            Activate = () => ActivateControl(item)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("Controls group failed: " + ex.Message);
            }
            return list;
        }

        private static void ActivateControl(UiItem item)
        {
            if (item.Selectable is TMP_InputField field)
            {
                UiActions.Select(field.gameObject);
                field.ActivateInputField();
                Speech.Say(Strings.EditingField);
                return;
            }
            if (!item.Interactable)
            {
                Speech.Say(Strings.Unavailable);
                return;
            }
            UiActions.Click(item.Go);
        }

        private void HandleBoardInput()
        {
            if (KeyInput.Repeat(Key.DownArrow)) MoveBoard(1);
            else if (KeyInput.Repeat(Key.UpArrow)) MoveBoard(-1);
            else if (KeyInput.Pressed(Key.Home)) MoveBoard(-1, toEnd: true);
            else if (KeyInput.Pressed(Key.End)) MoveBoard(1, toEnd: true);
            else if (KeyInput.Pressed(Key.RightArrow)) SwitchGroup(1);
            else if (KeyInput.Pressed(Key.LeftArrow)) SwitchGroup(-1);
            else if (KeyInput.Pressed(Key.Enter) || KeyInput.Pressed(Key.NumpadEnter))
            {
                var e = FindFocused(BuildGroup(_group), _groupFocus[_group]);
                if (e == null) Speech.Say(EmptyGroupText(_group));
                else e.Activate?.Invoke();
            }
            else if (KeyInput.Hotkey(ModConfig.KeyInspect.Value))
            {
                Inspect(FindFocused(BuildGroup(_group), _groupFocus[_group]));
            }
            else if (KeyInput.Hotkey(ModConfig.KeyRepeat.Value))
            {
                var e = FindFocused(BuildGroup(_group), _groupFocus[_group]);
                Speech.Say(e != null ? e.Summary() : EmptyGroupText(_group));
            }
            else if (KeyInput.Hotkey(ModConfig.KeyReadScreen.Value))
            {
                var list = BuildGroup(_group);
                if (list.Count == 0) Speech.Say(EmptyGroupText(_group));
                else Speech.Say(GroupName(_group) + ". " + TextCleaner.Sentences(list.Select(x => x.Summary())));
            }
        }

        private void MoveBoard(int dir, bool toEnd = false)
        {
            var list = BuildGroup(_group);
            if (list.Count == 0)
            {
                Speech.SayFocus(EmptyGroupText(_group));
                return;
            }
            _groupFocus[_group] = MoveIn(list, _groupFocus[_group], dir, toEnd);
        }

        private void SwitchGroup(int dir)
        {
            _group = (_group + dir + 3) % 3;
            var list = BuildGroup(_group);
            var e = FindFocused(list, _groupFocus[_group]);
            if (e != null) _groupFocus[_group] = e.Key;
            string text = Strings.GroupAnnounce(GroupName(_group), list.Count) + ". " + (e != null ? e.Summary() : EmptyGroupText(_group));
            Speech.SayFocus(text);
            FocusSideEffects(e);
        }

        // ------------------------------------------------------------------ pickers

        private void OpenVerbPicker(Token card)
        {
            if (card == null || card.Defunct) return;
            if (card.Payload.IsShrouded)
            {
                // A click on a face-down card turns it over (Token.OnPointerClick); do the same.
                GameActions.Unshroud(card);
                Speech.Say(Describer.CardSummary(card));
                BufferManager.SetDetails(Describer.CardDetails(card));
                return;
            }
            if (!card.CanBeDragged())
            {
                Speech.Say(Strings.CannotMove);
                return;
            }
            string name = Describer.CardName(card);
            var targets = GameActions.VerbsAccepting(card);
            if (targets.Count == 0)
            {
                Speech.Say(Strings.NoVerbAccepts);
                return;
            }
            var options = new List<PickerOption>();
            foreach (var kv in targets)
            {
                var s = kv.Key;
                var slot = kv.Value;
                options.Add(new PickerOption
                {
                    Label = TextCleaner.Join(Describer.VerbName(s), Describer.SlotLabel(slot) + " " + Strings.SlotWord, Describer.StateText(s)),
                    Details = () => Describer.VerbDetails(s),
                    OnChoose = () => PlaceAndReport(card, slot, s)
                });
            }
            _picker = new Picker(Strings.PickVerbFor(name, targets.Count), options);
            _picker.Open();
        }

        private void OpenCardPicker(Situation s, Sphere slot)
        {
            var options = new List<PickerOption>();
            var occupant = slot.GetElementTokens().FirstOrDefault();
            if (occupant != null)
            {
                string occName = Describer.CardName(occupant);
                options.Add(new PickerOption
                {
                    Label = Strings.EmptyTheSlot + ": " + occName,
                    Details = () => Describer.CardDetails(occupant),
                    OnChoose = () => EmptySlotAndReport(slot)
                });
            }
            var cards = GameAccess.TableCards()
                .Where(t => GameActions.CanPlace(t, slot))
                .OrderBy(t => Describer.CardName(t), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            foreach (var t in cards)
            {
                var card = t;
                options.Add(new PickerOption
                {
                    Label = Describer.CardSummary(card),
                    Details = () => Describer.CardDetails(card),
                    OnChoose = () => PlaceAndReport(card, slot, s)
                });
            }
            if (options.Count == 0)
            {
                string rules = Describer.SlotSpecText(slot.GoverningSphereSpec);
                Speech.Say(Strings.NoCardFits + (string.IsNullOrEmpty(rules) ? "" : " " + rules));
                return;
            }
            _picker = new Picker(Strings.PickCardFor(Describer.SlotLabel(slot), cards.Count), options);
            _picker.Open();
        }

        private void PlaceAndReport(Token card, Sphere slot, Situation s)
        {
            string name = Describer.CardName(card);
            string slotName = Describer.SlotLabel(slot);
            if (!GameActions.PlaceInSlot(card, slot)) return;
            Speech.Say(Strings.Placed(name, slotName));
            // Placing opens the verb window (Situation.OnTokensChangedForSphere); keep focus on the slot.
            _windowFocus = slot;
            _lastPrediction = "0000";
        }

        private void EmptySlotAndReport(Sphere slot)
        {
            var card = GameActions.EmptySlot(slot);
            if (card != null) Speech.Say(Strings.Removed(Describer.CardName(card)));
            _lastPrediction = "0000";
        }

        // ------------------------------------------------------------------ window

        private void AnnounceWindow(Situation s)
        {
            var parts = new List<string> { Describer.VerbName(s), Describer.StateText(s) };
            if (s.StateIdentifier == StateEnum.Ongoing) parts.Add(Strings.TimeLeft(GameAccess.FormatTime(s.TimeRemaining)));
            string recipe = Describer.RecipeLabel(s);
            string text = Describer.RecipeText(s);
            var slots = GameAccess.ActiveThresholds(s);
            string opening = TextCleaner.Join(parts.ToArray());
            var speech = new List<string> { opening };
            if (!string.IsNullOrEmpty(recipe) && recipe != Describer.VerbName(s)) speech.Add(recipe);
            if (!string.IsNullOrEmpty(text)) speech.Add(text);
            if (slots.Count > 0) speech.Add(slots.Count == 1 ? "1 slot" : slots.Count + " slots");
            if (s.StateIdentifier == StateEnum.Complete)
            {
                int n = Describer.OutputCount(s);
                if (n > 0) speech.Add(Strings.CardsWaiting(n));
            }
            if (!string.IsNullOrEmpty(text)) BufferManager.AddStory(Describer.VerbName(s) + ": " + (string.IsNullOrEmpty(recipe) ? "" : recipe + ". ") + text);
            var list = BuildWindow(s);
            // Default focus: the first empty slot when setting up, the first result when done, else the story.
            NavEntry first = null;
            if (_windowFocus != null) first = list.FirstOrDefault(x => Equals(x.Key, _windowFocus));
            if (first == null) first = list.FirstOrDefault(x => x.Key is Sphere sp && !sp.GetElementTokens().Any());
            if (first == null && s.StateIdentifier == StateEnum.Complete) first = list.FirstOrDefault(x => x.Key is Token);
            if (first == null) first = list.FirstOrDefault();
            _windowFocus = first?.Key;
            if (first != null && !Equals(first.Key, "header")) speech.Add(first.Summary());
            Speech.Say(TextCleaner.Sentences(speech));
            FocusSideEffects(first);
        }

        private List<NavEntry> BuildWindow(Situation s)
        {
            var list = new List<NavEntry>();
            if (s == null || s.Defunct) return list;

            list.Add(new NavEntry
            {
                Key = "header",
                Token = s.Token,
                Summary = () => WindowHeader(s),
                Details = () => Describer.VerbDetails(s),
                Activate = () => ReadStory(s),
                Adjust = dir => TurnPage(s, dir)
            });

            foreach (var slot in GameAccess.ActiveThresholds(s))
            {
                var sp = slot;
                list.Add(new NavEntry
                {
                    Key = sp,
                    Summary = () => Describer.SlotSummary(sp),
                    Details = () => Describer.SlotDetails(sp),
                    Activate = () => OpenCardPicker(s, sp),
                    Delete = () =>
                    {
                        if (!sp.GetElementTokens().Any()) Speech.Say(Strings.SlotNotEmpty);
                        else EmptySlotAndReport(sp);
                    }
                });
            }

            switch (s.StateIdentifier)
            {
                case StateEnum.Unstarted:
                    list.Add(new NavEntry
                    {
                        Key = "start",
                        Summary = () => Describer.CanStart(s) ? Strings.StartButton + ", " + Describer.RecipeLabel(s) : Strings.StartUnavailable,
                        Details = () => Describer.VerbDetails(s),
                        Activate = () => GameActions.TryStart(s)
                    });
                    break;
                case StateEnum.Ongoing:
                case StateEnum.Starting:
                    foreach (var card in GameAccess.SphereCards(GameAccess.DominionSpheres(s, SituationDominionEnum.Storage)))
                    {
                        var c = card;
                        list.Add(new NavEntry
                        {
                            Key = c,
                            Summary = () => Strings.StoredHeading + ": " + Describer.CardSummary(c),
                            Details = () => Describer.CardDetails(c),
                            Activate = () => Inspect(new NavEntry { Summary = () => Describer.CardSummary(c), Details = () => Describer.CardDetails(c) })
                        });
                    }
                    string deck = Describer.DeckEffects(s);
                    if (!string.IsNullOrEmpty(deck))
                    {
                        list.Add(new NavEntry
                        {
                            Key = "deck",
                            Summary = () => Describer.DeckEffects(s),
                            Details = () => new List<string> { Describer.DeckEffects(s) },
                            Activate = () => Speech.Say(Describer.DeckEffects(s))
                        });
                    }
                    break;
                case StateEnum.Complete:
                    foreach (var card in GameAccess.SphereCards(GameAccess.DominionSpheres(s, SituationDominionEnum.Output)))
                    {
                        var c = card;
                        list.Add(new NavEntry
                        {
                            Key = c,
                            Token = null,
                            Summary = () => Describer.CardSummary(c),
                            Details = () => Describer.CardDetails(c),
                            Activate = () =>
                            {
                                if (c.Payload.IsShrouded)
                                {
                                    // Results arrive face down (OutputSphere.AlwaysShroudIncomingTokens); a click turns them over.
                                    GameActions.Unshroud(c);
                                    Speech.Say(Describer.CardSummary(c));
                                    BufferManager.SetDetails(Describer.CardDetails(c));
                                    return;
                                }
                                string n = Describer.CardName(c);
                                if (GameActions.TakeToTable(c)) Speech.Say(Strings.TakenToTable(n));
                            }
                        });
                    }
                    list.Add(new NavEntry
                    {
                        Key = "collect",
                        Summary = () => Strings.CollectAll + (Describer.OutputCount(s) > 0 ? ", " + Strings.CardsWaiting(Describer.OutputCount(s)) : ""),
                        Details = () => Describer.VerbDetails(s),
                        Activate = () =>
                        {
                            // The cards are named as they land face up on the table (CardPatches arrivals).
                            GameActions.Collect(s);
                            Speech.Say(Strings.CollectedToTable);
                        }
                    });
                    break;
            }

            string aspects = Describer.AspectSummary(Describer.SituationAspects(s));
            if (!string.IsNullOrEmpty(aspects))
            {
                list.Add(new NavEntry
                {
                    Key = "aspects",
                    Summary = () => Strings.Aspects + ": " + Describer.AspectSummary(Describer.SituationAspects(s)),
                    Details = () =>
                    {
                        var a = Describer.SituationAspects(s);
                        var lines = new List<string> { Strings.Aspects + ": " + Describer.AspectSummary(a) };
                        lines.AddRange(Describer.AspectExplanations(a));
                        return lines;
                    },
                    Activate = () => Speech.Say(Strings.Aspects + ": " + Describer.AspectSummary(Describer.SituationAspects(s)))
                });
            }
            return list;
        }

        private static string WindowHeader(Situation s)
        {
            var parts = new List<string> { Describer.VerbName(s), Describer.StateText(s) };
            if (s.StateIdentifier == StateEnum.Ongoing || s.StateIdentifier == StateEnum.Starting)
                parts.Add(Strings.TimeLeft(GameAccess.FormatTime(s.TimeRemaining)));
            string recipe = Describer.RecipeLabel(s);
            if (!string.IsNullOrEmpty(recipe) && recipe != Describer.VerbName(s)) parts.Add(recipe);
            int pages = Describer.NotesPageCount(s);
            if (pages > 1) parts.Add(Strings.PageOf(Describer.NotesPageIndex(s) + 1, pages));
            return TextCleaner.Join(parts.ToArray());
        }

        private static void ReadStory(Situation s)
        {
            string recipe = Describer.RecipeLabel(s);
            string text = Describer.RecipeText(s);
            Speech.Say(TextCleaner.Sentences(new[] { recipe, text }));
        }

        private static void TurnPage(Situation s, int dir)
        {
            var notes = GameAccess.GetNotes(s);
            if (notes == null || notes.NoteCount <= 1)
            {
                Speech.Say(Strings.NoMorePages);
                return;
            }
            int target = notes.CurrentIndex + dir;
            if (target < 0 || target >= notes.NoteCount)
            {
                Speech.Say(dir < 0 ? Strings.StartOfList : Strings.EndOfList);
                return;
            }
            try
            {
                if (dir < 0) notes.ShowPrevPage();
                else notes.ShowNextPage();
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("Page turn failed: " + ex.Message);
            }
            // Read the page we asked for directly; the game's page animation finishes later.
            Token page = null;
            try
            {
                page = notes.Tokens.ElementAtOrDefault(target);
            }
            catch { }
            string text = page != null ? TextCleaner.Sentences(new[] { page.Payload.MetafictionalLabel, page.Payload.MetafictionalDescription }) : ReadStoryText(s);
            Speech.Say(Strings.PageOf(target + 1, notes.NoteCount) + ". " + text);
        }

        private static string ReadStoryText(Situation s) => TextCleaner.Sentences(new[] { Describer.RecipeLabel(s), Describer.RecipeText(s) });

        private static string PredictionKey(Situation s)
        {
            if (s == null) return "";
            return Describer.RecipeLabel(s) + "|" + Describer.CanStart(s);
        }

        /// <summary>While a window is open, speak changes the player cannot see: the recipe prediction and state.</summary>
        private void PollWindow()
        {
            if (Time.unscaledTime < _nextWindowPoll) return;
            _nextWindowPoll = Time.unscaledTime + 0.3f;
            var s = _windowSituation;
            if (s == null || s.Defunct) return;
            if (s.StateIdentifier != _lastWindowState)
            {
                var previous = _lastWindowState;
                _lastWindowState = s.StateIdentifier;
                _lastPrediction = PredictionKey(s);
                // The events poller announces starts and completions; refocus sensibly.
                var list = BuildWindow(s);
                if (FindFocused(list, _windowFocus)?.Key != _windowFocus) _windowFocus = list.FirstOrDefault()?.Key;
                if (s.StateIdentifier == StateEnum.Unstarted && previous == StateEnum.Complete)
                {
                    // Results collected with the window still open: it is ready for new cards.
                    var slot = list.FirstOrDefault(x => x.Key is Sphere);
                    if (slot != null) _windowFocus = slot.Key;
                    Speech.Say(TextCleaner.Sentences(new[] { TextCleaner.Join(Describer.VerbName(s), Describer.StateText(s)), slot?.Summary() }));
                }
                return;
            }
            if (s.StateIdentifier != StateEnum.Unstarted) return;
            string key = PredictionKey(s);
            if (_lastPrediction == null)
            {
                _lastPrediction = key;
                return;
            }
            if (key != _lastPrediction)
            {
                _lastPrediction = key;
                string label = Describer.RecipeLabel(s);
                if (label == Describer.VerbName(s)) label = TextCleaner.Join(label, Describer.StateText(s));
                else if (Describer.CanStart(s)) label += ", " + Strings.StateReady;
                Speech.Say(label);
                string text = Describer.RecipeText(s);
                if (!string.IsNullOrEmpty(text)) BufferManager.AddStory(Describer.VerbName(s) + ": " + label + ". " + text);
            }
        }

        private void HandleWindowInput()
        {
            var s = _windowSituation;
            if (s == null) return;
            var list = BuildWindow(s);
            if (KeyInput.Repeat(Key.DownArrow)) _windowFocus = MoveIn(list, _windowFocus, 1);
            else if (KeyInput.Repeat(Key.UpArrow)) _windowFocus = MoveIn(list, _windowFocus, -1);
            else if (KeyInput.Pressed(Key.Home)) _windowFocus = MoveIn(list, _windowFocus, -1, toEnd: true);
            else if (KeyInput.Pressed(Key.End)) _windowFocus = MoveIn(list, _windowFocus, 1, toEnd: true);
            else if (KeyInput.Pressed(Key.PageUp)) TurnPage(s, -1);
            else if (KeyInput.Pressed(Key.PageDown)) TurnPage(s, 1);
            else if (KeyInput.Repeat(Key.LeftArrow) || KeyInput.Repeat(Key.RightArrow))
            {
                int dir = KeyInput.Held(Key.LeftArrow) ? -1 : 1;
                var e = FindFocused(list, _windowFocus);
                if (e?.Adjust != null) e.Adjust(dir);
                else Speech.SayFocus(e != null ? e.Summary() : Strings.NothingFocused);
            }
            else if (KeyInput.Pressed(Key.Enter) || KeyInput.Pressed(Key.NumpadEnter))
            {
                var e = FindFocused(list, _windowFocus);
                if (e != null) _windowFocus = e.Key;
                e?.Activate?.Invoke();
            }
            else if (KeyInput.Hotkey(ModConfig.KeyEmptySlot.Value) || KeyInput.Pressed(Key.Backspace))
            {
                var e = FindFocused(list, _windowFocus);
                if (e?.Delete != null) e.Delete();
                else Speech.Say(Strings.NothingFocused);
            }
            else if (KeyInput.Hotkey(ModConfig.KeyInspect.Value))
            {
                Inspect(FindFocused(list, _windowFocus));
            }
            else if (KeyInput.Hotkey(ModConfig.KeyRepeat.Value))
            {
                var e = FindFocused(list, _windowFocus);
                Speech.Say(e != null ? e.Summary() : Strings.NothingFocused);
            }
            else if (KeyInput.Hotkey(ModConfig.KeyReadScreen.Value))
            {
                Speech.Say(TextCleaner.Sentences(Describer.VerbDetails(s)));
            }
        }

        // ------------------------------------------------------------------ Mansus

        private Otherworld CurrentOtherworld()
        {
            try
            {
                var numa = GameAccess.Numa;
                if (numa == null || CurrentOtherworldRef == null) return null;
                return CurrentOtherworldRef(numa);
            }
            catch
            {
                return null;
            }
        }

        private Sphere ActiveEgress(Otherworld ow)
        {
            if (ow == null) return null;
            foreach (var d in ow.Dominions.OfType<OtherworldDominion>())
            {
                var egress = d.EgressSphere;
                if (egress == null) continue;
                try
                {
                    if (!egress.CurrentlyBlockedFor(BlockDirection.Inward)) return egress;
                }
                catch { }
            }
            return null;
        }

        private List<NavEntry> BuildMansus()
        {
            var list = new List<NavEntry>();
            var ow = CurrentOtherworld();
            if (ow == null) return list;
            var egress = ActiveEgress(ow);
            foreach (var d in ow.Dominions.OfType<OtherworldDominion>())
            {
                int place = 0;
                foreach (var sphere in d.Spheres)
                {
                    if (sphere == null || sphere.Defunct || sphere == d.EgressSphere) continue;
                    if (sphere.SphereCategory != SphereCategory.Output) continue;
                    place++;
                    foreach (var t in sphere.GetElementTokens())
                    {
                        var card = t;
                        string where = MansusLocation(sphere, place);
                        list.Add(new NavEntry
                        {
                            Key = card,
                            Summary = () => where + ": " + (card.Payload.IsShrouded ? Strings.FaceDown : Describer.CardSummary(card)),
                            Details = () => Describer.CardDetails(card),
                            Activate = () => ChooseInMansus(card, egress)
                        });
                    }
                }
            }
            return list;
        }

        private static string MansusLocation(Sphere sphere, int n)
        {
            string label = TextCleaner.Clean(sphere.GoverningSphereSpec?.Label);
            if (!string.IsNullOrEmpty(label)) return label;
            return "Choice " + n;
        }

        private void ChooseInMansus(Token card, Sphere egress)
        {
            if (card == null || card.Defunct) return;
            if (card.Payload.IsShrouded)
            {
                GameActions.Unshroud(card);
                _mansusFocus = card;
                Speech.Say(Strings.MansusRevealed(Describer.CardSummary(card)));
                return;
            }
            if (egress == null)
            {
                Speech.Say(Strings.CannotPlace(Strings.SlotBlocked));
                return;
            }
            GameActions.PlaceInSlot(card, egress);
        }

        private void AnnounceMansus()
        {
            string where = "";
            try
            {
                var ow = CurrentOtherworld();
                var ingress = ow != null ? Traverse.Create(ow).Field("_activeIngress").GetValue() as SecretHistories.Tokens.Payloads.Ingress : null;
                if (ingress != null) where = TextCleaner.Clean(ingress.Label);
            }
            catch { }
            Speech.SayEvent(Strings.MansusEntered(where));
            if (ModConfig.SpeakHints.Value) Speech.Say(Strings.MansusChooseHint);
        }

        private void HandleMansusInput()
        {
            var list = BuildMansus();
            if (list.Count == 0)
            {
                if (KeyInput.Repeat(Key.DownArrow) || KeyInput.Repeat(Key.UpArrow) || KeyInput.Pressed(Key.Enter))
                    Speech.SayFocus(Strings.MansusNoCards);
                return;
            }
            if (KeyInput.Repeat(Key.DownArrow) || KeyInput.Repeat(Key.RightArrow)) _mansusFocus = MoveIn(list, _mansusFocus, 1);
            else if (KeyInput.Repeat(Key.UpArrow) || KeyInput.Repeat(Key.LeftArrow)) _mansusFocus = MoveIn(list, _mansusFocus, -1);
            else if (KeyInput.Pressed(Key.Home)) _mansusFocus = MoveIn(list, _mansusFocus, -1, toEnd: true);
            else if (KeyInput.Pressed(Key.End)) _mansusFocus = MoveIn(list, _mansusFocus, 1, toEnd: true);
            else if (KeyInput.Pressed(Key.Enter) || KeyInput.Pressed(Key.NumpadEnter))
            {
                var e = FindFocused(list, _mansusFocus);
                e?.Activate?.Invoke();
            }
            else if (KeyInput.Hotkey(ModConfig.KeyInspect.Value)) Inspect(FindFocused(list, _mansusFocus));
            else if (KeyInput.Hotkey(ModConfig.KeyRepeat.Value) || KeyInput.Hotkey(ModConfig.KeyReadScreen.Value))
                Speech.Say(TextCleaner.Sentences(list.Select(x => x.Summary())));
        }

        public void DumpToLog()
        {
            try
            {
                if (!GameAccess.TableReady) return;
                for (int g = 0; g < 3; g++)
                {
                    var list = BuildGroup(g);
                    Plugin.LogInfo("---- group " + GroupName(g) + ": " + list.Count);
                    foreach (var e in list) Plugin.LogInfo("  " + e.Summary());
                }
                if (_windowSituation != null)
                {
                    Plugin.LogInfo("---- window " + Describer.VerbName(_windowSituation));
                    foreach (var e in BuildWindow(_windowSituation)) Plugin.LogInfo("  " + e.Summary());
                }
                if (_mode == Mode.Mansus)
                {
                    Plugin.LogInfo("---- mansus");
                    foreach (var e in BuildMansus()) Plugin.LogInfo("  " + e.Summary());
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarning("Tabletop dump failed: " + ex);
            }
        }

        // ------------------------------------------------------------------ help support

        public string ModeName
        {
            get
            {
                if (_picker != null && _picker.IsOpen) return "picker";
                return _mode.ToString();
            }
        }
    }
}
