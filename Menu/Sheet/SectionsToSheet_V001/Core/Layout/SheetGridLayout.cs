using Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SectionsToSheet.V001.Core.Layout
{
    /// <summary>Inputs of <see cref="SheetGridLayout.Compute"/>. Lengths in feet.</summary>
    public sealed class GridLayoutOptions
    {
        /// <summary>Grid (columns line up) or Rows (flow like text).</summary>
        public ArrangeMode Mode { get; init; } = ArrangeMode.Grid;

        /// <summary>Smallest gap between two views side by side.</summary>
        public double MinHorizontalGap { get; init; }

        /// <summary>Smallest gap between two rows.</summary>
        public double MinVerticalGap { get; init; }

        /// <summary>Where the rows sit vertically.</summary>
        public VerticalMode Vertical { get; init; } = VerticalMode.Top;

        /// <summary>How a short last row is placed (Rows only).</summary>
        public LastRowMode LastRow { get; init; } = LastRowMode.Left;

        /// <summary>True: fill one sheet only; what does not fit is reported in <see cref="GridLayoutResult.NoRoom"/>.</summary>
        public bool SinglePage { get; init; }
    }

    /// <summary>Where one item lands: the bottom-left corner of its footprint, on a page.</summary>
    public sealed class GridSlot
    {
        /// <summary>Index of the item in the input list.</summary>
        public int Index { get; init; }

        /// <summary>0-based sheet.</summary>
        public int Page { get; init; }

        /// <summary>0-based row on its sheet, counted from the top.</summary>
        public int Row { get; init; }

        /// <summary>0-based position in its row, counted from the left (the column in Grid mode).</summary>
        public int Column { get; init; }

        /// <summary>Left edge of the item's footprint.</summary>
        public double MinX { get; init; }

        /// <summary>Bottom edge of the item's footprint.</summary>
        public double MinY { get; init; }
    }

    /// <summary>One sheet of the layout.</summary>
    public sealed class GridPage
    {
        /// <summary>Slots in reading order (row by row, left to right).</summary>
        public List<GridSlot> Slots { get; } = new();

        /// <summary>Number of rows.</summary>
        public int RowCount { get; init; }

        /// <summary>Smallest gap between neighbours in a row, or null when no row has two items.</summary>
        public double? HorizontalGap { get; init; }

        /// <summary>Gap between rows, or null for a single row.</summary>
        public double? VerticalGap { get; init; }
    }

    /// <summary>Output of <see cref="SheetGridLayout.Compute"/>.</summary>
    public sealed class GridLayoutResult
    {
        /// <summary>The sheets, in order. Empty when nothing fits.</summary>
        public List<GridPage> Pages { get; } = new();

        /// <summary>Columns used in Grid mode; 0 in Rows mode.</summary>
        public int Columns { get; init; }

        /// <summary>Items wider or taller than the usable area (input indexes).</summary>
        public List<int> TooBig { get; } = new();

        /// <summary>Items left over when <see cref="GridLayoutOptions.SinglePage"/> is on (input indexes).</summary>
        public List<int> NoRoom { get; } = new();
    }

    /// <summary>
    /// Lays out view footprints on one or more sheets, in input order, left to right and then the
    /// next row down. Every view in a row sits on the row's bottom line, so view titles line up.
    /// <list type="bullet">
    /// <item><b>Grid</b>: the largest number of columns that fits at the minimum gap; each column
    /// is as wide as its widest view, so columns line up from row to row. The columns are spread
    /// to the full width (one column is centred).</item>
    /// <item><b>Rows</b>: rows fill until the next view would not fit at the minimum gap; each row
    /// is spread to the full width, a single view is centred, and a short last row follows
    /// <see cref="LastRowMode"/>.</item>
    /// </list>
    /// Rows that do not fit in the height go on the next sheet. Pure math, no Revit types.
    /// </summary>
    public static class SheetGridLayout
    {
        private const double Eps = 1e-9;

        /// <summary>
        /// Computes the layout of <paramref name="sizes"/> (footprint width, height) in
        /// <paramref name="area"/>. Items bigger than the area are reported, not placed.
        /// </summary>
        public static GridLayoutResult Compute(IReadOnlyList<(double Width, double Height)> sizes, LayoutRect area, GridLayoutOptions options)
        {
            options ??= new GridLayoutOptions();
            if (sizes == null || sizes.Count == 0)
                return new GridLayoutResult();

            if (!area.IsValid)
            {
                var none = new GridLayoutResult();
                none.TooBig.AddRange(Enumerable.Range(0, sizes.Count));
                return none;
            }

            double hGap = Clean(options.MinHorizontalGap);
            double vGap = Clean(options.MinVerticalGap);

            var fit = new List<int>();
            var tooBig = new List<int>();
            for (int i = 0; i < sizes.Count; i++)
            {
                var (w, h) = sizes[i];
                if (!(w > 0) || !(h > 0) || !double.IsFinite(w) || !double.IsFinite(h)
                    || w > area.Width + Eps || h > area.Height + Eps)
                    tooBig.Add(i);
                else
                    fit.Add(i);
            }

            // ── Rows ────────────────────────────────────────────────────
            var rows = new List<List<int>>();
            double[] columnWidths = null;
            int columns = 0;
            if (options.Mode == ArrangeMode.Grid)
            {
                columns = ChooseColumns(fit, sizes, area.Width, hGap, out columnWidths);
                for (int k = 0; k < fit.Count; k += columns)
                    rows.Add(fit.GetRange(k, Math.Min(columns, fit.Count - k)));
            }
            else
            {
                rows = FlowRows(fit, sizes, area.Width, hGap);
            }

            // ── Sheets: rows that do not fit in the height go to the next sheet ──
            var pages = new List<List<List<int>>>();
            var current = new List<List<int>>();
            double used = 0;
            foreach (var row in rows)
            {
                double h = RowHeight(row, sizes);
                double need = current.Count == 0 ? h : used + vGap + h;
                if (current.Count > 0 && need > area.Height + Eps)
                {
                    pages.Add(current);
                    current = new List<List<int>> { row };
                    used = h;
                }
                else
                {
                    current.Add(row);
                    used = need;
                }
            }
            if (current.Count > 0)
                pages.Add(current);

            var result = new GridLayoutResult { Columns = columns };
            result.TooBig.AddRange(tooBig);
            if (options.SinglePage && pages.Count > 1)
            {
                result.NoRoom.AddRange(pages.Skip(1).SelectMany(p => p.SelectMany(r => r)));
                pages = pages.Take(1).ToList();
            }

            // ── Positions ───────────────────────────────────────────────
            int totalRows = pages.Sum(p => p.Count);
            double? previousGap = null;
            for (int pi = 0; pi < pages.Count; pi++)
            {
                var pageRows = pages[pi];
                var heights = pageRows.Select(r => RowHeight(r, sizes)).ToList();
                double totalHeight = heights.Sum();

                double rowGap = vGap;
                double top = area.MaxY;
                if (options.Vertical == VerticalMode.Center)
                    top = area.MaxY - Math.Max(0, (area.Height - totalHeight - vGap * (pageRows.Count - 1)) / 2);
                else if (options.Vertical == VerticalMode.Spread && pageRows.Count > 1)
                    rowGap = Math.Max(vGap, (area.Height - totalHeight) / (pageRows.Count - 1));

                var slots = new List<GridSlot>();
                double? smallestGap = null;

                for (int ri = 0; ri < pageRows.Count; ri++)
                {
                    var row = pageRows[ri];
                    double bottom = top - heights[ri];
                    var xs = new double[row.Count];

                    if (options.Mode == ArrangeMode.Grid)
                    {
                        double sumColumns = columnWidths.Sum();
                        double gap = columns > 1 ? (area.Width - sumColumns) / (columns - 1) : 0;
                        double x = columns > 1 ? area.MinX : area.MinX + (area.Width - columnWidths[0]) / 2;
                        for (int c = 0; c < row.Count; c++)
                        {
                            xs[c] = x;
                            x += columnWidths[c] + gap;
                        }
                        if (row.Count > 1)
                            smallestGap = Min(smallestGap, gap);
                    }
                    else
                    {
                        double sumWidths = row.Sum(i => sizes[i].Width);
                        bool isLast = pi == pages.Count - 1 && ri == pageRows.Count - 1 && totalRows > 1;
                        double full = row.Count > 1 ? (area.Width - sumWidths) / (row.Count - 1) : 0;
                        double gap = full;
                        if (isLast && options.LastRow != LastRowMode.Justify && row.Count > 1)
                            gap = Math.Max(hGap, Math.Min(previousGap ?? full, full));

                        double x = area.MinX;
                        if (row.Count == 1 && !(isLast && options.LastRow == LastRowMode.Left))
                            x = area.MinX + (area.Width - sumWidths) / 2;
                        else if (isLast && options.LastRow == LastRowMode.Center)
                            x = area.MinX + Math.Max(0, (area.Width - sumWidths - gap * (row.Count - 1)) / 2);

                        for (int c = 0; c < row.Count; c++)
                        {
                            xs[c] = x;
                            x += sizes[row[c]].Width + gap;
                        }
                        if (row.Count > 1)
                        {
                            smallestGap = Min(smallestGap, gap);
                            if (!isLast)
                                previousGap = gap;
                        }
                    }

                    for (int c = 0; c < row.Count; c++)
                        slots.Add(new GridSlot { Index = row[c], Page = pi, Row = ri, Column = c, MinX = xs[c], MinY = bottom });

                    top = bottom - rowGap;
                }

                var page = new GridPage
                {
                    RowCount = pageRows.Count,
                    HorizontalGap = smallestGap,
                    VerticalGap = pageRows.Count > 1 ? rowGap : null
                };
                page.Slots.AddRange(slots);
                result.Pages.Add(page);
            }

            return result;
        }

        /// <summary>
        /// The most columns (item i goes in column i mod n) whose widest items fit side by side at
        /// the minimum gap. Never less than one.
        /// </summary>
        private static int ChooseColumns(List<int> fit, IReadOnlyList<(double Width, double Height)> sizes,
            double width, double hGap, out double[] columnWidths)
        {
            for (int n = fit.Count; n >= 1; n--)
            {
                var widths = new double[n];
                for (int k = 0; k < fit.Count; k++)
                    widths[k % n] = Math.Max(widths[k % n], sizes[fit[k]].Width);
                if (widths.Sum() + hGap * (n - 1) <= width + Eps)
                {
                    columnWidths = widths;
                    return n;
                }
            }
            columnWidths = new[] { fit.Count == 0 ? 0 : fit.Max(i => sizes[i].Width) };
            return 1;
        }

        /// <summary>Fills rows left to right; a view starts a new row when it would not fit at the minimum gap.</summary>
        private static List<List<int>> FlowRows(List<int> fit, IReadOnlyList<(double Width, double Height)> sizes, double width, double hGap)
        {
            var rows = new List<List<int>>();
            var current = new List<int>();
            double used = 0;
            foreach (int i in fit)
            {
                double w = sizes[i].Width;
                double need = current.Count == 0 ? w : used + hGap + w;
                if (current.Count > 0 && need > width + Eps)
                {
                    rows.Add(current);
                    current = new List<int> { i };
                    used = w;
                }
                else
                {
                    current.Add(i);
                    used = need;
                }
            }
            if (current.Count > 0)
                rows.Add(current);
            return rows;
        }

        private static double RowHeight(List<int> row, IReadOnlyList<(double Width, double Height)> sizes)
            => row.Max(i => sizes[i].Height);

        private static double Clean(double value) => double.IsFinite(value) && value > 0 ? value : 0;

        private static double? Min(double? a, double b) => a.HasValue ? Math.Min(a.Value, b) : b;
    }
}
