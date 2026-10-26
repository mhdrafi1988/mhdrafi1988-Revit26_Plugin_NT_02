using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TagToText.V001.Core.Services
{
    /// <summary>
    /// Revit-free text rules: splitting tag text into lines, adding prefixes, and naming the new view.
    /// Covered by Tests/TagToText.Tests.
    /// </summary>
    public static class TextRules
    {
        private static readonly string[] LineBreaks = { "\r\n", "\r", "\n" };

        /// <summary>
        /// Splits a tag's text into its non-empty, trimmed lines. Returns an empty array for null
        /// or blank text.
        /// </summary>
        public static string[] SplitLines(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            return text.Split(LineBreaks, StringSplitOptions.None)
                       .Select(l => l.Trim())
                       .Where(l => l.Length > 0)
                       .ToArray();
        }

        /// <summary>
        /// Puts <paramref name="prefix"/> in front of <paramref name="value"/> unless the value already
        /// starts with it (Revit includes some indicators in the displayed value and not others).
        /// A blank prefix leaves the value unchanged.
        /// </summary>
        public static string PrefixOnce(string prefix, string value)
        {
            value ??= string.Empty;
            if (string.IsNullOrWhiteSpace(prefix)) return value;
            string p = prefix.Trim();
            if (value.TrimStart().StartsWith(p, StringComparison.OrdinalIgnoreCase)) return value;
            return p + " " + value.TrimStart();
        }

        /// <summary>
        /// Adds <paramref name="suffix"/> after <paramref name="value"/> unless it already ends with it.
        /// </summary>
        public static string SuffixOnce(string value, string suffix)
        {
            value ??= string.Empty;
            if (string.IsNullOrWhiteSpace(suffix)) return value;
            string s = suffix.Trim();
            if (value.TrimEnd().EndsWith(s, StringComparison.OrdinalIgnoreCase)) return value;
            return value.TrimEnd() + " " + s;
        }

        /// <summary>
        /// Applies the user's prefix and suffix to every line. The user's text is added as typed
        /// (no extra space), so "EL " + "+12.450" reads "EL +12.450".
        /// </summary>
        public static string[] ApplyUserAffixes(IEnumerable<string> lines, string prefix, string suffix)
        {
            prefix ??= string.Empty;
            suffix ??= string.Empty;
            return (lines ?? Enumerable.Empty<string>()).Select(l => prefix + l + suffix).ToArray();
        }

        /// <summary>
        /// Returns <paramref name="wanted"/> if no existing name matches it (ignoring case), otherwise
        /// "wanted (2)", "wanted (3)"… Characters Revit refuses in view names are replaced with "-".
        /// A blank name falls back to "Tag To Text".
        /// </summary>
        public static string UniqueViewName(string wanted, IEnumerable<string> existingNames)
        {
            string name = CleanViewName(wanted);
            var taken = new HashSet<string>(existingNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(name)) return name;
            for (int i = 2; ; i++)
            {
                string candidate = $"{name} ({i})";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        /// <summary>Replaces characters Revit doesn't allow in names ({}[]|;&lt;&gt;?`~\:) and trims.</summary>
        public static string CleanViewName(string name)
        {
            const string invalid = "{}[]|;<>?`~\\:";
            if (string.IsNullOrWhiteSpace(name)) return "Tag To Text";
            var chars = name.Trim().Select(c => invalid.IndexOf(c) >= 0 ? '-' : c).ToArray();
            return new string(chars);
        }
    }
}
