using System;
using System.Collections.Generic;
using SecretHistories.UI;

namespace CultistAccessibility.Tabletop
{
    /// <summary>One focusable thing on the table: a verb, a card, a slot, a button, a line of text.</summary>
    internal sealed class NavEntry
    {
        /// <summary>Identity used to keep focus when lists are rebuilt (Situation, Token, Sphere or a string).</summary>
        public object Key;
        public Func<string> Summary;
        public Func<List<string>> Details;
        public Action Activate;
        /// <summary>Left/Right on this entry (for example turning story pages). Null: arrows do nothing here.</summary>
        public Action<int> Adjust;
        /// <summary>Delete key on this entry (empty a slot).</summary>
        public Action Delete;
        /// <summary>Token to point the camera at.</summary>
        public Token Token;
    }
}
