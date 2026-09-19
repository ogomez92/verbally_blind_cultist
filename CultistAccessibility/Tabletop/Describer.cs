using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Application.Spheres;
using CultistAccessibility.Core;
using SecretHistories.Abstract;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Logic;
using SecretHistories.Enums;
using SecretHistories.Spheres;
using SecretHistories.UI;

namespace CultistAccessibility.Tabletop
{
    /// <summary>
    /// Builds what is spoken for cards, verbs and slots. Summaries are short (spoken on focus);
    /// Details are review-buffer lines (description, every aspect with its explanation, slot rules).
    /// All values come from game state (payloads, elements, recipes), never from UI labels.
    /// </summary>
    internal static class Describer
    {
        // ------------------------------------------------------------------ cards

        public static string CardName(Token token)
        {
            if (token == null || token.Defunct) return "";
            var p = token.Payload;
            if (p.IsShrouded) return Strings.FaceDown;
            string label = p.Metafictional ? p.MetafictionalLabel : p.Label;
            return TextCleaner.Clean(label);
        }

        public static string CardSummary(Token token)
        {
            if (token == null || token.Defunct) return "";
            var p = token.Payload;
            string name = CardName(token);
            if (p.IsShrouded) return name;
            var parts = new List<string> { name };
            if (p.Quantity > 1) parts.Add(Strings.Quantity(p.Quantity));
            var shadow = SafeTimeshadow(p);
            if (shadow != null && shadow.Transient) parts.Add(Strings.DecaysIn(GameAccess.FormatTime(shadow.LifetimeRemaining)));
            if (ModConfig.Level == Verbosity.Verbose)
            {
                string aspects = AspectSummary(CardAspects(p));
                if (!string.IsNullOrEmpty(aspects)) parts.Add(aspects);
            }
            return TextCleaner.Join(parts.ToArray());
        }

        public static List<string> CardDetails(Token token)
        {
            var lines = new List<string>();
            if (token == null || token.Defunct) return lines;
            var p = token.Payload;
            if (p.IsShrouded)
            {
                lines.Add(Strings.FaceDown);
                return lines;
            }
            lines.Add(CardSummary(token));
            string desc = p.Metafictional ? p.MetafictionalDescription : p.Description;
            lines.AddRange(TextCleaner.SplitIntoSpeechItems(desc));
            var stack = p as ElementStack;
            if (stack != null && stack.Element != null && stack.Element.Unique) lines.Add(Strings.Unique);
            var aspects = CardAspects(p);
            lines.Add(AspectSummary(aspects, withHeading: true));
            lines.AddRange(AspectExplanations(aspects));
            if (stack != null && stack.Element != null && stack.Element.Slots != null && stack.Element.Slots.Count > 0)
            {
                foreach (var slot in stack.Element.Slots)
                    lines.Add("Brings a slot: " + SlotSpecText(slot));
            }
            return lines;
        }

        /// <summary>Aspects of one card of the stack, like the game's details window (self excluded, divided by quantity).</summary>
        public static AspectsDictionary CardAspects(ITokenPayload p)
        {
            try
            {
                var a = new AspectsDictionary(p.GetAspects(false));
                if (p.Quantity > 1) a.DivideByQuantity(p.Quantity);
                return a;
            }
            catch
            {
                return new AspectsDictionary();
            }
        }

        private static Timeshadow SafeTimeshadow(ITokenPayload p)
        {
            try { return p.GetTimeshadow(); }
            catch { return null; }
        }

        // ------------------------------------------------------------------ aspects

        /// <summary>Visible aspects in the game's display order: "Lantern 2, Knock 1".</summary>
        public static string AspectSummary(AspectsDictionary aspects, bool withHeading = false)
        {
            var list = VisibleAspects(aspects).Select(x => x.Key.Label + " " + x.Value).ToList();
            if (list.Count == 0) return withHeading ? Strings.NoAspects : "";
            string joined = string.Join(", ", list.ToArray());
            return withHeading ? Strings.Aspects + ": " + joined : joined;
        }

