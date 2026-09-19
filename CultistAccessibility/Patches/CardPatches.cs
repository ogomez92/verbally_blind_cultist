using System;
using System.Collections.Generic;
using System.Linq;
using CultistAccessibility.Core;
using CultistAccessibility.Tabletop;
using HarmonyLib;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;

namespace CultistAccessibility.Patches
{
    /// <summary>
    /// Card events on the table that happen without the player: decay/transformation (ElementStack.ChangeTo),
    /// vanishing (ElementStack.Retire with a visible effect), cards arriving (TabletopSphere.AcceptToken with a
    /// non-player context), and greedy slots grabbing cards (GreedyAngel.GrabStack).
    /// </summary>
    internal static class CardPatches
    {
        private static readonly List<string> PendingArrivals = new List<string>();
        private static float _flushAt;
        // The context of the travel that is arriving (TokenTravelItinerary.Arrive keeps the departure context),
        // so a merge on arrival can tell the player's own card coming back from a card the game sends.
        private static Context.ActionSource? _arrivingSource;

        public static void TryPatch(Harmony harmony)
        {
            PatchRegistry.Patch(harmony, typeof(CardPatches), "SecretHistories.UI.ElementStack", "ChangeTo", new[] { typeof(string) },
                prefix: nameof(ChangeToPrefix), postfix: nameof(ChangeToPostfix));
            PatchRegistry.Patch(harmony, typeof(CardPatches), "SecretHistories.UI.ElementStack", "Retire", new[] { typeof(RetirementVFX) },
                prefix: nameof(RetirePrefix));
            PatchRegistry.Patch(harmony, typeof(CardPatches), "SecretHistories.Spheres.TabletopSphere", "AcceptToken", new[] { typeof(Token), typeof(Context) },
                postfix: nameof(AcceptTokenPostfix));
            PatchRegistry.Patch(harmony, typeof(CardPatches), "SecretHistories.Spheres.Angels.GreedyAngel", "GrabStack", new[] { typeof(Token) },
                prefix: nameof(GrabStackPrefix));
            PatchRegistry.Patch(harmony, typeof(CardPatches), "SecretHistories.UI.ElementStack", "InteractWithIncoming", new[] { typeof(Token) },
                prefix: nameof(MergePrefix), postfix: nameof(MergePostfix));
            PatchRegistry.Patch(harmony, typeof(CardPatches), "SecretHistories.UI.TokenTravelItinerary", "Arrive", new[] { typeof(Token), typeof(Context) },
                prefix: nameof(ArrivePrefix), postfix: nameof(ArrivePostfix));
        }

        public static void ClearPendingArrivals()
        {
            PendingArrivals.Clear();
        }

        /// <summary>Moves the player made (or loading and bookkeeping) are not news.</summary>
        private static bool IsQuietSource(Context.ActionSource source)
        {
            switch (source)
            {
                case Context.ActionSource.PlayerDrag:
                case Context.ActionSource.CalvedStack:
                case Context.ActionSource.Loading:
                case Context.ActionSource.Eden:
                case Context.ActionSource.Debug:
                case Context.ActionSource.UI:
                case Context.ActionSource.Metafictional:
                case Context.ActionSource.SphereReferenceLocationChanged:
                    return true;
                default:
                    return false;
            }
        }

        public static void ArrivePrefix(Context context)
        {
            _arrivingSource = context?.actionSource ?? Context.ActionSource.Unknown;
        }

        public static void ArrivePostfix()
        {
            _arrivingSource = null;
        }

        private static bool Armed
        {
            get
            {
                var c = AccessibilityController.Instance;
                return c != null && ScreenTracker.Current == GameScreen.Tabletop && c.Tabletop.Events.Armed;
            }
        }

        public static void ChangeToPrefix(ElementStack __instance, out string __state)
        {
            __state = null;
            try
            {
                if (!Armed || !ModConfig.AnnounceCardChanges.Value) return;
                var token = __instance.GetToken();
                if (token == null || !GameAccess.IsOnTable(token) || __instance.IsShrouded) return;
                __state = TextCleaner.Clean(__instance.Label);
            }
            catch { }
        }

