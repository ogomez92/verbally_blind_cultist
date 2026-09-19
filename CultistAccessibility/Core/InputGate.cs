namespace CultistAccessibility.Core
{
    /// <summary>
    /// Decides which of the game's own key handlers may run. The Input System fires the game's action
    /// callbacks before our Update, so these flags describe the mod state from previous frames:
    /// a key press that closes a mod modal is still blocked from reaching the game.
    /// </summary>
    internal static class InputGate
    {
        /// <summary>The F1 help list is open.</summary>
        public static bool HelpOpen;

        /// <summary>A table picker (cards for a slot, verbs for a card, portal) is open.</summary>
        public static bool PickerOpen;

        /// <summary>A mod modal owns the keyboard. Help can open over a picker; closing it leaves the picker's claim.</summary>
        public static bool ModalOpen => HelpOpen || PickerOpen;

        /// <summary>The mod's own navigation uses the arrow keys, so the game's camera pan must not.</summary>
        public static bool ArrowsClaimed;

        /// <summary>The mod is waiting for a key press that belongs to the game (key rebinding).</summary>
        public static bool PassThrough;

        public static bool BlockGameHotkeys => ModalOpen && !PassThrough;

        public static bool BlockCameraKeys => (ArrowsClaimed || ModalOpen) && !PassThrough;
    }
}
