using System;
using System.Collections.Generic;
using System.Text;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Matching
{
    /// <summary>Decides whether a room name marks a wet area.</summary>
    public static class KeywordMatcher
    {
        /// <summary>
        /// Returns the first keyword that a word of <paramref name="roomName"/> starts with, or null.
        /// Case and punctuation are ignored ("W.C." matches "wc"); a keyword inside a word does not
        /// count ("Dishwasher" does not match "wash").
        /// </summary>
        public static string Match(string roomName, IEnumerable<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(roomName) || keywords == null) return null;
            var words = Words(roomName);
            foreach (string raw in keywords)
            {
                string keyword = raw?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(keyword)) continue;
                foreach (string word in words)
                    if (word.StartsWith(keyword, StringComparison.Ordinal)) return keyword;
            }
            return null;
        }

        /// <summary>Lower-case words of the name; dots are dropped so "W.C." reads "wc".</summary>
        private static List<string> Words(string name)
        {
            var words = new List<string>();
            var current = new StringBuilder();
            foreach (char c in name.ToLowerInvariant())
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
