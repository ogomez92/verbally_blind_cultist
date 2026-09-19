using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CultistAccessibility.Core
{
    /// <summary>
    /// Every string goes through <see cref="Clean"/> before it is spoken: TextMeshPro rich text tags are removed
    /// (sprites become their names), bracketed key hints from button labels are kept readable,
    /// and whitespace is collapsed.
    /// </summary>
    internal static class TextCleaner
    {
        private static readonly Regex SpriteTag = new Regex("<sprite[^>]*name=\"?([^\"> ]+)\"?[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnyTag = new Regex("<[^<>]{1,200}>", RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex("\\s+", RegexOptions.Compiled);
        private static readonly Regex MissingLabel = new Regex("MISSING_([A-Z0-9_]+)", RegexOptions.Compiled);

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string s = text;
            s = s.Replace("​", "").Replace("­", "").Replace(" ", " ");
            if (s.IndexOf('<') >= 0)
            {
                s = SpriteTag.Replace(s, m => " " + m.Groups[1].Value.Replace('_', ' ') + " ");
                s = s.Replace("<br>", ". ").Replace("<br/>", ". ").Replace("<br />", ". ");
                s = AnyTag.Replace(s, "");
            }
            if (s.IndexOf("MISSING_", System.StringComparison.Ordinal) >= 0)
                s = MissingLabel.Replace(s, m => m.Groups[1].Value.Replace('_', ' ').ToLowerInvariant());
            s = Whitespace.Replace(s, " ").Trim();
            return s;
        }

        /// <summary>Text on its own line becomes a sentence, so multi-line labels read naturally.</summary>
        public static string CleanMultiline(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var lines = text.Replace("\r", "").Split('\n');
            var sb = new StringBuilder();
            foreach (var raw in lines)
            {
                string line = Clean(raw);
                if (line.Length == 0) continue;
                if (sb.Length > 0)
                {
                    char last = sb[sb.Length - 1];
                    if (last != '.' && last != '!' && last != '?' && last != ':' && last != ',') sb.Append('.');
                    sb.Append(' ');
                }
                sb.Append(line);
            }
            return sb.ToString();
        }

        /// <summary>Splits long text into review-buffer items at sentence and line boundaries.</summary>
        public static List<string> SplitIntoSpeechItems(string text, int maxLength = 300)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (var para in text.Replace("\r", "").Split('\n'))
            {
                string p = Clean(para);
                if (p.Length == 0) continue;
                if (p.Length <= maxLength)
                {
                    result.Add(p);
                    continue;
                }
                var sb = new StringBuilder();
                foreach (var sentence in Regex.Split(p, "(?<=[\\.!\\?])\\s+"))
                {
                    if (sb.Length > 0 && sb.Length + sentence.Length > maxLength)
                    {
                        result.Add(sb.ToString().Trim());
                        sb.Clear();
                    }
                    sb.Append(sentence).Append(' ');
                }
                if (sb.Length > 0) result.Add(sb.ToString().Trim());
            }
            return result;
        }

        public static bool IsPlaceholder(string cleaned)
        {
            if (string.IsNullOrEmpty(cleaned)) return true;
            switch (cleaned)
            {
                case "New Text":
                case "Text":
                case "Button":
                case "[HINT]":
                case "[VALUE]":
                case "- ACTION -":
                case "?":
                case "Title":
                case "Description":
                case "...":
                case ".....":
                case "!":
                    return true;
            }
            if (cleaned.StartsWith("Lorem", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (cleaned.StartsWith("This is button bar text", System.StringComparison.Ordinal)) return true;
            if (cleaned.StartsWith("[") && cleaned.EndsWith("]") && cleaned.ToUpperInvariant() == cleaned) return true;
            return false;
        }

        /// <summary>Joins non-empty parts with ", ".</summary>
        public static string Join(params string[] parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(p.Trim());
            }
            return sb.ToString();
        }

        /// <summary>"A. B. C" joining for sentences.</summary>
        public static string Sentences(IEnumerable<string> parts)
        {
            var sb = new StringBuilder();
            foreach (var raw in parts)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string p = raw.Trim();
                if (sb.Length > 0)
                {
                    char last = sb[sb.Length - 1];
                    if (last != '.' && last != '!' && last != '?' && last != ':') sb.Append('.');
                    sb.Append(' ');
                }
                sb.Append(p);
            }
            return sb.ToString();
        }
    }
}
