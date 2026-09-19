using System;
using System.Collections.Generic;
using System.Linq;
using CultistAccessibility.Core;
using CultistAccessibility.Core.Buffers;
using SecretHistories.Entities;
using SecretHistories.Enums;
using UnityEngine;

namespace CultistAccessibility.Tabletop
{
    /// <summary>
    /// Polls every verb on the table (4 times a second) and announces what a sighted player would notice:
    /// verbs starting, continuing into a linked recipe, finishing, appearing or vanishing, and dangerous
    /// countdowns (recipes that signal an ending flavour) about to run out.
    /// Polling state instead of patching keeps working whatever code path changed the verb.
    /// </summary>
    internal sealed class TabletopEvents
    {
        private sealed class Snapshot
        {
            public StateEnum State;
            public string RecipeId;
            public bool Warned;
            public string Verb;
            public float TimeRemaining;
            // Slots of the running recipe already announced (or present when the table was loaded).
            public readonly HashSet<SecretHistories.Spheres.Sphere> KnownSlots = new HashSet<SecretHistories.Spheres.Sphere>();
        }

        private readonly Dictionary<Situation, Snapshot> _snaps = new Dictionary<Situation, Snapshot>();
        private bool _armed;
        private float _nextPoll;

        public bool Armed => _armed;

        public void Reset()
        {
            _snaps.Clear();
            _armed = false;
        }

        /// <summary>Takes a silent snapshot of the table as loaded; changes after this are announced.</summary>
        public void Arm()
        {
            _snaps.Clear();
            foreach (var s in GameAccess.TableSituations())
            {
                var snap = Take(s);
                foreach (var slot in GameAccess.ActiveThresholds(s)) snap.KnownSlots.Add(slot);
                _snaps[s] = snap;
            }
            _armed = true;
        }

        private static Snapshot Take(Situation s)
        {
            return new Snapshot
            {
                State = s.StateIdentifier,
                RecipeId = SafeRecipeId(s),
                Verb = Describer.VerbName(s),
                TimeRemaining = s.TimeRemaining
            };
        }

        private static string SafeRecipeId(Situation s)
        {
            try { return s.CurrentRecipe?.Id ?? ""; }
            catch { return ""; }
        }

        public void Tick()
        {
            if (!_armed) return;
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.25f;

            List<Situation> current;
            try { current = GameAccess.TableSituations(); }
            catch { return; }

            foreach (var s in current)
            {
                try { Check(s); }
                catch (Exception ex) { Plugin.LogDebug("Event check failed: " + ex.Message); }
            }

            var gone = _snaps.Keys.Where(k => !current.Contains(k)).ToList();
            foreach (var s in gone)
            {
                var snap = _snaps[s];
                _snaps.Remove(s);
                if (ModConfig.AnnounceNewVerbs.Value) Speech.SayEvent(Strings.VerbVanished(snap.Verb));
            }
        }

