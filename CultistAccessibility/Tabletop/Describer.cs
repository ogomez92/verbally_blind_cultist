using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Application.Spheres;
using CultistAccessibility.Core;
using SecretHistories.Abstract;
using SecretHistories.Commands;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Logic;
using SecretHistories.Enums;
using SecretHistories.Fucine;
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
                    lines.Add(Strings.BringsSlot(SlotSpecText(slot)));
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
            var spec = TextSpec(slot);
            if (spec == null) return Strings.SlotWord;
            string label = TextCleaner.Clean(spec.Label);
            if (string.IsNullOrEmpty(label)) label = TextCleaner.Clean(spec.Id);
            return label;
        }

        /// <summary>
        /// The slot's spec for its label and description. A slot's spec is saved with the game, texts included, in
        /// the language of the time; the verb's or recipe's slot with the same id gives the current language.
        /// </summary>
        private static SphereSpec TextSpec(Sphere slot)
        {
            var spec = slot?.GoverningSphereSpec;
            if (spec == null || string.IsNullOrEmpty(spec.Id)) return spec;
            try
            {
                if (!(slot.GetContainer() is Situation s)) return spec;
                // Verb slots exist only before the start, recipe slots only while it runs: look there first.
                var candidates = new List<SphereSpec>();
                var verb = CurrentEntity(s.Verb);
                foreach (var r in new[] { CurrentEntity(s.CurrentRecipe), CurrentEntity(s.FallbackRecipe) })
                    if (r?.Slots != null) candidates.AddRange(r.Slots);
                if (verb?.Thresholds != null) candidates.InsertRange(s.StateIdentifier == StateEnum.Unstarted ? 0 : candidates.Count, verb.Thresholds);
                return candidates.FirstOrDefault(c => c != null && c.Id == spec.Id) ?? spec;
            }
            catch
            {
                return spec;
            }
        }

        public static string SlotSummary(Sphere slot)
        {
            if (slot == null) return "";
            var parts = new List<string> { Strings.SlotNamed(SlotLabel(slot)) };
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
                lines.AddRange(TextCleaner.SplitIntoSpeechItems(TextSpec(slot).Description));
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
                VisibleNote(s, out string label, out _);
                if (string.IsNullOrWhiteSpace(label) || label == ".") label = CurrentEntity(s.CurrentRecipe)?.Label;
                return TextCleaner.Clean(label);
            }
            catch
            {
                return "";
            }
        }

        public static string RecipeText(Situation s)
        {
            try
            {
                VisibleNote(s, out _, out string text);
                return TextCleaner.CleanMultiline(text);
            }
            catch { return ""; }
        }

        /// <summary>
        /// The note the verb window shows. Notes are saved as text in the language the game had when they were
        /// written, so a game continued in another language shows old text (in the game's own window too). The
        /// latest note is written again the way the game writes it for the current state (RecipeNote), in the
        /// current language, when the saved one matches neither its title nor its text. Older pages stay as saved.
        /// </summary>
        public static void VisibleNote(Situation s, out string title, out string text)
        {
            title = s.MetafictionalLabel;
            text = s.MetafictionalDescription;
            Relocalise(s, GameAccess.GetNotes(s)?.GetVisibleToken(), ref title, ref text);
        }

        /// <summary>One page of the verb's notes, title and text, the latest one in the current language.</summary>
        public static string NotePageText(Situation s, Token page)
        {
            string title = page.Payload.MetafictionalLabel;
            string text = page.Payload.MetafictionalDescription;
            Relocalise(s, page, ref title, ref text);
            return TextCleaner.Sentences(new[] { title, text });
        }

        private static void Relocalise(Situation s, Token note, ref string title, ref string text)
        {
            try
            {
                var notes = GameAccess.GetNotes(s);
                if (notes == null || note == null || !ReferenceEquals(note, notes.GetLastToken())) return;
                if (!CurrentStateNote(s, out string freshTitle, out string freshText)) return;
                if (SameText(freshTitle, title) || SameText(freshText, text)) return;
                title = freshTitle;
                text = freshText;
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("Note relocalisation failed: " + ex.Message);
            }
        }

        /// <summary>The latest note as the game writes it now (UnstartedState, OngoingState, RecipeCompletionNotesCommand).</summary>
        private static bool CurrentStateNote(Situation s, out string title, out string text)
        {
            title = text = null;
            var recipe = CurrentEntity(s.CurrentRecipe);
            RecipeNote note;
            switch (s.StateIdentifier)
            {
                case StateEnum.Unstarted:
                    if (s.CurrentRecipe == s.FallbackRecipe || recipe == null || !recipe.IsValid())
                    {
                        var verb = CurrentEntity(s.Verb);
                        if (verb == null) return false;
                        title = verb.Label;
                        text = verb.Description;
                        return true;
                    }
                    note = RecipeNote.StartDescription(recipe, s, additive: false);
                    break;
                case StateEnum.Starting:
                case StateEnum.Ongoing:
                    if (recipe == null || !recipe.IsValid()) return false;
                    note = RecipeNote.StartDescription(recipe, s);
                    break;
                case StateEnum.Complete:
                    if (recipe == null || !recipe.IsValid()) return false;
                    note = RecipeNote.EndDescription(recipe, s);
                    break;
                default:
                    return false;
            }
            title = note.Title;
            text = note.Description;
            return true;
        }

        private static bool SameText(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

        /// <summary>
        /// The entity with the same id in the compendium loaded now (the game reloads it when the language changes),
        /// or the given one when there is none.
        /// </summary>
        private static T CurrentEntity<T>(T entity) where T : class, IEntityWithId
        {
            if (entity == null || string.IsNullOrEmpty(entity.Id)) return entity;
            try { return GameAccess.Compendium?.GetEntityById<T>(entity.Id) ?? entity; }
            catch { return entity; }
        }

        public static string VerbSummary(Situation s, bool includeRecipe = true)
        {
            if (s == null) return "";
            var parts = new List<string> { VerbName(s), StateText(s) };
            if (s.StateIdentifier == StateEnum.Ongoing || s.StateIdentifier == StateEnum.Starting)
            {
                parts.Add(Strings.TimeLeft(GameAccess.FormatTime(s.TimeRemaining)));
                if (includeRecipe) parts.Add(RecipeLabel(s));
                parts.Add(OngoingSlotText(s));
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

        /// <summary>
        /// The slot a running recipe opened, which the game shows as a mini slot on the verb token
        /// (VerbManifestation.DisplayRecipeThreshold): "Offering slot open" or the card it holds.
        /// </summary>
        public static string OngoingSlotText(Situation s)
        {
            if (s == null || s.StateIdentifier != StateEnum.Ongoing) return "";
            var parts = new List<string>();
            foreach (var slot in GameAccess.ActiveThresholds(s))
            {
                var card = slot.GetElementTokens().FirstOrDefault();
                parts.Add(card != null ? Strings.OngoingSlotHolds(SlotLabel(slot), CardName(card)) : Strings.OngoingSlotOpen(SlotLabel(slot)));
            }
            return string.Join(", ", parts.ToArray());
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

        /// <summary>What the game's deck details window shows for each deck of the running recipe: label, description.</summary>
        public static List<string> DeckDetails(Situation s)
        {
            var lines = new List<string> { DeckEffects(s) };
            try
            {
                var effects = s.CurrentRecipe?.DeckEffects;
                if (effects == null) return lines;
                foreach (var kv in effects)
                {
                    var spec = GameAccess.Compendium.GetEntityById<DeckSpec>(kv.Key);
                    if (spec == null || spec.Id == "NULL_DECKSPEC_ID" || spec.IsHidden) continue;
                    string desc = TextCleaner.CleanMultiline(spec.Description);
                    if (!string.IsNullOrEmpty(desc)) lines.Add(TextCleaner.Clean(spec.Label) + ": " + desc);
                }
            }
            catch { }
            return lines;
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
