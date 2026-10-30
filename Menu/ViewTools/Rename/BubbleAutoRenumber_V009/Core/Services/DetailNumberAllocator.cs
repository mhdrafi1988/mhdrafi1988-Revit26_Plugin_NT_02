using System.Collections.Generic;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Services
{
    /// <summary>
    /// Hands out detail numbers that are unique on the sheet, and the temporary
    /// placeholders that make a clean rewrite possible. No Revit API.
    ///
    /// THE V008 BUG THIS EXISTS TO FIX
    /// ------------------------------
    /// V008 seeded its used-numbers set from every view on the sheet:
    ///
    ///     var used = CollectExistingNumbers(doc, sheet);   // ALL viewports
    ///     …
    ///     string unique = MakeUnique(current.ToString(), used);
    ///
    /// The views about to be renumbered were in that set, so they collided with
    /// themselves. Renumbering a sheet already numbered 1,2,3 from 1 produced
    /// "1 (D)", "2 (D)", "3 (D)" — and running it again produced "1 (D2)" and so on.
    ///
    /// V009 seeds only from views that are NOT being renumbered (out of scope, or a
    /// read-only detail number — those keep what they have, so their numbers are
    /// genuinely taken). A "(D)" suffix now means a real clash with a view the user
    /// chose to leave alone.
    ///
    /// WHY THE TEMP PASS
    /// -----------------
    /// Revit rejects two viewports on one sheet holding the same detail number. Writing
    /// finals straight over the old numbers can therefore collide mid-sequence — view B
    /// still holds "2" when view A is told to become "2". So every target is first
    /// stamped with a placeholder no view holds, and the finals are written afterwards,
    /// all inside one transaction. Blanking to "" instead is not reliably accepted for
    /// every view type, which is why this uses a token rather than an empty string.
    /// </summary>
    public static class DetailNumberAllocator
    {
        /// <summary>Appended when a base number is already taken: "3" → "3 (D)" → "3 (D2)".
        /// Same shape V008 produced, so a genuine clash still reads the way users expect.</summary>
        private const string DuplicateMarker = "D";

        /// <summary>
        /// Sequential numbers from <paramref name="startNumber"/>, skipping anything in
        /// <paramref name="reserved"/>. Every number returned is added to
        /// <paramref name="reserved"/>, so the caller's set stays the live picture.
        /// </summary>
        public static List<string> Allocate(int count, int startNumber, HashSet<string> reserved)
            => Allocate(count, startNumber, reserved, NumberStyle.Numeric);

        /// <summary>As above, with labels written in <paramref name="style"/>
        /// (1, 01, A or a); <paramref name="startNumber"/> is the 1-based index of the first label.</summary>
        public static List<string> Allocate(int count, int startNumber, HashSet<string> reserved, NumberStyle style)
        {
            int pad = NumberFormatter.PadWidthFor(startNumber + count - 1);
            var allocated = new List<string>(count);
            HashSet<string> used = reserved ?? new HashSet<string>();

            int next = startNumber;

            for (int i = 0; i < count; i++)
            {
                allocated.Add(MakeUnique(NumberFormatter.Format(next, style, pad), used));
                next++;
            }

            return allocated;
        }

        /// <summary>
        /// <paramref name="baseNumber"/> if free, else "base (D)", "base (D2)", "base (D3)"…
        /// The chosen value is added to <paramref name="used"/>.
        /// </summary>
        public static string MakeUnique(string baseNumber, HashSet<string> used)
        {
            if (used == null) return baseNumber;

            if (used.Add(baseNumber)) return baseNumber;

            string first = $"{baseNumber} ({DuplicateMarker})";
            if (used.Add(first)) return first;

            int i = 2;
            while (true)
            {
                string attempt = $"{baseNumber} ({DuplicateMarker}{i})";
                if (used.Add(attempt)) return attempt;
                i++;
            }
        }

        /// <summary>
        /// One placeholder per target, guaranteed to clash with nothing: not with a
        /// reserved number, not with a planned final, not with each other. The prefix
        /// grows a tilde at a time until it is unused, so even a sheet whose numbers
        /// genuinely look like "~R1" cannot defeat it.
        /// </summary>
        public static List<string> AllocateTemporaries(
            int count,
            IEnumerable<string> reserved,
            IEnumerable<string> plannedFinals)
        {
            var taken = new HashSet<string>();

            if (reserved != null)
                foreach (string value in reserved)
                    taken.Add(value);

            if (plannedFinals != null)
                foreach (string value in plannedFinals)
                    taken.Add(value);

            string prefix = "~R";

            while (ClashesWithAny(prefix, count, taken))
                prefix = "~" + prefix;

            var temporaries = new List<string>(count);

            for (int i = 1; i <= count; i++)
                temporaries.Add($"{prefix}{i}");

            return temporaries;
        }

        private static bool ClashesWithAny(string prefix, int count, HashSet<string> taken)
        {
            for (int i = 1; i <= count; i++)
            {
                if (taken.Contains($"{prefix}{i}")) return true;
            }

            return false;
        }
    }
}
