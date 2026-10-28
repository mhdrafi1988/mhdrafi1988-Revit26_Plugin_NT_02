using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout
{
    /// <summary>
    /// Sheet numbers for the new sheets: a suggestion for the first one, and the numbers of the
    /// sheets that follow it (trailing digits counted up, numbers already in the model skipped).
    /// Comparisons ignore case, like Revit. Pure, no Revit types.
    /// </summary>
    public static class SheetNumbering
    {
        /// <summary>Used when nothing was created before.</summary>
        public const string DefaultFirst = "SEC-001";

        private static readonly Regex TrailingDigits = new(@"^(.*?)(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// The first free number at or after <paramref name="start"/> (or <see cref="DefaultFirst"/>
        /// when it is empty), counting its trailing digits up.
        /// </summary>
        public static string SuggestFrom(string start, IEnumerable<string> existing)
        {
            var taken = ToSet(existing);
            string candidate = string.IsNullOrWhiteSpace(start) ? DefaultFirst : start.Trim();
            for (int i = 0; i < 100000 && taken.Contains(candidate); i++)
                candidate = Next(candidate, 1);
            return candidate;
        }

        /// <summary>The number that follows <paramref name="lastUsed"/>, skipping taken numbers.</summary>
        public static string SuggestAfter(string lastUsed, IEnumerable<string> existing)
            => string.IsNullOrWhiteSpace(lastUsed)
                ? SuggestFrom(DefaultFirst, existing)
                : SuggestFrom(Next(lastUsed.Trim(), 1), existing);

        /// <summary>
        /// <paramref name="count"/> numbers: <paramref name="first"/> as typed, then the next free
        /// ones. Returns an empty list when <paramref name="first"/> is blank.
        /// </summary>
        public static List<string> Sequence(string first, int count, IEnumerable<string> existing)
        {
            var numbers = new List<string>();
            if (string.IsNullOrWhiteSpace(first) || count <= 0)
                return numbers;

            var taken = ToSet(existing);
            string current = first.Trim();
            numbers.Add(current);
            taken.Add(current);

            int step = 1;
            while (numbers.Count < count && step < 100000)
            {
                string candidate = Next(first.Trim(), step++);
                if (taken.Add(candidate))
                    numbers.Add(candidate);
            }
            return numbers;
        }

        /// <summary>
        /// <paramref name="number"/> with its trailing digits increased by <paramref name="step"/>,
        /// keeping leading zeros ("SEC-009" + 1 = "SEC-010"). Without trailing digits: "A" + 1 = "A-2".
        /// </summary>
        public static string Next(string number, int step)
        {
            number ??= string.Empty;
            var m = TrailingDigits.Match(number);
            if (!m.Success)
                return number + "-" + (step + 1).ToString(CultureInfo.InvariantCulture);

            string digits = m.Groups[2].Value;
            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
                return number + "-" + (step + 1).ToString(CultureInfo.InvariantCulture);

            string next = (value + step).ToString(CultureInfo.InvariantCulture);
            return m.Groups[1].Value + next.PadLeft(digits.Length, '0');
        }

        private static HashSet<string> ToSet(IEnumerable<string> existing)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existing != null)
                foreach (var s in existing)
                    if (!string.IsNullOrWhiteSpace(s))
                        set.Add(s.Trim());
            return set;
        }
    }
}
