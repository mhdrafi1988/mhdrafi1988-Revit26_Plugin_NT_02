using System;
using System.Collections.Generic;
using System.Text;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V003.Core.Matching
{
    /// <summary>Decides whether a name (a room, a fixture family) matches a keyword.</summary>
    public static class KeywordMatcher
    {
        /// <summary>
        /// Returns the first keyword that matches <paramref name="name"/>, or null. Case and
        /// punctuation are ignored ("W.C." and "wc" are the same). A one-word keyword must start a
        /// word of the name ("wash" matches "Washroom", not "Dishwasher"); a keyword of several
        /// words must match words that follow each other ("powder room").
        /// </summary>
        public static string Match(string name, IEnumerable<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(name) || keywords == null) return null;
            List<string> words = Words(name);
            foreach (string raw in keywords)
            {
                string keyword = raw?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(keyword)) continue;
                List<string> parts = Words(keyword);
                if (parts.Count == 0) continue;

                for (int start = 0; start + parts.Count <= words.Count; start++)
                {
                    bool hit = true;
                    for (int i = 0; i < parts.Count && hit; i++)
                    {
                        bool last = i == parts.Count - 1;
                        hit = last ? words[start + i].StartsWith(parts[i], StringComparison.Ordinal) : words[start + i] == parts[i];
                    }
                    if (hit) return keyword;
                }
            }
            return null;
        }

        /// <summary>Lower-case words of the text; dots are dropped so "W.C." reads "wc".</summary>
        private static List<string> Words(string text)
        {
            var words = new List<string>();
            var current = new StringBuilder();
            foreach (char c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) current.Append(c);
                else if (c == '.') continue;
                else if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
            }
            if (current.Length > 0) words.Add(current.ToString());
            return words;
        }
    }
}