        public static List<KeyValuePair<Element, int>> VisibleAspects(AspectsDictionary aspects)
        {
            var result = new List<KeyValuePair<Element, int>>();
            if (aspects == null) return result;
            var comp = GameAccess.Compendium;
            if (comp == null) return result;
            foreach (var kv in aspects)
            {
                if (kv.Value == 0) continue;
                Element e;
                try { e = comp.GetEntityById<Element>(kv.Key); }
                catch { continue; }
                if (e == null || !e.IsValid() || e.IsHidden) continue;
                result.Add(new KeyValuePair<Element, int>(e, kv.Value));
            }
            return result
                .OrderBy(x => x.Key.GetSortGroup())
                .ThenBy(x => x.Key.GetSortOrder())
                .ThenByDescending(x => x.Value)
                .ToList();
        }

        /// <summary>One line per aspect: "Lantern: description". Buffer content only.</summary>
        public static List<string> AspectExplanations(AspectsDictionary aspects)
        {
            var lines = new List<string>();
            foreach (var kv in VisibleAspects(aspects))
            {
                string d = TextCleaner.Clean(kv.Key.Description);
                if (string.IsNullOrEmpty(d)) continue;
                lines.Add(TextCleaner.Clean(kv.Key.Label) + ": " + d);
            }
            return lines;
        }

        private static string AspectList(AspectsDictionary a)
        {
            if (a == null || a.Count == 0) return "";
            var comp = GameAccess.Compendium;
            var parts = new List<string>();
            foreach (var kv in a)
            {
                string label = kv.Key;
                try
                {
                    var e = comp.GetEntityById<Element>(kv.Key);
                    if (e != null && e.IsValid()) label = TextCleaner.Clean(e.Label);
                }
                catch { }
                parts.Add(kv.Value > 1 ? label + " " + kv.Value : label);
            }
            return string.Join(", ", parts.ToArray());
        }

        // ------------------------------------------------------------------ slots

        public static string SlotLabel(Sphere slot)
        {
            var spec = slot?.GoverningSphereSpec;
            if (spec == null) return Strings.SlotWord;
            string label = TextCleaner.Clean(spec.Label);
            if (string.IsNullOrEmpty(label)) label = TextCleaner.Clean(spec.Id);
            return label;
        }

        public static string SlotSummary(Sphere slot)
        {
            if (slot == null) return "";
            var parts = new List<string> { SlotLabel(slot) + " " + Strings.SlotWord };
            var card = slot.GetElementTokens().FirstOrDefault();
            parts.Add(card != null ? CardSummary(card) : Strings.SlotEmpty);
            var spec = slot.GoverningSphereSpec;
            if (spec != null)
            {
                if (spec.Greedy) parts.Add(Strings.SlotGreedy);
                if (spec.Consumes) parts.Add(Strings.SlotConsumes);
            }
            try
            {
                if (slot.CurrentlyBlockedFor(BlockDirection.Inward) && card == null) parts.Add(Strings.SlotBlocked);
            }
            catch { }
            if (ModConfig.Level != Verbosity.Terse && card == null && spec != null)
            {
                string req = AspectList(spec.Required);
                if (!string.IsNullOrEmpty(req)) parts.Add(Strings.SlotRequires + " " + req);
                string ess = AspectList(spec.Essential);
                if (!string.IsNullOrEmpty(ess)) parts.Add(Strings.SlotEssential + " " + ess);
            }
            return TextCleaner.Join(parts.ToArray());
        }

        public static string SlotSpecText(SphereSpec spec)
        {
            if (spec == null) return "";
            var parts = new List<string> { TextCleaner.Clean(spec.Label) };
            string req = AspectList(spec.Required);
            if (!string.IsNullOrEmpty(req)) parts.Add(Strings.SlotRequires + " " + req);
            string ess = AspectList(spec.Essential);
            if (!string.IsNullOrEmpty(ess)) parts.Add(Strings.SlotEssential + " " + ess);
            string forb = AspectList(spec.Forbidden);
            if (!string.IsNullOrEmpty(forb)) parts.Add(Strings.SlotForbids + " " + forb);
            return TextCleaner.Join(parts.ToArray());
        }

