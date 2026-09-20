using System.Runtime.CompilerServices;
using UnityEngine.InputSystem;

namespace CultistAccessibility.Core
{
    /// <summary>
    /// Every string the mod itself speaks (game text comes from the game's own localisation).
    /// The texts live in Lang/&lt;culture&gt;.txt under the member's name and follow the game's language (see Loc).
    /// Nothing here may be cached by callers across frames: the player can change language at any time.
    /// </summary>
    internal static class Strings
    {
        private static string T([CallerMemberName] string key = null) => Loc.Get(key);
        private static string F(string key, params object[] args) => Loc.Format(key, args);

        public static string ModLoaded => T();

        // Lists and navigation
        public static string StartOfList => T();
        public static string EndOfList => T();
        public static string NothingToNavigate => T();
        public static string NothingFocused => T();
        public static string Unavailable => T();
        public static string Selected => T();
        public static string NotSelected => T();
        public static string Checked => T();
        public static string Unchecked => T();
        public static string Maximum => T();
        public static string Minimum => T();
        public static string PercentValue(int percent) => F(nameof(PercentValue), percent);
        public static string EditingField => T();
        public static string SliderHint => T();
        public static string PressNewKey => T();
        public static string NoKeyBound => T();
        public static string KeyBoundTo(string key) => F(nameof(KeyBoundTo), key);
        public static string PositionOf(int i, int n) => F(nameof(PositionOf), i, n);

        // Roles
        public static string RoleSlider => T();
        public static string RoleKeyBinding => T();
        public static string RoleTab => T();
        public static string RoleCheckbox => T();
        public static string RoleRadio => T();
        public static string RoleCombo => T();
        public static string RoleEdit => T();
        public static string RoleLink => T();
        public static string OpeningLink => T();
        public static string RoleLegacyChoice => T();
        public static string CloseButton => T();
        public static string StackCardsButton => T();
        public static string CharacterName => T();

        // Menus
        public static string LegacyInstalled => T();
        public static string LegacyNotInstalled => T();
        public static string ModMoveUp => T();
        public static string ModMoveDown => T();
        public static string ModEnabled => T();
        public static string ModDisabled => T();

        // Buffers
        public static string BufferEvents => T();
        public static string BufferDetails => T();
        public static string BufferStory => T();
        public static string BufferVerbs => T();
        public static string BufferTable => T();
        public static string BufferStatus => T();
        public static string AllBuffersEmpty => T();
        public static string BufferEmpty(string name) => F(nameof(BufferEmpty), name);
        public static string BufferSummary(string name, int count) => Loc.Plural(nameof(BufferSummary), count, name);

        // Help
        public static string HelpTitle(string context) => F(nameof(HelpTitle), context);
        public static string HelpNavigationHint => T();
        public static string HelpClosed => T();

        // Verbosity
        public static string VerbosityIs(Verbosity v) => F(nameof(VerbosityIs), Loc.Get("Verbosity." + v));

        /// <summary>Spoken name of a key: letters, digits and function keys as they are, named keys translated.</summary>
        public static string KeyName(Key k)
        {
            string key = "Key." + k;
            return Loc.Has(key) ? Loc.Get(key) : k.ToString();
        }

        // Screens
        public static string ScreenName(GameScreen s)
        {
            string key = "Screen." + s;
            return Loc.Has(key) ? Loc.Get(key) : "";
        }
        public static string PressAnyKey => T();
        public static string DebugDumped => T();

        // Tabletop groups
        public static string GroupVerbs => T();
        public static string GroupCards => T();
        public static string GroupControls => T();
        public static string GroupAnnounce(string name, int count) => F(nameof(GroupAnnounce), name, count);
        public static string NoVerbs => T();
        public static string NoCards => T();
        public static string VerbCount(int n) => Loc.Plural(nameof(VerbCount), n);
        public static string CardCount(int n) => Loc.Plural(nameof(CardCount), n);
        public static string BusyCount(int n) => Loc.Plural(nameof(BusyCount), n);
        public static string DoneCount(int n) => Loc.Plural(nameof(DoneCount), n);
        public static string TableHint => T();

