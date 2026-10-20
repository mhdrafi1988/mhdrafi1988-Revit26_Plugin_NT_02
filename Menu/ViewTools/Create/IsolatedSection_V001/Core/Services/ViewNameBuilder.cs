using System;
using System.Collections.Generic;
using System.Text;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Services
{
    /// <summary>Builds a valid, unique Revit view name from the name pattern. No Revit API dependency.</summary>
    public static class ViewNameBuilder
    {
        /// <summary>Name used when the pattern gives an empty name.</summary>
        public const string Fallback = "ISO-3D";

        // Characters Revit does not allow in view names.
        private const string Invalid = "\\:{}[]|;<>?`~";

        /// <summary>
        /// Replaces {Category} {Id} {Count} {Side} {Date} (case-insensitive) in <paramref name="pattern"/>,
        /// then swaps characters Revit refuses for "-".
        /// </summary>
        public static string Build(string pattern, string category, long id, int count, string side, DateTime date)
        {
            string s = string.IsNullOrWhiteSpace(pattern) ? Fallback : pattern;
            s = Replace(s, "{Category}", category);
            s = Replace(s, "{Id}", id.ToString());
            s = Replace(s, "{Count}", count.ToString());
            s = Replace(s, "{Side}", side);
            s = Replace(s, "{Date}", date.ToString("yyyy-MM-dd"));

            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(Invalid.IndexOf(c) >= 0 || char.IsControl(c) ? '-' : c);

            string name = sb.ToString().Trim();
            return name.Length == 0 ? Fallback : name;
        }

        /// <summary>
        /// Returns <paramref name="name"/>, or "name (2)", "name (3)"… when a view already has it
        /// (compared case-insensitively, as Revit does).
        /// </summary>
        public static string MakeUnique(string name, ICollection<string> existing)
        {
            if (existing == null || !Contains(existing, name)) return name;
            for (int i = 2; ; i++)
            {
                string candidate = $"{name} ({i})";
                if (!Contains(existing, candidate)) return candidate;
            }
        }

        private static bool Contains(ICollection<string> existing, string name)
        {
            foreach (string e in existing)
                if (string.Equals(e, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Replace(string s, string token, string value)
            => s.Replace(token, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
