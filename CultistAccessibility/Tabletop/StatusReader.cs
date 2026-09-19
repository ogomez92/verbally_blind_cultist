using System;
using System.Collections.Generic;
using System.Linq;
using CultistAccessibility.Core;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.UI;

namespace CultistAccessibility.Tabletop
{
    /// <summary>Hotkey readouts that work from anywhere on the table: status bar and busy verbs.</summary>
    internal static class StatusReader
    {
        /// <summary>Character name, profession and the status bar counts defined by the active legacy.</summary>
        public static string StatusText()
        {
            var parts = new List<string>();
            try
            {
                Character c = Watchman.Get<Stable>()?.Protag();
                if (c != null)
                {
                    parts.Add(TextCleaner.Join(TextCleaner.Clean(c.Name), TextCleaner.Clean(c.Profession)));
                    var legacy = c.ActiveLegacy;
                    var specs = legacy != null ? legacy.StatusBarElements : null;
                    if (specs == null || specs.Count == 0) specs = StatusBarElementSpec.GetDefaultStatusBar();
                    var extant = GameAccess.Axe.GetAspectsInContext().AspectsExtant;
                    foreach (var spec in specs ?? new List<StatusBarElementSpec>())
                    {
                        if (spec?.Ids == null) continue;
                        var bits = new List<string>();
                        for (int i = 0; i < spec.Ids.Count; i++)
                        {
                            string id = spec.Ids[i];
                            int value = extant.AspectValue(id);
                            if (i > 0 && value == 0) continue;
                            var e = GameAccess.GetElement(id);
                            string label = e != null && e.IsValid() ? TextCleaner.Clean(e.Label) : id;
                            bits.Add(label + " " + value);
                        }
                        if (bits.Count > 0) parts.Add(string.Join(", ", bits.ToArray()));
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("StatusText failed: " + ex.Message);
            }
            parts.Add(SpeedText());
            return TextCleaner.Sentences(parts);
        }

        public static string SpeedText()
        {
            try
            {
                var heart = GameAccess.Heart;
                if (heart == null) return "";
                return SpeedName(heart.GetEffectiveGameSpeed());
            }
            catch
            {
                return "";
            }
        }

        public static string SpeedName(GameSpeed speed)
        {
            switch (speed)
            {
                case GameSpeed.Paused: return Strings.Paused;
                case GameSpeed.Normal: return Strings.NormalSpeed;
                case GameSpeed.Fast: return Strings.FastSpeed;
                case GameSpeed.VeryFast:
                case GameSpeed.VeryVeryFast: return Strings.VeryFastSpeed;
                default: return "";
            }
        }

        /// <summary>Game speed, then busy verbs soonest first, then verbs with results waiting.</summary>
        public static string TimersText()
        {
            var parts = new List<string> { SpeedText() };
            var verbs = GameAccess.TableSituations();
            var busy = verbs.Where(v => v.StateIdentifier == StateEnum.Ongoing || v.StateIdentifier == StateEnum.Starting)
                .OrderBy(v => v.TimeRemaining).ToList();
            if (busy.Count == 0) parts.Add(Strings.NoBusyVerbs);
            foreach (var v in busy)
                parts.Add(TextCleaner.Join(Strings.Busy(Describer.VerbName(v), GameAccess.FormatTime(v.TimeRemaining)), Describer.RecipeLabel(v)));
            var done = verbs.Where(v => v.StateIdentifier == StateEnum.Complete).ToList();
            foreach (var v in done)
                parts.Add(TextCleaner.Join(Describer.VerbName(v), Strings.StateComplete));
            var decaying = GameAccess.TableCards().Where(t => t.Payload.GetTimeshadow() != null && t.Payload.GetTimeshadow().Transient)
                .OrderBy(t => t.Payload.GetTimeshadow().LifetimeRemaining).ToList();
            foreach (var t in decaying)
                parts.Add(Describer.CardSummary(t));
            return TextCleaner.Sentences(parts);
        }

        public static List<string> VerbLines()
        {
            if (!GameAccess.TableReady) return null;
            return GameAccess.TableSituations().Select(s => Describer.VerbSummary(s)).ToList();
        }

        public static List<string> TableLines()
        {
            if (!GameAccess.TableReady) return null;
            return GameAccess.TableCards()
                .OrderBy(t => Describer.CardName(t), StringComparer.CurrentCultureIgnoreCase)
                .Select(Describer.CardSummary).ToList();
        }

        public static List<string> StatusLines()
        {
            if (!GameAccess.TableReady) return null;
            return new List<string> { StatusText(), TimersText() };
        }
    }
}
