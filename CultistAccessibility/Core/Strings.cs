namespace CultistAccessibility.Core
{
    /// <summary>
    /// Every string the mod itself speaks (game text comes from the game's own localisation).
    /// Kept in one place so the mod can be translated later.
    /// </summary>
    internal static class Strings
    {
        public const string ModLoaded = "Cultist Simulator accessibility loaded. Press F1 for help at any time.";

        // Lists and navigation
        public const string StartOfList = "Top.";
        public const string EndOfList = "Bottom.";
        public const string NothingToNavigate = "Nothing to navigate here.";
        public const string NothingFocused = "Nothing focused.";
        public const string Unavailable = "unavailable";
        public const string Selected = "selected";
        public const string NotSelected = "not selected";
        public const string Checked = "checked";
        public const string Unchecked = "not checked";
        public const string Maximum = "Maximum.";
        public const string Minimum = "Minimum.";
        public const string Percent = "percent";
        public const string EditingField = "Editing. Type, then press Enter.";
        public const string SliderHint = "Use left and right arrows to change the value.";
        public const string PressNewKey = "Press the new key for this action.";
        public const string NoKeyBound = "no key";
        public static string KeyBoundTo(string key) => "Bound to " + key + ".";
        public static string PositionOf(int i, int n) => i + " of " + n;

        // Roles
        public const string RoleSlider = "slider";
        public const string RoleKeyBinding = "key binding";
        public const string RoleTab = "tab";
        public const string RoleCheckbox = "checkbox";
        public const string RoleRadio = "option";
        public const string RoleCombo = "combo box";
        public const string RoleEdit = "edit field";
        public const string RoleLink = "link";
        public const string RoleLegacyChoice = "legacy";
        public const string CloseButton = "Close";
        public const string StackCardsButton = "Stack cards";
        public const string CharacterName = "Character name";

        // Menus
        public const string LegacyInstalled = "installed, press Enter to begin";
        public const string LegacyNotInstalled = "not installed";
        public static string LegacyNumber(int n) => "Legacy " + n;
        public const string ModMoveUp = "Move up in load order";
        public const string ModMoveDown = "Move down in load order";
        public const string ModEnabled = "enabled";
        public const string ModDisabled = "disabled";

        // Buffers
        public const string BufferEvents = "Events";
        public const string BufferDetails = "Details";
        public const string BufferStory = "Story";
        public const string BufferVerbs = "Verbs";
        public const string BufferTable = "Table";
        public const string BufferStatus = "Status";
        public const string BufferTop = "Newest.";
        public const string BufferEnd = "Oldest.";
        public const string AllBuffersEmpty = "All review buffers are empty.";
        public static string BufferEmpty(string name) => name + " is empty.";
        public static string BufferSummary(string name, int count) => name + ", " + count + (count == 1 ? " item" : " items");

        // Help
        public static string HelpTitle(string context) => "Help: " + context;
        public const string HelpNavigationHint = "Up and down arrows read the help, Escape closes it.";
        public const string HelpClosed = "Help closed.";

        // Verbosity
        public static string VerbosityIs(Verbosity v) => "Verbosity " + v.ToString().ToLowerInvariant() + ".";

        // Screens
        public static string ScreenName(GameScreen s)
        {
            switch (s)
            {
                case GameScreen.Logo: return "Logo. Press any key to skip.";
                case GameScreen.Quote: return "Title quote.";
                case GameScreen.Menu: return "Main menu.";
                case GameScreen.Tabletop: return "The table.";
                case GameScreen.GameOver: return "The end.";
                case GameScreen.NewGame: return "Choose your legacy.";
                case GameScreen.Crash: return "The game hit an error.";
                default: return "";
            }
        }
        public const string PressAnyKey = "Press any key to continue.";

        // Tabletop groups
        public const string GroupVerbs = "Verbs";
        public const string GroupCards = "Cards";
        public const string GroupControls = "Controls";
        public const string GroupMansus = "The Mansus";
        public static string GroupAnnounce(string name, int count) => name + ", " + count;
        public const string NoVerbs = "No verbs on the table.";
        public const string NoCards = "No cards on the table.";
        public const string TableLoading = "The table is still being laid out.";

        // Situations
        public const string StateIdle = "idle";
        public const string StateReady = "ready to start";
        public const string StateRunning = "running";
        public const string StateStarting = "starting";
        public const string StateComplete = "results waiting";
        public const string StateHalting = "halting";
        public const string StateBusy = "busy";
        public static string TimeLeft(string time) => time + " left";
        public static string CardsWaiting(int n) => n == 1 ? "1 card waiting" : n + " cards waiting";
        public const string WindowOpen = "open";
        public const string SlotEmpty = "empty";
        public const string SlotWord = "slot";
        public const string SlotGreedy = "greedy: grabs a matching card by itself";
        public const string SlotConsumes = "consumes its card";
        public const string SlotBlocked = "blocked";
        public const string SlotRequires = "accepts";
        public const string SlotForbids = "forbids";
        public const string SlotEssential = "must have";
        public const string StartButton = "Start";
        public const string StartUnavailable = "Start, unavailable: these cards do not make a recipe that can begin";
        public const string CollectAll = "Collect all";
        public const string OutputHeading = "Results";
        public const string StoredHeading = "Inside";
        public const string NotesPage = "page";
        public static string PageOf(int i, int n) => "Page " + i + " of " + n;
        public const string NoMorePages = "No other pages.";
        public static string VerbStarted(string verb, string recipe, string time) => verb + " begins: " + recipe + (string.IsNullOrEmpty(time) ? "" : ", " + time);
        public static string VerbCompleted(string verb, string recipe) => verb + " is done" + (string.IsNullOrEmpty(recipe) ? "" : ": " + recipe);
        public static string VerbAppeared(string verb) => "New verb: " + verb;
        public static string VerbVanished(string verb) => verb + " is gone";
        public static string VerbDanger(string verb, string time) => "Warning: " + verb + " finishes in " + time;
        public static string VerbContinues(string verb, string recipe, string time) => verb + " continues: " + recipe + (string.IsNullOrEmpty(time) ? "" : ", " + time);
        public const string WindowClosed = "Window closed.";
        public const string Recipe = "Recipe";
        public const string DeckDraws = "draws from";

        // Cards
        public const string FaceDown = "face-down card";
        public const string Unique = "unique";
        public static string DecaysIn(string time) => "decays in " + time;
        public static string Quantity(int n) => "x " + n;
        public const string Aspects = "Aspects";
        public const string NoAspects = "No aspects.";
        public static string CardArrived(string card) => card + " arrives";
        public static string CardsArrived(string list) => "On the table: " + list;
        public static string CardGone(string card) => card + " is gone";
        public static string MergedInto(string card, int total) => card + ", now " + total;
        public static string CardBecame(string from, string to) => from + " becomes " + to;
        public static string GreedyGrab(string verb, string card) => verb + " takes " + card;
        public const string InSlot = "in";

        // Picking and placing
        public static string PickCardFor(string slot, int n) => "Choose a card for " + slot + ". " + n + (n == 1 ? " card fits." : " cards fit.");
        public static string PickVerbFor(string card, int n) => "Choose a verb for " + card + ". " + n + (n == 1 ? " verb can take it." : " verbs can take it.");
        public const string NoCardFits = "No card on the table fits this slot.";
        public const string NoVerbAccepts = "No verb can take this card right now.";
        public const string EmptyTheSlot = "Take the card out";
        public const string PickerCancelled = "Cancelled.";
        public static string Placed(string card, string slot) => card + " placed in " + slot;
        public static string Removed(string card) => card + " returned to the table";
        public static string CannotPlace(string reason) => "Cannot place. " + reason;
        public const string CannotMove = "That card cannot be moved right now.";
        public const string SlotNotEmpty = "The slot is empty.";
        public static string SentTo(string card, string verb) => card + " sent to " + verb;
        public const string CollectedToTable = "Collecting.";
        public static string TakenToTable(string card) => card + " taken to the table";
        public const string CannotStart = "Cannot start: nothing here makes a recipe that can begin.";
        public const string NoWindowOpen = "No verb window is open.";
        public const string NothingToCollect = "Nothing to collect yet.";
        public const string Started = "Started.";

        // Status and time
        public const string Paused = "Paused";
        public const string NormalSpeed = "Normal speed";
        public const string FastSpeed = "Fast forward";
        public const string VeryFastSpeed = "Very fast";
        public const string NoBusyVerbs = "No verb is busy.";
        public const string NothingCompleted = "No verb has results waiting.";
        public static string Busy(string verb, string time) => verb + ", " + time;

        // Mansus
        public static string MansusEntered(string where) => "You enter the Mansus: " + where + ". Choose one face-down card.";
        public const string MansusChooseHint = "Press Enter on a face-down card to turn it over, then Enter again to take it back with you.";
        public static string MansusRevealed(string card) => "Turned over: " + card + ". Press Enter again to take it back.";
        public const string MansusLeft = "You leave the Mansus.";
        public const string MansusNoCards = "No cards to choose here yet.";
        public static string MansusTake(string card) => "Take " + card + " back to the waking world";

        public static string PortalSummary(string label, int cards) => "Portal: " + label + (cards > 0 ? ", " + CardsWaiting(cards) : "");
        public const string PortalReadText = "Read what I remember";
        public static string PortalTake(string card) => "Take " + card + " to the table";
        public const string PortalCollectAll = "Take everything and close the portal";
        public static string PortalWaiting(string card) => "Waiting in the portal on the table: " + card;

        public static string AchievementUnlocked(string when) => string.IsNullOrEmpty(when) ? "unlocked" : "unlocked " + when;
        public const string AchievementLocked = "locked";

        // Game over
        public const string LifeEnding = "This life is ending.";
        public const string GameOverHint = "Up and down arrows read the ending and the buttons.";
    }
}
