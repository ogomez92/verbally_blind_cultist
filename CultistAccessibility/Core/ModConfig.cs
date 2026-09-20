using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace CultistAccessibility.Core
{
    internal enum Verbosity
    {
        Terse = 0,
        Normal = 1,
        Verbose = 2
    }

    /// <summary>
    /// All player-facing settings. Written to BepInEx\config\accessibility.cultistsimulator.screenreader.cfg,
    /// which blind players edit in a text editor, so every entry carries a plain description.
    /// </summary>
    internal static class ModConfig
    {
        // General
        public static ConfigEntry<Verbosity> VerbosityLevel;
        public static ConfigEntry<bool> Braille;
        public static ConfigEntry<string> SpeechBackend;
        public static ConfigEntry<bool> InterruptOnNavigation;
        public static ConfigEntry<bool> SpeakHints;
        public static ConfigEntry<bool> MoveCameraToFocus;
        public static ConfigEntry<bool> DebugLogging;
        public static ConfigEntry<bool> DeveloperTestKeys;

        // Announcements
        public static ConfigEntry<bool> AnnounceSituationStarted;
        public static ConfigEntry<bool> AnnounceSituationCompleted;
        public static ConfigEntry<bool> ReadCompletionText;
        public static ConfigEntry<bool> AnnounceOngoingSlots;
        public static ConfigEntry<bool> AnnounceNewVerbs;
        public static ConfigEntry<bool> AnnounceCardChanges;
        public static ConfigEntry<bool> AnnounceCardsArriving;
        public static ConfigEntry<bool> AnnounceGreedyGrabs;
        public static ConfigEntry<bool> AnnounceNotifications;
        public static ConfigEntry<bool> AnnounceSpeedChanges;
        public static ConfigEntry<bool> AnnounceTimerWarnings;
        public static ConfigEntry<int> TimerWarningSeconds;

        // Sounds
        public static ConfigEntry<bool> ActionableSound;
        public static ConfigEntry<int> SoundVolume;

        // Keys
        public static ConfigEntry<Key> KeyHelp;
        public static ConfigEntry<Key> KeyCycleVerbosity;
        public static ConfigEntry<Key> KeyRepeat;
        public static ConfigEntry<Key> KeyInspect;
        public static ConfigEntry<Key> KeyStatus;
        public static ConfigEntry<Key> KeyTimers;
        public static ConfigEntry<Key> KeyReadScreen;
        public static ConfigEntry<Key> KeyNextCompleted;
        public static ConfigEntry<Key> KeyEmptySlot;

        public static void Init(ConfigFile cfg)
        {
            VerbosityLevel = cfg.Bind("General", "Verbosity", Verbosity.Normal,
                "How much is spoken when focus moves. Terse = names only. Normal = names plus the most useful state. Verbose = adds hints and extra detail.");
            SpeechBackend = cfg.Bind("General", "SpeechBackend", "Auto",
                "Which speech output to use. Auto picks your running screen reader, or Windows speech when none is running. Other values: NVDA, JAWS, SAPI, OneCore, ZDSR, SystemAccess, ZoomText.");
            Braille = cfg.Bind("General", "Braille", true,
                "Also send text to a braille display when the screen reader supports it.");
            InterruptOnNavigation = cfg.Bind("General", "InterruptOnNavigation", false,
                "When true, moving focus with the arrow keys cuts off the previous speech. Game events are never interrupted.");
            SpeakHints = cfg.Bind("General", "SpeakHints", true,
                "Speak a short keyboard hint when a screen opens.");
            MoveCameraToFocus = cfg.Bind("General", "MoveCameraToFocus", true,
                "Point the game camera at the focused card or verb, so a sighted helper can follow along.");
            DebugLogging = cfg.Bind("General", "DebugLogging", false,
                "Write every spoken line and extra diagnostics to BepInEx\\LogOutput.log. Control+Shift+D then writes the navigable items to the log.");
            DeveloperTestKeys = cfg.Bind("General", "DeveloperTestKeys", false,
                "For mod developers only: Control+Shift+F9 opens a Mansus portal, Control+Shift+F10 adds basic cards. These change your game; leave false.");

            AnnounceSituationStarted = cfg.Bind("Announcements", "SituationStarted", true,
                "Announce when a verb starts working on something.");
            AnnounceSituationCompleted = cfg.Bind("Announcements", "SituationCompleted", true,
                "Announce when a verb finishes and has results waiting.");
            ReadCompletionText = cfg.Bind("Announcements", "ReadCompletionText", false,
                "Also read the full story text when a verb finishes. When false, the text goes to the Story review buffer only.");
            AnnounceOngoingSlots = cfg.Bind("Announcements", "OngoingSlots", true,
                "Announce when a running verb opens a slot that wants a card (the small slot shown on the verb's token).");
            AnnounceNewVerbs = cfg.Bind("Announcements", "NewVerbs", true,
                "Announce verbs that appear or vanish on the table.");
            AnnounceCardChanges = cfg.Bind("Announcements", "CardChanges", true,
                "Announce cards on the table that decay, transform or vanish.");
            AnnounceCardsArriving = cfg.Bind("Announcements", "CardsArriving", true,
                "Announce cards that arrive on the table without you moving them.");
            AnnounceGreedyGrabs = cfg.Bind("Announcements", "GreedyGrabs", true,
                "Announce when a verb's greedy slot pulls a card from the table.");
            AnnounceNotifications = cfg.Bind("Announcements", "Notifications", true,
                "Read the game's pop-up notifications.");
            AnnounceSpeedChanges = cfg.Bind("Announcements", "SpeedChanges", true,
                "Announce pause, normal speed and fast forward.");
            AnnounceTimerWarnings = cfg.Bind("Announcements", "TimerWarnings", true,
                "Warn when a dangerous verb is about to finish.");
            TimerWarningSeconds = cfg.Bind("Announcements", "TimerWarningSeconds", 10,
                "How many seconds before a dangerous verb finishes the warning is given.");

            ActionableSound = cfg.Bind("Sounds", "ActionableSound", true,
                "Play a short beep when focus lands on something Enter can act on, in lists where only some items can: a card that a verb would take, a slot with a card that fits, a Start button that will work.");
            SoundVolume = cfg.Bind("Sounds", "SoundVolume", 40,
                new ConfigDescription("Volume of the mod's beeps, from 0 to 100. Independent of the game's own volume settings.",
                    new AcceptableValueRange<int>(0, 100)));

            KeyHelp = cfg.Bind("Keys", "Help", Key.F1, "Open context help for the current screen.");
            KeyCycleVerbosity = cfg.Bind("Keys", "CycleVerbosity", Key.F2, "Cycle the verbosity level.");
            KeyRepeat = cfg.Bind("Keys", "Repeat", Key.R, "Read the focused item again in full.");
            KeyInspect = cfg.Bind("Keys", "Inspect", Key.I, "Read every detail of the focused item (description, aspects, slots).");
            KeyStatus = cfg.Bind("Keys", "Status", Key.H, "Read your character and the status bar counts (health, passion, reason, funds).");
            KeyTimers = cfg.Bind("Keys", "Timers", Key.T, "Read game speed and every busy verb with its time remaining.");
            KeyReadScreen = cfg.Bind("Keys", "ReadScreen", Key.A, "Read everything on the current screen.");
            KeyNextCompleted = cfg.Bind("Keys", "NextCompleted", Key.G, "On the table: move focus to the next verb with results waiting.");
            KeyEmptySlot = cfg.Bind("Keys", "EmptySlot", Key.Delete, "In a verb window: take the card out of the focused slot.");
        }

        public static Verbosity Level => VerbosityLevel?.Value ?? Verbosity.Normal;

        public static void CycleVerbosity()
        {
            int next = ((int)Level + 1) % 3;
            VerbosityLevel.Value = (Verbosity)next;
        }
    }
}
