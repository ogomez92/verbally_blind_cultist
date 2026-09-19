using System;
using System.Collections.Generic;
using System.Linq;
using CultistAccessibility.Core;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Spheres;
using SecretHistories.UI;

namespace CultistAccessibility.Tabletop
{
    /// <summary>
    /// Every state-changing action the keyboard can take on the table. Each one reuses the code path a mouse
    /// action reaches in the game (drop into a slot, click a verb, the Start and Collect buttons), asks the
    /// game's own validation first, and speaks the refusal instead of silently failing.
    /// </summary>
    internal static class GameActions
    {
        public static void OpenSituation(Situation s)
        {
            if (s == null || s.Defunct) return;
            try
            {
                if (!s.IsOpen) s.OpenAt(s.Token.Location);
            }
            catch (Exception ex)
            {
                Plugin.LogError("OpenSituation failed: " + ex);
            }
        }

        public static void CloseSituation(Situation s)
        {
            if (s == null || s.Defunct) return;
            try { if (s.IsOpen) s.Close(); }
            catch (Exception ex) { Plugin.LogError("CloseSituation failed: " + ex); }
        }

        /// <summary>The same checks ThresholdSphere.TryMoveAsideAndAcceptToken performs, as speakable text.</summary>
        public static string WhyCannotPlace(Token card, Sphere slot)
        {
            if (card == null || card.Defunct) return Strings.CannotMove;
            if (!card.CanBeDragged() || card.CurrentState.InSystemDrivenMotion()) return Strings.CannotMove;
            if (slot == null || slot.Defunct) return Strings.SlotBlocked;
            if (slot.CurrentlyBlockedFor(BlockDirection.Inward)) return Strings.SlotBlocked;
            var match = slot.GetMatchForTokenPayload(card.Payload);
            if (match.MatchType != SlotMatchForAspectsType.Okay)
            {
                try { return TextCleaner.Clean(match.GetProblemDescription(GameAccess.Compendium)); }
                catch { return Strings.SlotBlocked; }
            }
            if (!slot.IsValidDestinationForToken(card)) return Strings.SlotBlocked;
            return null;
        }

        public static bool CanPlace(Token card, Sphere slot) => WhyCannotPlace(card, slot) == null;

        /// <summary>
        /// Drops one card of the stack into the slot, like a mouse drop: the rest of the stack stays on the table,
        /// the card remembers its table position (so it returns there), any occupant goes back to the table.
        /// </summary>
        public static bool PlaceInSlot(Token card, Sphere slot)
        {
            string why = WhyCannotPlace(card, slot);
            if (why != null)
            {
                Speech.Say(Strings.CannotPlace(why));
                return false;
            }
            try
            {
                if (card.Quantity > 1) card.CalveToken(card.Quantity - 1);
                card.RequestHomeLocationFromCurrentSphere();
                bool ok = slot.TryAcceptToken(card, new Context(Context.ActionSource.PlayerDrag));
                if (!ok)
                {
                    Speech.Say(Strings.CannotPlace(Strings.SlotBlocked));
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogError("PlaceInSlot failed: " + ex);
                return false;
            }
        }

        /// <summary>Sends the card's occupant of a slot back to the table (its home position if it has one).</summary>
        public static Token EmptySlot(Sphere slot)
        {
            var card = slot?.GetElementTokens().FirstOrDefault();
            if (card == null) return null;
            if (!card.CanBeDragged())
            {
                Speech.Say(Strings.CannotMove);
                return null;
            }
            try
            {
                card.GoAway(new Context(Context.ActionSource.PlayerDrag));
                return card;
            }
            catch (Exception ex)
            {
                Plugin.LogError("EmptySlot failed: " + ex);
                return null;
            }
        }

        /// <summary>Takes one result card out of a finished verb onto the table.</summary>
        public static bool TakeToTable(Token card)
        {
            if (card == null || card.Defunct) return false;
            if (!card.CanBeDragged())
            {
                Speech.Say(Strings.CannotMove);
                return false;
            }
            try
            {
                card.GoAway(new Context(Context.ActionSource.PlayerDrag));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogError("TakeToTable failed: " + ex);
                return false;
            }
        }

        /// <summary>Verbs with a slot that can take this card now (the game's double-click destinations).</summary>
        public static List<KeyValuePair<Situation, Sphere>> VerbsAccepting(Token card)
        {
            var result = new List<KeyValuePair<Situation, Sphere>>();
            if (card == null || card.Defunct) return result;
            foreach (var s in GameAccess.TableSituations())
            {
                foreach (var slot in GameAccess.ActiveThresholds(s))
                {
                    if (slot.GetElementTokens().Any()) continue;
                    if (!slot.CanAcceptToken(card)) continue;
                    if (slot.CurrentlyBlockedFor(BlockDirection.Inward)) continue;
                    result.Add(new KeyValuePair<Situation, Sphere>(s, slot));
                    break;
                }
            }
            return result;
        }

        public static bool TryStart(Situation s)
        {
            if (s == null) return false;
            if (!Describer.CanStart(s))
            {
                Speech.Say(Strings.CannotStart);
                return false;
            }
            try
            {
                s.TryStart();
                return s.StateIdentifier != StateEnum.Unstarted;
            }
            catch (Exception ex)
            {
                Plugin.LogError("TryStart failed: " + ex);
                return false;
            }
        }

        public static void Collect(Situation s)
        {
            if (s == null) return;
            try { s.Conclude(); }
            catch (Exception ex) { Plugin.LogError("Collect failed: " + ex); }
        }

        /// <summary>Turns a face-down card over, as a click on it does.</summary>
        public static void Unshroud(Token card)
        {
            try { card.Payload.Unshroud(false); }
            catch (Exception ex) { Plugin.LogError("Unshroud failed: " + ex); }
        }
    }
}