        // Situations
        public static string StateIdle => T();
        public static string StateReady => T();
        public static string StateRunning => T();
        public static string StateStarting => T();
        public static string StateComplete => T();
        public static string StateHalting => T();
        public static string StateBusy => T();
        public static string TimeLeft(string time) => F(nameof(TimeLeft), time);
        public static string CardsWaiting(int n) => Loc.Plural(nameof(CardsWaiting), n);
        public static string WindowOpen => T();
        public static string SlotEmpty => T();
        public static string SlotWord => T();
        public static string SlotNamed(string label) => F(nameof(SlotNamed), label);
        public static string SlotCount(int n) => Loc.Plural(nameof(SlotCount), n);
        public static string SlotGreedy => T();
        public static string SlotConsumes => T();
        public static string SlotBlocked => T();
        public static string SlotRequires => T();
        public static string SlotForbids => T();
        public static string SlotEssential => T();
        public static string BringsSlot(string slot) => F(nameof(BringsSlot), slot);
        public static string StartButton => T();
        public static string StartUnavailable => T();
        public static string CollectAll => T();
        public static string OutputHeading => T();
        public static string StoredHeading => T();
        public static string PageOf(int i, int n) => F(nameof(PageOf), i, n);
        public static string NoMorePages => T();
        public static string VerbStarted(string verb, string recipe, string time) => WithTime(F(nameof(VerbStarted), verb, recipe), time);
        public static string VerbCompleted(string verb, string recipe) =>
            string.IsNullOrEmpty(recipe) ? F("VerbCompleted.plain", verb) : F("VerbCompleted.recipe", verb, recipe);
        public static string VerbAppeared(string verb) => F(nameof(VerbAppeared), verb);
        public static string VerbVanished(string verb) => F(nameof(VerbVanished), verb);
        public static string VerbDanger(string verb, string time) => F(nameof(VerbDanger), verb, time);
        public static string VerbContinues(string verb, string recipe, string time) => WithTime(F(nameof(VerbContinues), verb, recipe), time);
        public static string VerbWillBecome(string verb, string recipe) => F(nameof(VerbWillBecome), verb, recipe);
        public static string VerbSlotOpened(string verb, string slot) => F(nameof(VerbSlotOpened), verb, slot);
        public static string OngoingSlotOpen(string slot) => F(nameof(OngoingSlotOpen), slot);
        public static string OngoingSlotHolds(string slot, string card) => F(nameof(OngoingSlotHolds), slot, card);
        public static string WindowClosed => T();
        public static string Recipe => T();
        public static string DeckDraws => T();

        private static string WithTime(string text, string time) => string.IsNullOrEmpty(time) ? text : text + ", " + time;

        // Cards
        public static string FaceDown => T();
        public static string Unique => T();
        public static string DecaysIn(string time) => F(nameof(DecaysIn), time);
        public static string Quantity(int n) => F(nameof(Quantity), n);
        public static string Aspects => T();
        public static string NoAspects => T();
        public static string CardsArrived(string list) => F(nameof(CardsArrived), list);
        public static string CardGone(string card) => F(nameof(CardGone), card);
        public static string MergedInto(string card, int total) => F(nameof(MergedInto), card, total);
        public static string CardBecame(string from, string to) => F(nameof(CardBecame), from, to);
        public static string GreedyGrab(string verb, string card) => F(nameof(GreedyGrab), verb, card);

        // Picking and placing
        public static string PickCardFor(string slot, int n) => Loc.Plural(nameof(PickCardFor), n, slot);
        public static string PickVerbFor(string card, int n) => Loc.Plural(nameof(PickVerbFor), n, card);
        public static string NoCardFits => T();
        public static string NoVerbAccepts => T();
        public static string EmptyTheSlot => T();
        public static string PickerCancelled => T();
        public static string Placed(string card, string slot) => F(nameof(Placed), card, slot);
        public static string Removed(string card) => F(nameof(Removed), card);
        public static string CannotPlace(string reason) => F(nameof(CannotPlace), reason);
        public static string CannotMove => T();
        public static string SlotNotEmpty => T();
        public static string CollectedToTable => T();
        public static string TakenToTable(string card) => F(nameof(TakenToTable), card);
        public static string CannotStart => T();
        public static string NoWindowOpen => T();
        public static string NothingToCollect => T();

        // Status and time
        public static string Paused => T();
        public static string NormalSpeed => T();
        public static string FastSpeed => T();
        public static string VeryFastSpeed => T();
        public static string NoBusyVerbs => T();
        public static string NothingCompleted => T();
        public static string Busy(string verb, string time) => F(nameof(Busy), verb, time);
        public static string Seconds(int n) => Loc.Plural(nameof(Seconds), n);
        /// <summary>Tenths of a second below ten seconds: "4.5 seconds", with the language's decimal separator.</summary>
        public static string SecondsFraction(float seconds)
        {
            string number = seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace(".", Loc.Get("DecimalSeparator"));
            return F(nameof(SecondsFraction), number);
        }
        public static string Minutes(int n) => Loc.Plural(nameof(Minutes), n);
        public static string MinutesAndSeconds(string minutes, string seconds) => F(nameof(MinutesAndSeconds), minutes, seconds);

        // Mansus
        public static string MansusEntered(string where) => F(nameof(MansusEntered), where);
        public static string MansusChooseHint => T();
        public static string MansusRevealed(string card) => F(nameof(MansusRevealed), card);
        public static string MansusLeft => T();
        public static string MansusNoCards => T();
        public static string MansusChoice(int n) => F(nameof(MansusChoice), n);

        public static string PortalSummary(string label, int cards) =>
            F(nameof(PortalSummary), label) + (cards > 0 ? ", " + CardsWaiting(cards) : "");
        public static string PortalReadText => T();
        public static string PortalTake(string card) => F(nameof(PortalTake), card);
        public static string PortalCollectAll => T();
        public static string PortalWaiting(string card) => F(nameof(PortalWaiting), card);

        public static string AchievementUnlocked(string when) =>
            string.IsNullOrEmpty(when) ? Loc.Get("AchievementUnlocked.plain") : F("AchievementUnlocked.when", when);
        public static string AchievementLocked => T();

        // Game over
        public static string LifeEnding => T();
    }
}