        public static void ChangeToPostfix(ElementStack __instance, string __state)
        {
            try
            {
                if (__state == null) return;
                string now = TextCleaner.Clean(__instance.Label);
                if (string.IsNullOrEmpty(now) || now == __state) return;
                Speech.SayEvent(Strings.CardBecame(__state, now));
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("ChangeTo patch failed: " + ex.Message);
            }
        }

        public static void RetirePrefix(ElementStack __instance, RetirementVFX VFX)
        {
            try
            {
                if (!Armed || !ModConfig.AnnounceCardChanges.Value) return;
                if (__instance.Defunct) return;
                if (VFX == RetirementVFX.None || VFX == RetirementVFX.CardHide) return;
                var token = __instance.GetToken();
                if (token == null || !GameAccess.IsOnTable(token)) return;
                if (__instance.Element == null || __instance.Element.Metafictional) return;
                string name = __instance.IsShrouded ? Strings.FaceDown : TextCleaner.Clean(__instance.Label);
                Speech.SayEvent(Strings.CardGone(name));
            }
            catch { }
        }

        public static void AcceptTokenPostfix(Token token, Context context)
        {
            try
            {
                if (!Armed || !ModConfig.AnnounceCardsArriving.Value) return;
                if (token == null || token.Defunct || !token.IsValidElementStack() || token.Payload.Metafictional) return;
                if (IsQuietSource(context?.actionSource ?? Context.ActionSource.Unknown)) return;
                PendingArrivals.Add(Describer.CardSummary(token));
                _flushAt = Time.unscaledTime + 0.6f;
            }
            catch { }
        }

        /// <summary>
        /// A card arriving on the table merges into a matching stack instead of landing (TokenTravelItinerary
        /// TryMergeWithTokenAtDestination), so TabletopSphere.AcceptToken never sees it: announce it here.
        /// Merges of cards already on the table (the Stack button) are not arrivals.
        /// </summary>
        public static void MergePrefix(ElementStack __instance, Token incomingToken, out string __state)
        {
            __state = null;
            try
            {
                if (!Armed || !ModConfig.AnnounceCardsArriving.Value) return;
                var target = __instance.GetToken();
                if (target == null || !GameAccess.IsOnTable(target)) return;
                if (incomingToken == null || incomingToken.Sphere == null || incomingToken.Sphere is TabletopSphere) return;
                if (_arrivingSource.HasValue && IsQuietSource(_arrivingSource.Value)) return;
                __state = Describer.CardName(incomingToken) + (incomingToken.Quantity > 1 ? " " + Strings.Quantity(incomingToken.Quantity) : "");
            }
            catch { }
        }

        public static void MergePostfix(ElementStack __instance, string __state)
        {
            try
            {
                if (__state == null) return;
                PendingArrivals.Add(Strings.MergedInto(__state, __instance.Quantity));
                _flushAt = Time.unscaledTime + 0.6f;
            }
            catch { }
        }

        /// <summary>Called every frame: arrivals within a short window are spoken as one line.</summary>
        public static void FlushArrivals()
        {
            if (PendingArrivals.Count == 0 || Time.unscaledTime < _flushAt) return;
            var grouped = PendingArrivals.GroupBy(x => x).Select(g => g.Count() > 1 ? g.Key + " " + Strings.Quantity(g.Count()) : g.Key).ToList();
            PendingArrivals.Clear();
            Speech.SayEvent(Strings.CardsArrived(string.Join(", ", grouped.ToArray())));
        }

        public static void GrabStackPrefix(Token matchingToken, ThresholdSphere ____thresholdSphereToGrabTo)
        {
            try
            {
                if (!Armed || !ModConfig.AnnounceGreedyGrabs.Value) return;
                string card = Describer.CardName(matchingToken);
                string verb = "";
                if (____thresholdSphereToGrabTo != null && ____thresholdSphereToGrabTo.GetContainer() is Situation s)
                    verb = Describer.VerbName(s);
                if (string.IsNullOrEmpty(verb)) verb = Describer.SlotLabel(____thresholdSphereToGrabTo);
                Speech.SayEvent(Strings.GreedyGrab(verb, card));
            }
            catch { }
        }
    }
}
