using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SecretHistories.Services;
using SecretHistories.UI;
using UnityEngine;

namespace CultistAccessibility.Core
{
    /// <summary>
    /// The mod's own translations. One table per game culture id (en, es, de, fr, ru, jp, zh), embedded from
    /// Lang/&lt;id&gt;.txt ("Key = text" lines, # comments). A file lang\&lt;id&gt;.txt next to the dll overrides or adds
    /// entries, so a translation can be corrected or a new language added without rebuilding.
    /// The language is the game's own "Culture" config value, read on every lookup: changing the language in the
    /// game's options changes the mod's speech at once. Missing keys fall back to English.
    /// </summary>
    internal static class Loc
    {
        private const string Fallback = "en";

        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex GameLabel = new Regex(@"\[\[([A-Za-z0-9_]+)\]\]", RegexOptions.Compiled);
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static string _iniCulture;
        private static float _iniReadAt = -100f;

        /// <summary>The game's culture id. Before the game's Config service exists (plugin start), from its config.ini.</summary>
        public static string CultureId
        {
            get
            {
                try
                {
                    if (Watchman.Exists<Config>())
                    {
                        string id = Watchman.Get<Config>().GetConfigValue("Culture");
                        if (!string.IsNullOrEmpty(id)) return id;
                    }
                }
                catch { }
                return CultureFromIni();
            }
        }

        private static string CultureFromIni()
        {
            if (_iniCulture != null && Time.realtimeSinceStartup - _iniReadAt < 5f) return _iniCulture;
            _iniReadAt = Time.realtimeSinceStartup;
            _iniCulture = Fallback;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "config.ini");
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0 || !line.Substring(0, eq).Trim().Equals("Culture", StringComparison.OrdinalIgnoreCase)) continue;
                        string value = line.Substring(eq + 1).Trim();
                        if (value.Length > 0) _iniCulture = value;
                        break;
                    }
                }
            }
            catch { }
            return _iniCulture;
        }

        public static string Get(string key)
        {
            return ResolveGameLabels(Raw(key));
        }

        private static string Raw(string key)
        {
            string id = CultureId;
            string value;
            if (Table(id).TryGetValue(key, out value)) return value;
            if (Table(Fallback).TryGetValue(key, out value))
            {
                if (!id.Equals(Fallback, StringComparison.OrdinalIgnoreCase)) ReportMissing(id, key);
                return value;
            }
            ReportMissing(Fallback, key);
            return key;
        }

        public static bool Has(string key)
        {
            return Table(CultureId).ContainsKey(key) || Table(Fallback).ContainsKey(key);
        }

        public static string Format(string key, params object[] args)
        {
            string format = Raw(key);
            try { return ResolveGameLabels(string.Format(format, args)); }
            catch (FormatException)
            {
                ReportMissing(CultureId, key + " (bad placeholders)");
                return ResolveGameLabels(format);
            }
        }

        /// <summary>
        /// [[UI_CONTINUE]] in a translation becomes the game's own label for that key, so the help names buttons
        /// exactly as the game does in the current language.
        /// </summary>
        private static string ResolveGameLabels(string text)
        {
            if (text.IndexOf("[[", StringComparison.Ordinal) < 0) return text;
            return GameLabel.Replace(text, m =>
            {
                string key = m.Groups[1].Value;
                try
                {
                    string label = Watchman.Get<ILocStringProvider>()?.Get(key);
                    if (!string.IsNullOrEmpty(label) && !label.StartsWith("MISSING_")) return TextCleaner.Clean(label);
                }
                catch { }
                return key.Substring(key.IndexOf('_') + 1).Replace('_', ' ').ToLowerInvariant();
            });
        }

        /// <summary>
        /// Picks Key.one / Key.few / Key.many / Key.other by the language's plural rule (missing forms fall back to
        /// Key.other), then formats with the count as {0} followed by <paramref name="args"/>.
        /// </summary>
        public static string Plural(string key, int count, params object[] args)
        {
            string id = CultureId;
            string form = key + "." + PluralCategory(id, count);
            if (!Table(id).ContainsKey(form))
                form = Table(id).ContainsKey(key + ".other") || !Table(Fallback).ContainsKey(form) ? key + ".other" : form;
            var all = new object[args.Length + 1];
            all[0] = count;
            Array.Copy(args, 0, all, 1, args.Length);
            return Format(form, all);
        }

        /// <summary>Key.1, Key.2, ... until the first missing number, each formatted with the same arguments.</summary>
        public static List<string> Lines(string key, params object[] args)
        {
            var lines = new List<string>();
            for (int i = 1; Has(key + "." + i); i++)
                lines.Add(Format(key + "." + i, args));
            return lines;
        }

        private static string PluralCategory(string cultureId, int n)
        {
            switch ((cultureId ?? "").ToLowerInvariant())
            {
                case "jp":
                case "ja":
                case "zh":
                case "zh-hans":
                    return "other";
                case "fr":
                    return n == 0 || n == 1 ? "one" : "other";
                case "ru":
                    int d = n % 10, h = n % 100;
                    if (d == 1 && h != 11) return "one";
                    if (d >= 2 && d <= 4 && (h < 12 || h > 14)) return "few";
                    return "many";
                default:
                    return n == 1 ? "one" : "other";
            }
        }

        private static Dictionary<string, string> Table(string id)
        {
            Dictionary<string, string> table;
            if (Tables.TryGetValue(id, out table)) return table;
            table = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string wanted = ".Lang." + id + ".txt";
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (!name.EndsWith(wanted, StringComparison.OrdinalIgnoreCase)) continue;
                    using (var stream = assembly.GetManifestResourceStream(name))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        Parse(reader.ReadToEnd(), table);
                }
                string file = Path.Combine(Path.Combine(Plugin.PluginDir ?? "", "lang"), id + ".txt");
                if (File.Exists(file)) Parse(File.ReadAllText(file, Encoding.UTF8), table);
            }
            catch (Exception ex) { Plugin.LogWarning("Language " + id + " failed to load: " + ex.Message); }
            if (table.Count == 0 && !id.Equals(Fallback, StringComparison.OrdinalIgnoreCase))
                Plugin.LogWarning("No translation for the game language '" + id + "': the mod speaks English.");
            Tables[id] = table;
            return table;
        }

        private static void Parse(string text, Dictionary<string, string> table)
        {
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                table[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }

        private static void ReportMissing(string id, string key)
        {
            if (Reported.Add(id + "/" + key)) Plugin.LogWarning("Missing translation " + id + ": " + key);
        }
    }
}