        private void Check(Situation s)
        {
            if (!_snaps.TryGetValue(s, out var snap))
            {
                snap = Take(s);
                _snaps[s] = snap;
                if (ModConfig.AnnounceNewVerbs.Value)
                {
                    string text = Strings.VerbAppeared(Describer.VerbName(s));
                    if (s.StateIdentifier == StateEnum.Ongoing)
                        text += ". " + TextCleaner.Join(Describer.RecipeLabel(s), Strings.TimeLeft(GameAccess.FormatTime(s.TimeRemaining)));
                    Speech.SayEvent(text);
                }
                return;
            }

            StateEnum now = s.StateIdentifier;
            string recipeId = SafeRecipeId(s);

            if (now != snap.State)
            {
                StateEnum was = snap.State;
                snap.State = now;
                snap.RecipeId = recipeId;
                snap.Warned = false;
                snap.TimeRemaining = s.TimeRemaining;
                // Slots of the new state are announced by the next poll, after this line.
                snap.KnownSlots.Clear();
                if (now == StateEnum.Ongoing && ModConfig.AnnounceSituationStarted.Value)
                {
                    string time = GameAccess.FormatTime(s.TimeRemaining);
                    if (was == StateEnum.RequiringExecution || was == StateEnum.Ongoing)
                        Speech.SayEvent(Strings.VerbContinues(Describer.VerbName(s), Describer.RecipeLabel(s), time));
                    else
                        Speech.SayEvent(Strings.VerbStarted(Describer.VerbName(s), Describer.RecipeLabel(s), time));
                    StoreStory(s);
                }
                else if (now == StateEnum.Complete)
                {
                    AnnounceCompletion(s);
                }
                return;
            }

            if (now == StateEnum.Ongoing)
            {
                // The timer went back up: a linked recipe (or the same one again) started between two polls.
                bool restarted = s.TimeRemaining > snap.TimeRemaining + 1f;
                snap.TimeRemaining = s.TimeRemaining;
                if (recipeId != snap.RecipeId || restarted)
                {
                    snap.RecipeId = recipeId;
                    snap.Warned = false;
                    if (restarted) snap.KnownSlots.Clear();
                    // A card in the running recipe's slot changes what comes next (OngoingState.UpdateRecipePrediction
                    // shows the alternative's text in the window); the timer keeps running.
                    bool prediction = !restarted && IsPrediction(s);
                    if (ModConfig.AnnounceSituationStarted.Value)
                    {
                        string line = prediction
                            ? Strings.VerbWillBecome(Describer.VerbName(s), Describer.RecipeLabel(s))
                            : Strings.VerbContinues(Describer.VerbName(s), Describer.RecipeLabel(s), GameAccess.FormatTime(s.TimeRemaining));
                        if (prediction && s.IsOpen) line = TextCleaner.Sentences(new[] { line, Describer.RecipeText(s) });
                        Speech.SayEvent(line);
                    }
                    StoreStory(s);
                }
                AnnounceNewSlots(s, snap);
                if (!snap.Warned && ModConfig.AnnounceTimerWarnings.Value && IsDangerous(s) && s.TimeRemaining <= ModConfig.TimerWarningSeconds.Value && s.TimeRemaining > 0f)
                {
                    snap.Warned = true;
                    Speech.SayEvent(TextCleaner.Join(Strings.VerbDanger(Describer.VerbName(s), GameAccess.FormatTime(s.TimeRemaining)), Describer.RecipeLabel(s)));
                }
            }
        }

        private static bool IsPrediction(Situation s)
        {
            try { return s.CurrentRecipe != null && s.FallbackRecipe != null && s.CurrentRecipe != s.FallbackRecipe; }
            catch { return false; }
        }

        /// <summary>
        /// A running recipe opened a slot: the game shows a mini slot on the verb token with a sound
        /// (VerbManifestation.ShowMiniSlot). Greedy slots that already grabbed their card were announced by CardPatches.
        /// </summary>
        private static void AnnounceNewSlots(Situation s, Snapshot snap)
        {
            foreach (var slot in GameAccess.ActiveThresholds(s))
            {
                if (!snap.KnownSlots.Add(slot)) continue;
                if (!ModConfig.AnnounceOngoingSlots.Value || slot.GetElementTokens().Any()) continue;
                Speech.SayEvent(Strings.VerbSlotOpened(Describer.VerbName(s), Describer.SlotSummary(slot)));
            }
            snap.KnownSlots.RemoveWhere(k => k == null || k.Defunct);
        }

        /// <summary>The recipe signals an ending (the game changes the music and countdown colour for these).</summary>
        private static bool IsDangerous(Situation s)
        {
            try
            {
                var shadow = s.GetTimeshadow();
                if (shadow != null && shadow.EndingFlavour != EndingFlavour.None) return true;
                return s.CurrentRecipe != null && s.CurrentRecipe.SignalEndingFlavour != EndingFlavour.None;
            }
            catch
            {
                return false;
            }
        }

        private static void StoreStory(Situation s)
        {
            string text = Describer.RecipeText(s);
            if (string.IsNullOrEmpty(text)) return;
            BufferManager.AddStory(Describer.VerbName(s) + ": " + Describer.RecipeLabel(s) + ". " + text);
        }

        private static void AnnounceCompletion(Situation s)
        {
            string verb = Describer.VerbName(s);
            string recipe = Describer.RecipeLabel(s);
            string text = Describer.RecipeText(s);
            if (!string.IsNullOrEmpty(text)) BufferManager.AddStory(verb + ": " + recipe + ". " + text);
            if (!ModConfig.AnnounceSituationCompleted.Value) return;
            string line = Strings.VerbCompleted(verb, recipe);
            int n = Describer.OutputCount(s);
            if (n > 0) line += ", " + Strings.CardsWaiting(n);
            if (ModConfig.ReadCompletionText.Value && !string.IsNullOrEmpty(text)) line += ". " + text;
            Speech.SayEvent(line);
        }
    }
}
