using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout
{
    /// <summary>One item to sort: its marker position in the source view (null when unknown) and its name.</summary>
    public readonly record struct ReadingItem(double? Across, double? Up, string Name);

    /// <summary>
    /// Sorts section markers the way the plan reads: top to bottom in row bands, left to right in
    /// each band. Markers whose heights differ by no more than the tolerance (measured from the
    /// band's running average, so a slowly rising line of markers does not drift into one band)
    /// share a band. Markers with no position go last, by name. Pure math, no Revit types.
    /// </summary>
    public static class ReadingOrder
    {
        /// <summary>Returns the indexes of <paramref name="items"/> in reading order.</summary>
        public static List<int> Sort(IReadOnlyList<ReadingItem> items, double rowTolerance)
        {
            var result = new List<int>();
            if (items == null || items.Count == 0)
                return result;

            double tolerance = double.IsFinite(rowTolerance) && rowTolerance > 0 ? rowTolerance : 0;
            var names = NaturalStringComparer.Instance;

            var placed = Enumerable.Range(0, items.Count)
                .Where(i => items[i].Across.HasValue && items[i].Up.HasValue)
                .OrderByDescending(i => items[i].Up.Value)
                .ThenBy(i => items[i].Across.Value)
                .ToList();

            var bands = new List<List<int>>();
            double bandSum = 0;
            foreach (int i in placed)
            {
                double up = items[i].Up.Value;
                var band = bands.LastOrDefault();
                if (band == null || Math.Abs(up - bandSum / band.Count) > tolerance)
                {
                    band = new List<int>();
                    bands.Add(band);
                    bandSum = 0;
                }
                band.Add(i);
                bandSum += up;
            }

            foreach (var band in bands)
                result.AddRange(band
                    .OrderBy(i => items[i].Across.Value)
                    .ThenBy(i => items[i].Name, names));

            result.AddRange(Enumerable.Range(0, items.Count)
                .Where(i => !(items[i].Across.HasValue && items[i].Up.HasValue))
                .OrderBy(i => items[i].Name, names));

            return result;
        }
    }

    /// <summary>Compares text with digit runs compared as numbers: "Section 2" before "Section 10". Case-insensitive.</summary>
    public sealed class NaturalStringComparer : IComparer<string>
    {
        /// <summary>Shared instance.</summary>
        public static NaturalStringComparer Instance { get; } = new();

        /// <inheritdoc/>
        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int si = i, sj = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;
                    string a = x.Substring(si, i - si).TrimStart('0');
                    string b = y.Substring(sj, j - sj).TrimStart('0');
                    if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                    int byDigits = string.CompareOrdinal(a, b);
                    if (byDigits != 0) return byDigits;
                }
                else
                {
                    int byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                    if (byChar != 0) return byChar;
                    i++;
                    j++;
                }
            }

            int byLength = (x.Length - i).CompareTo(y.Length - j);
            return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
        }
    }
}
