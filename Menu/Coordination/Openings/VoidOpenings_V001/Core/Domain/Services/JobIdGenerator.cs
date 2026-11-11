using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services
{
    /// <summary>Formats, reads and issues Job IDs of the form <c>VOID-2026-000001</c>.</summary>
    public static class JobIdGenerator
    {
        private static readonly Regex Pattern = new Regex(@"^VOID-(\d{4})-(\d{6})$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Largest number that fits the six-digit format.</summary>
        public const int MaxNumber = 999999;

        /// <summary>Formats a Job ID.</summary>
        public static string Format(int year, int number)
        {
            if (number < 1 || number > MaxNumber) throw new ArgumentOutOfRangeException(nameof(number));
            return string.Format(CultureInfo.InvariantCulture, "VOID-{0:0000}-{1:000000}", year, number);
        }

        /// <summary>Reads the year and number out of a Job ID. Returns false for anything that is not one.</summary>
        public static bool TryParse(string text, out int year, out int number)
        {
            year = 0;
            number = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            Match match = Pattern.Match(text.Trim());
            if (!match.Success)
                return false;
            year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            number = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// The next free Job ID for a year: one more than the highest number already used that year,
        /// taking both the IDs found in the model and the stored counter. Using both means a lost or
        /// stale counter can never produce a duplicate.
        /// </summary>
        /// <param name="existingIds">Job IDs already in the model; other text and nulls are ignored.</param>
        /// <param name="year">Year to issue for.</param>
        /// <param name="lastIssued">The stored counter for that year (0 when there is none).</param>
        public static string Next(IEnumerable<string> existingIds, int year, int lastIssued = 0)
        {
            int highest = Math.Max(0, lastIssued);
            if (existingIds != null)
            {
                foreach (string id in existingIds)
                    if (TryParse(id, out int y, out int n) && y == year && n > highest)
                        highest = n;
            }
            if (highest >= MaxNumber)
                throw new InvalidOperationException($"No Job ID numbers are left for {year}.");
            return Format(year, highest + 1);
        }
    }
}