        public static List<string> SlotDetails(Sphere slot)
        {
            var lines = new List<string>();
            if (slot == null) return lines;
            lines.Add(SlotSummary(slot));
            var spec = slot.GoverningSphereSpec;
            if (spec != null)
            {
                lines.AddRange(TextCleaner.SplitIntoSpeechItems(spec.Description));
                string req = AspectList(spec.Required);
                if (!string.IsNullOrEmpty(req)) lines.Add(Strings.SlotRequires + ": " + req);
                string ess = AspectList(spec.Essential);
                if (!string.IsNullOrEmpty(ess)) lines.Add(Strings.SlotEssential + ": " + ess);
                string forb = AspectList(spec.Forbidden);
                if (!string.IsNullOrEmpty(forb)) lines.Add(Strings.SlotForbids + ": " + forb);
            }
            var card = slot.GetElementTokens().FirstOrDefault();
            if (card != null) lines.AddRange(CardDetails(card));
            return lines;
        }

        // ------------------------------------------------------------------ verbs

        public static string VerbName(Situation s)
        {
            if (s == null) return "";
            string label = "";
            try { label = s.Verb?.Label; } catch { }
            if (string.IsNullOrWhiteSpace(label)) label = s.VerbId;
            // Verbs spawned without a definition (Compendium creates them with label = id, e.g. "despair")
            // have no display name in the game; make the id readable.
            if (label == s.VerbId) label = PrettifyId(label);
            return TextCleaner.Clean(label);
        }

        private static string PrettifyId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string s = id.Replace('.', ' ').Replace('_', ' ').Trim();
            return s.Length == 0 ? id : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        public static string StateText(Situation s)
        {
            switch (s.StateIdentifier)
            {
                case StateEnum.Unstarted:
                    return CanStart(s) ? Strings.StateReady : Strings.StateIdle;
                case StateEnum.Ongoing:
                    return Strings.StateRunning;
                case StateEnum.Starting:
                    return Strings.StateStarting;
                case StateEnum.Complete:
                    return Strings.StateComplete;
                case StateEnum.Halting:
                    return Strings.StateHalting;
                default:
                    return Strings.StateBusy;
            }
        }

        public static bool CanStart(Situation s)
        {
            try
            {
                if (s.StateIdentifier != StateEnum.Unstarted) return false;
                var r = s.CurrentRecipe;
                if (r == null || !r.IsValid() || !r.Craftable) return false;
                var aic = GameAccess.Axe.GetAspectsInContext(s);
                return r.CanExecuteInContext(aic, Watchman.Get<Stable>().Protag());
            }
            catch
            {
                return false;
            }
        }

        public static int OutputCount(Situation s)
        {
            return GameAccess.DominionSpheres(s, SituationDominionEnum.Output).Sum(x => x.GetTotalElementsCount());
        }

        public static string RecipeLabel(Situation s)
        {
            try
            {
                string label = s.MetafictionalLabel;
                if (string.IsNullOrWhiteSpace(label) || label == ".") label = s.CurrentRecipe?.Label;
                return TextCleaner.Clean(label);
            }
            catch
            {
                return "";
            }
        }

        public static string RecipeText(Situation s)
        {
            try { return TextCleaner.CleanMultiline(s.MetafictionalDescription); }
            catch { return ""; }
        }

