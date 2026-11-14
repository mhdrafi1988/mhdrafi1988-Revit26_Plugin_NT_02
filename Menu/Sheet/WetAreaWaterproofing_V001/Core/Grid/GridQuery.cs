using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid
{
    /// <summary>A level button: the level's name and how many rooms it has.</summary>
    public readonly record struct LevelCount(string Level, int Count, double ElevationMm);

    /// <summary>Filters and sorts the room grid.</summary>
    public static class GridQuery
    {
        /// <summary>Text shown for an empty value in tick-list filters.</summary>
        public const string NoValue = "(none)";

        /// <summary>Text of <paramref name="row"/> in a filterable column; <see cref="NoValue"/> when empty.</summary>
        public static string Value(RoomRow row, string column)
        {
            string text = column switch
            {
                "Source" => row.Source,
                "Level" => row.Level,
                "Number" => row.Number,
                "Name" => row.Name,
                "Matched" => row.Matched,
                "Slab" => row.SlabText,
                "Status" => row.Status.ToString(),
                "Area" => row.AreaM2.ToString("0.00", CultureInfo.InvariantCulture),
                _ => null
            };
            return string.IsNullOrEmpty(text) ? NoValue : text;
        }

        /// <summary>Rows that pass every filter in <paramref name="st"/>, sorted.</summary>
        public static IReadOnlyList<RoomRow> Apply(IEnumerable<RoomRow> rows, GridState st)
        {
            if (rows == null) return new List<RoomRow>();
            st ??= new GridState();
            IEnumerable<RoomRow> query = rows.Where(r => r != null && Passes(r, st));

            IOrderedEnumerable<RoomRow> ordered = null;
            foreach (SortKey key in st.Sort ?? new List<SortKey>())
            {
                if (key == null || string.IsNullOrWhiteSpace(key.Column)) continue;
                var comparer = Comparer(key.Column);
                ordered = ordered == null
                    ? (key.Descending ? query.OrderByDescending(r => r, comparer) : query.OrderBy(r => r, comparer))
                    : (key.Descending ? ordered.ThenByDescending(r => r, comparer) : ordered.ThenBy(r => r, comparer));
            }
            ordered = ordered == null
                ? query.OrderBy(r => r, Comparer("Level")).ThenBy(r => r, Comparer("Number"))
                : ordered.ThenBy(r => r, Comparer("Level")).ThenBy(r => r, Comparer("Number"));
            return ordered.ToList();
        }

        /// <summary>Levels with their room counts, lowest level first.</summary>
        public static IReadOnlyList<LevelCount> LevelCounts(IEnumerable<RoomRow> rows, bool wetOnly)
            => (rows ?? Enumerable.Empty<RoomRow>())
                .Where(r => r != null && (!wetOnly || !string.IsNullOrEmpty(r.Matched)))
                .GroupBy(r => r.Level ?? NoValue)
                .Select(g => new LevelCount(g.Key, g.Count(), g.Min(r => r.LevelElevationMm)))
                .OrderBy(l => l.ElevationMm).ThenBy(l => l.Level, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>How many filters are on (levels, each column filter, the area range, the search).</summary>
        public static int ActiveFilterCount(GridState st)
        {
            if (st == null) return 0;
            int count = 0;
            if (st.Levels != null && st.Levels.Count > 0) count++;
            if (st.TickFilters != null) count += st.TickFilters.Count(f => f.Value != null && f.Value.Count > 0);
            if (st.Contains != null) count += st.Contains.Count(f => !string.IsNullOrWhiteSpace(f.Value));
            if (st.AreaMin.HasValue || st.AreaMax.HasValue) count++;
            if (!string.IsNullOrWhiteSpace(st.Search)) count++;
            return count;
        }

        private static bool Passes(RoomRow r, GridState st)
        {
            if (st.WetOnly && string.IsNullOrEmpty(r.Matched)) return false;
            if (st.Levels != null && st.Levels.Count > 0 && !st.Levels.Contains(r.Level ?? NoValue)) return false;

            if (st.TickFilters != null)
                foreach (var filter in st.TickFilters)
                    if (filter.Value != null && filter.Value.Count > 0 && !filter.Value.Contains(Value(r, filter.Key)))
                        return false;

            if (st.Contains != null)
                foreach (var filter in st.Contains)
                    if (!string.IsNullOrWhiteSpace(filter.Value)
                        && Value(r, filter.Key).IndexOf(filter.Value.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                        return false;

            if (st.AreaMin.HasValue && r.AreaM2 < st.AreaMin.Value - 1e-9) return false;
            if (st.AreaMax.HasValue && r.AreaM2 > st.AreaMax.Value + 1e-9) return false;

            if (!string.IsNullOrWhiteSpace(st.Search))
            {
                string needle = st.Search.Trim();
                bool hit = new[] { r.Number, r.Name, r.Source, r.Level, r.Matched, r.SlabText, r.Status.ToString(), r.StatusDetail }
                    .Any(t => !string.IsNullOrEmpty(t) && t.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hit) return false;
            }
            return true;
        }

        private static IComparer<RoomRow> Comparer(string column) => column switch
        {
            "Level" => Comparer<RoomRow>.Create((a, b) =>
            {
                int byElevation = a.LevelElevationMm.CompareTo(b.LevelElevationMm);
                return byElevation != 0 ? byElevation : Natural(a.Level, b.Level);
            }),
            "Area" => Comparer<RoomRow>.Create((a, b) => a.AreaM2.CompareTo(b.AreaM2)),
            "Slab" => Comparer<RoomRow>.Create((a, b) => Nullable.Compare(a.SlabTopMm, b.SlabTopMm)),
            "Status" => Comparer<RoomRow>.Create((a, b) => a.Status.CompareTo(b.Status)),
            _ => Comparer<RoomRow>.Create((a, b) => Natural(Value(a, column), Value(b, column)))
        };

        /// <summary>Text compare that reads digit runs as numbers, so "102" comes before "1001".</summary>
        private static int Natural(string a, string b)
        {
            a ??= string.Empty; b ??= string.Empty;
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string na = a.Substring(si, i - si).TrimStart('0'), nb = b.Substring(sj, j - sj).TrimStart('0');
                    if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                    int byDigits = string.CompareOrdinal(na, nb);
                    if (byDigits != 0) return byDigits;
                }
                else
                {
                    int byChar = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                    if (byChar != 0) return byChar;
                    i++; j++;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }
    }
}
