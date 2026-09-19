using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Application.Spheres;
using CultistAccessibility.Core;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Infrastructure;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;

namespace CultistAccessibility.Tabletop
{
    /// <summary>
    /// Read-only access to game state for the tabletop (verified against the decompiled SecretHistories.Main).
    /// Everything here tolerates a half-loaded table and returns empty results rather than throwing.
    /// </summary>
    internal static class GameAccess
    {
        public static HornedAxe Axe => Watchman.Get<HornedAxe>();
        public static Heart Heart => Watchman.Get<Heart>();
        public static Meniscate Meniscate => Watchman.Get<Meniscate>();
        public static Compendium Compendium => Watchman.Get<Compendium>();
        public static LocalNexus Nexus => Watchman.Get<LocalNexus>();
        public static Numa Numa => Watchman.Exists<Numa>() ? Watchman.Get<Numa>() : null;
        public static ILocStringProvider Loc => Watchman.Get<ILocStringProvider>();

        public static bool TableReady
        {
            get
            {
                try
                {
                    return Axe != null && Watchman.Exists<Meniscate>() && Watchman.Exists<Heart>() && TabletopSphere != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>The main table (~/tabletop in dicta.json).</summary>
        public static Sphere TabletopSphere
        {
            get
            {
                try
                {
                    var axe = Axe;
                    if (axe == null) return null;
                    return axe.GetSpheresOfCategory(SphereCategory.World).FirstOrDefault(s => s is SecretHistories.Spheres.TabletopSphere && !s.Defunct);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static bool IsOnTable(Token token)
        {
            if (token == null || token.Defunct || token.Sphere == null) return false;
            return token.Sphere.SphereCategory == SphereCategory.World;
        }

        /// <summary>Verbs whose token is on (or travelling across) the table.</summary>
        public static List<Situation> TableSituations()
        {
            var result = new List<Situation>();
            try
            {
                foreach (var s in Axe.GetRegisteredSituations())
                {
                    if (s == null || s.Defunct) continue;
                    var token = s.GetToken();
                    if (token == null || token.Defunct) continue;
                    result.Add(s);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("TableSituations failed: " + ex.Message);
            }
            return result;
        }

        /// <summary>Element stacks lying on world spheres (the table), excluding anything in motion.</summary>
        public static List<Token> TableCards(bool includeTravelling = false)
        {
            var result = new List<Token>();
            try
            {
                foreach (var sphere in Axe.GetSpheresOfCategory(SphereCategory.World))
                {
                    if (sphere == null || sphere.Defunct) continue;
                    if (!(sphere is SecretHistories.Spheres.TabletopSphere)) continue;
                    foreach (var t in sphere.GetElementTokens())
                    {
                        if (t.Payload.Metafictional) continue;
                        if (!includeTravelling && t.CurrentState != null && t.CurrentState.InSystemDrivenMotion()) continue;
                        result.Add(t);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("TableCards failed: " + ex.Message);
            }
            return result;
        }

        /// <summary>Mansus portals lying on the table (Ingress payloads); the chosen card waits inside them.</summary>
        public static List<Token> TablePortals()
        {
            var result = new List<Token>();
            try
            {
                var table = TabletopSphere;
                if (table == null) return result;
                foreach (var t in table.Tokens)
                {
                    if (t == null || t.Defunct) continue;
                    if (t.Payload is SecretHistories.Tokens.Payloads.Ingress) result.Add(t);
                }
            }
            catch { }
            return result;
        }

        public static List<Token> PortalCards(Token portal)
        {
            try
            {
                var ingress = portal.Payload as SecretHistories.Tokens.Payloads.Ingress;
                if (ingress == null || ingress.Defunct) return new List<Token>();
                return ingress.GetSpheres().SelectMany(s => s.GetElementTokens()).Where(x => !x.Defunct).ToList();
            }
            catch
            {
                return new List<Token>();
            }
        }

        public static NotesSphere GetNotes(Situation s)
        {
            try { return s.GetSpheresByCategory(SphereCategory.Notes).OfType<NotesSphere>().FirstOrDefault(); }
            catch { return null; }
        }

        public static List<Sphere> DominionSpheres(Situation s, SituationDominionEnum which)
        {
            try
            {
                var d = s.GetDominion(which);
                if (d == null) return new List<Sphere>();
                return d.Spheres.Where(x => x != null && !x.Defunct).ToList();
            }
            catch
            {
                return new List<Sphere>();
            }
        }

        /// <summary>Slots the player can fill right now, in window order.</summary>
        public static List<Sphere> ActiveThresholds(Situation s)
        {
            var result = new List<Sphere>();
            foreach (var d in new[] { SituationDominionEnum.VerbThresholds, SituationDominionEnum.RecipeThresholds })
            {
                foreach (var sphere in DominionSpheres(s, d))
                {
                    if (sphere.SphereCategory != SphereCategory.Threshold) continue;
                    try
                    {
                        if (!s.State.IsActiveInThisState(sphere)) continue;
                    }
                    catch
                    {
                        continue;
                    }
                    result.Add(sphere);
                }
            }
            return result;
        }

        public static List<Token> SphereCards(IEnumerable<Sphere> spheres)
        {
            var result = new List<Token>();
            foreach (var sphere in spheres)
            {
                foreach (var t in sphere.GetElementTokens())
                {
                    if (t.Payload.Metafictional) continue;
                    result.Add(t);
                }
            }
            return result;
        }

        public static Situation OpenSituation()
        {
            try
            {
                foreach (var s in Axe.GetRegisteredSituations())
                    if (s != null && !s.Defunct && s.IsOpen) return s;
            }
            catch { }
            return null;
        }

        public static bool MansusActive
        {
            get
            {
                try
                {
                    var numa = Numa;
                    return numa != null && numa.IsOtherworldActive();
                }
                catch
                {
                    return false;
                }
            }
        }

        public static Element GetElement(string id)
        {
            try { return Compendium?.GetEntityById<Element>(id); }
            catch { return null; }
        }

        /// <summary>Speakable duration: "4.5 seconds", "30 seconds", "1 minute 20 seconds".</summary>
        public static string FormatTime(float seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds < 10f)
            {
                float rounded = Mathf.Round(seconds * 10f) / 10f;
                if (Math.Abs(rounded - Mathf.Round(rounded)) < 0.01f) return Strings.Seconds(Mathf.RoundToInt(rounded));
                return Strings.SecondsFraction(rounded);
            }
            int total = Mathf.CeilToInt(seconds);
            if (total < 60) return Strings.Seconds(total);
            int m = total / 60;
            int s = total % 60;
            if (s == 0) return Strings.Minutes(m);
            return Strings.MinutesAndSeconds(Strings.Minutes(m), Strings.Seconds(s));
        }

        public static void PointCameraAt(Token token)
        {
            if (!ModConfig.MoveCameraToFocus.Value || token == null || token.Defunct) return;
            try
            {
                if (token.Sphere == null || token.Sphere.AreLocalPositionsInScreenSpace()) return;
                var cam = Watchman.Get<CamOperator>();
                cam?.PointCameraAtPosition_CurrentZoom(token.transform.position, 0.3f, null);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("Camera move failed: " + ex.Message);
            }
        }
    }
}