        public static string VerbSummary(Situation s, bool includeRecipe = true)
        {
            if (s == null) return "";
            var parts = new List<string> { VerbName(s), StateText(s) };
            if (s.StateIdentifier == StateEnum.Ongoing || s.StateIdentifier == StateEnum.Starting)
            {
                parts.Add(Strings.TimeLeft(GameAccess.FormatTime(s.TimeRemaining)));
                if (includeRecipe) parts.Add(RecipeLabel(s));
            }
            else if (s.StateIdentifier == StateEnum.Complete)
            {
                int n = OutputCount(s);
                if (n > 0) parts.Add(Strings.CardsWaiting(n));
                if (includeRecipe && ModConfig.Level != Verbosity.Terse) parts.Add(RecipeLabel(s));
            }
            else if (s.StateIdentifier == StateEnum.Unstarted && ModConfig.Level == Verbosity.Verbose)
            {
                var filled = GameAccess.SphereCards(GameAccess.ActiveThresholds(s));
                if (filled.Count > 0) parts.Add(string.Join(", ", filled.Select(CardName).ToArray()));
            }
            if (s.IsOpen) parts.Add(Strings.WindowOpen);
            return TextCleaner.Join(parts.ToArray());
        }

        public static List<string> VerbDetails(Situation s)
        {
            var lines = new List<string>();
            if (s == null) return lines;
            lines.Add(VerbSummary(s));
            try { lines.AddRange(TextCleaner.SplitIntoSpeechItems(s.Verb?.Description)); } catch { }
            string recipe = RecipeLabel(s);
            string text = RecipeText(s);
            string verbDesc = "";
            try { verbDesc = TextCleaner.CleanMultiline(s.Verb?.Description); } catch { }
            // An idle verb shows its own description as the note; do not read it twice.
            if (!string.IsNullOrEmpty(recipe) && recipe != VerbName(s)) lines.Add(Strings.Recipe + ": " + recipe);
            if (text != verbDesc) lines.AddRange(TextCleaner.SplitIntoSpeechItems(text));
            foreach (var slot in GameAccess.ActiveThresholds(s)) lines.Add(SlotSummary(slot));
            foreach (var card in GameAccess.SphereCards(GameAccess.DominionSpheres(s, SituationDominionEnum.Storage)))
                lines.Add(Strings.StoredHeading + ": " + CardSummary(card));
            foreach (var card in GameAccess.SphereCards(GameAccess.DominionSpheres(s, SituationDominionEnum.Output)))
                lines.Add(Strings.OutputHeading + ": " + CardSummary(card));
            string deck = DeckEffects(s);
            if (!string.IsNullOrEmpty(deck)) lines.Add(deck);
            string aspects = AspectSummary(SituationAspects(s));
            if (!string.IsNullOrEmpty(aspects)) lines.Add(Strings.Aspects + ": " + aspects);
            return lines;
        }

        /// <summary>The aspect total the window footer shows (contents, without element ids).</summary>
        public static AspectsDictionary SituationAspects(Situation s)
        {
            try { return s.GetAspects(false); }
            catch { return new AspectsDictionary(); }
        }

        /// <summary>"draws from Deck A (2)" for an ongoing recipe with deck effects, like the window's deck view.</summary>
        public static string DeckEffects(Situation s)
        {
            try
            {
                if (s.StateIdentifier != StateEnum.Ongoing) return "";
                var effects = s.CurrentRecipe?.DeckEffects;
                if (effects == null || effects.Count == 0) return "";
                var comp = GameAccess.Compendium;
                var aspects = s.GetAspects(true);
                var parts = new List<string>();
                foreach (var kv in effects)
                {
                    var spec = comp.GetEntityById<DeckSpec>(kv.Key);
                    if (spec == null || spec.Id == "NULL_DECKSPEC_ID" || spec.IsHidden) continue;
                    var effect = new DeckEffect(spec, kv.Value);
                    int draws = effect.GetDrawsConsideringAspects(aspects);
                    parts.Add(TextCleaner.Clean(spec.Label) + (draws > 1 ? " (" + draws + ")" : ""));
                }
                if (parts.Count == 0) return "";
                return Strings.DeckDraws + " " + string.Join(", ", parts.ToArray());
            }
            catch
            {
                return "";
            }
        }

        public static int NotesPageCount(Situation s)
        {
            var notes = GameAccess.GetNotes(s);
            return notes != null ? notes.NoteCount : 0;
        }

        public static int NotesPageIndex(Situation s)
        {
            var notes = GameAccess.GetNotes(s);
            return notes != null ? notes.CurrentIndex : 0;
        }
    }
}
