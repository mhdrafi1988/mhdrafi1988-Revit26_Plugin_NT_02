using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Grid
{
    /// <summary>One sort column.</summary>
    public sealed class SortKey
    {
        /// <summary>For the settings file.</summary>
        public SortKey() { }

        /// <summary>Sort by <paramref name="column"/>, descending when <paramref name="descending"/>.</summary>
        public SortKey(string column, bool descending) { Column = column; Descending = descending; }

        /// <summary>Column name: Source, Level, Number, Name, Area, Matched, Slab, Status.</summary>
        public string Column { get; set; }

        /// <summary>Largest first.</summary>
        public bool Descending { get; set; }
    }

    /// <summary>Everything the room grid remembers: filters, sort, grouping and hidden columns.</summary>
    public sealed class GridState
    {
        /// <summary>Show only rooms a keyword matched.</summary>
        public bool WetOnly { get; set; } = true;

        /// <summary>Levels shown by the level buttons; empty = every level.</summary>
        public HashSet<string> Levels { get; set; } = new();

        /// <summary>Tick-list filters by column (Source, Level, Matched, Status): the values to show.</summary>
        public Dictionary<string, HashSet<string>> TickFilters { get; set; } = new();

        /// <summary>"Contains" filters by column (Number, Name).</summary>
        public Dictionary<string, string> Contains { get; set; } = new();

        /// <summary>Smallest area to show, m²; null = no limit.</summary>
        public double? AreaMin { get; set; }

        /// <summary>Largest area to show, m²; null = no limit.</summary>
        public double? AreaMax { get; set; }

        /// <summary>Text looked for in every text column.</summary>
        public string Search { get; set; } = string.Empty;

        /// <summary>Sort columns, most important first; empty = level, then number.</summary>
        public List<SortKey> Sort { get; set; } = new();

        /// <summary>None, Level, Source or Matched.</summary>
        public string GroupBy { get; set; } = "Level";

        /// <summary>Columns the user has hidden.</summary>
        public List<string> HiddenColumns { get; set; } = new();

        /// <summary>Column order, left to right; empty = the default order.</summary>
        public List<string> ColumnOrder { get; set; } = new();

        /// <summary>Column widths in pixels, by column name.</summary>
        public Dictionary<string, double> ColumnWidths { get; set; } = new();

        /// <summary>
        /// A click on a level button. A plain click shows or hides that level; with
        /// <paramref name="solo"/> (Ctrl+click) only that level stays. The grid never ends up with
        /// no level: showing all levels, or hiding the last one, goes back to "all".
        /// </summary>
        public void ToggleLevel(string level, bool solo, IEnumerable<string> allLevels)
        {
            var all = (allLevels ?? Enumerable.Empty<string>()).ToList();
            if (string.IsNullOrEmpty(level)) return;

            if (solo)
            {
                bool alreadySolo = Levels.Count == 1 && Levels.Contains(level);
                Levels.Clear();
                if (!alreadySolo) Levels.Add(level);
                return;
            }

            if (Levels.Count == 0) foreach (string l in all) Levels.Add(l); // "all" → explicit list
            if (!Levels.Remove(level)) Levels.Add(level);
            if (Levels.Count == 0 || all.All(Levels.Contains)) Levels.Clear();
        }

        /// <summary>Removes every filter and the search text. Sort, grouping and columns stay.</summary>
        public void Clear()
        {
            Levels.Clear();
            TickFilters.Clear();
            Contains.Clear();
            AreaMin = AreaMax = null;
            Search = string.Empty;
        }

        /// <summary>Replaces nulls left by an old or hand-edited settings file. Returns this.</summary>
        public GridState Normalize()
        {
            Levels ??= new HashSet<string>();
            TickFilters ??= new Dictionary<string, HashSet<string>>();
            Contains ??= new Dictionary<string, string>();
            Sort ??= new List<SortKey>();
            Sort.RemoveAll(k => k == null || string.IsNullOrWhiteSpace(k.Column));
            HiddenColumns ??= new List<string>();
            ColumnOrder ??= new List<string>();
            ColumnWidths ??= new Dictionary<string, double>();
            Search ??= string.Empty;
            if (string.IsNullOrWhiteSpace(GroupBy)) GroupBy = "Level";
            return this;
        }
    }
}
