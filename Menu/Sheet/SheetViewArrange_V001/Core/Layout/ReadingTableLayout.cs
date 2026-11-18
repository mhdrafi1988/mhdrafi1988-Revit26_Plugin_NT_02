using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Layout
{
    /// <summary>How the last row is laid out when the layout has two or more rows.</summary>
    public enum LastRowMode
    {
        /// <summary>Same gap as the row above, starting at the left edge (like justified text).</summary>
        PackLeft,

        /// <summary>Stretched to the full width like every other row.</summary>
        Justify,

        /// <summary>Same gap as the row above, centred in the width.</summary>
        Center
    }

    /// <summary>Where each row of views sits across the usable width.</summary>
    public enum HorizontalLayoutMode
    {
        /// <summary>Full rows spread to the whole width; a short last row follows <see cref="LastRowMode"/>.</summary>
        Auto,

        /// <summary>Every row spread to the whole width, the last row included.</summary>
        Justify,

        /// <summary>Every row packed at the minimum gap against the left edge.</summary>
        Left,

        /// <summary>Every row packed at the minimum gap against the right edge.</summary>
        Right,

        /// <summary>Every row packed at the minimum gap and centred in the width.</summary>
        Center
    }

    /// <summary>Where the block of rows sits down the usable height.</summary>
    public enum VerticalLayoutMode
    {
        /// <summary>Rows spread to the whole height when they fit, otherwise packed to the top.</summary>
        Auto,

        /// <summary>Rows spread to the whole height (falls back to the top when they do not fit).</summary>
        Spread,

        /// <summary>Rows packed at the minimum gap against the top edge.</summary>
        Top,

        /// <summary>Rows packed at the minimum gap against the bottom edge.</summary>
        Bottom,

        /// <summary>Rows packed at the minimum gap and centred in the height.</summary>
        Center
    }

    /// <summary>How views of differing height sit inside their own row.</summary>
    public enum RowAlignMode
    {
        /// <summary>Common bottom line, so the view titles under them line up.</summary>
        Bottom,

        /// <summary>Common top line.</summary>
        Top,

        /// <summary>Centred on the row's middle line.</summary>
        Center
    }

    /// <summary>Which way the reading order flows before it wraps.</summary>
    public enum FlowMode
    {
        /// <summary>Left → right, wrapping into rows top → bottom (the reading-table layout).</summary>
        Rows,

        /// <summary>Top → bottom, wrapping into columns left → right.</summary>
        Columns
    }

    /// <summary>How views of differing width sit inside their own column (column flow only).</summary>
    public enum ColumnAlignMode
    {
        /// <summary>Common left edge.</summary>
        Left,

        /// <summary>Common right edge.</summary>
        Right,

        /// <summary>Centred on the column's middle line.</summary>
        Center
    }

    /// <summary>
    /// Everything <see cref="ReadingTableLayout.Compute(IReadOnlyList{ValueTuple{double, double}}, LayoutRect, LayoutOptions)"/>
    /// needs beyond the sizes and the usable area. The defaults reproduce the original
    /// layout (<see cref="HorizontalLayoutMode.Auto"/> / <see cref="VerticalLayoutMode.Auto"/> /
    /// <see cref="RowAlignMode.Bottom"/>), so settings files written before the modes existed
    /// keep their look.
    /// </summary>
    public sealed class LayoutOptions
    {
        /// <summary>Minimum gap between views in a row (feet).</summary>
        public double MinHorizontalGap { get; init; }

        /// <summary>Minimum gap between rows (feet).</summary>
        public double MinVerticalGap { get; init; }

        /// <summary>How a short last row is placed. Only used by <see cref="HorizontalLayoutMode.Auto"/>.</summary>
        public LastRowMode LastRowMode { get; init; } = LastRowMode.PackLeft;

        /// <summary>Where each row sits across the width.</summary>
        public HorizontalLayoutMode Horizontal { get; init; } = HorizontalLayoutMode.Auto;

        /// <summary>Where the block of rows sits down the height.</summary>
        public VerticalLayoutMode Vertical { get; init; } = VerticalLayoutMode.Auto;

        /// <summary>How views of differing height sit inside their row (row flow).</summary>
        public RowAlignMode RowAlign { get; init; } = RowAlignMode.Bottom;

        /// <summary>Which way the order flows before it wraps.</summary>
        public FlowMode Flow { get; init; } = FlowMode.Rows;

        /// <summary>How views of differing width sit inside their column (column flow).</summary>
        public ColumnAlignMode ColumnAlign { get; init; } = ColumnAlignMode.Left;
    }

    /// <summary>Axis-aligned rectangle in sheet space (feet).</summary>
    public readonly struct LayoutRect
    {
        /// <summary>Left edge.</summary>
        public double MinX { get; }

        /// <summary>Bottom edge.</summary>
        public double MinY { get; }

        /// <summary>Right edge.</summary>
        public double MaxX { get; }

        /// <summary>Top edge.</summary>
        public double MaxY { get; }

        /// <summary>Creates a rectangle from its four edges.</summary>
        public LayoutRect(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>MaxX − MinX.</summary>
        public double Width => MaxX - MinX;

        /// <summary>MaxY − MinY.</summary>
        public double Height => MaxY - MinY;

        /// <summary>True when the rectangle has a positive width and height.</summary>
        public bool IsValid => Width > 0 && Height > 0;
    }

    /// <summary>Where one item lands: its footprint's bottom-left corner plus its row/column.</summary>
    public sealed class LayoutSlot
    {
        /// <summary>Index of the item in the input list.</summary>
        public int Index { get; init; }

        /// <summary>0-based row, counted from the top.</summary>
        public int Row { get; init; }

        /// <summary>0-based position within its row, counted from the left.</summary>
        public int Column { get; init; }

        /// <summary>New left edge of the item's footprint (feet).</summary>
        public double MinX { get; init; }

        /// <summary>New bottom edge of the item's footprint (feet).</summary>
        public double MinY { get; init; }

        /// <summary>False when the item lands (partly) outside the usable area.</summary>
        public bool Fits { get; init; }
    }

    /// <summary>Output of <see cref="ReadingTableLayout.Compute(IReadOnlyList{ValueTuple{double, double}}, LayoutRect, LayoutOptions)"/>.</summary>
    public sealed class LayoutResult
    {
        /// <summary>One slot per input item, in input order.</summary>
        public List<LayoutSlot> Slots { get; } = new();

        /// <summary>Number of rows.</summary>
        public int RowCount { get; set; }

        /// <summary>True when every item lies inside the usable area.</summary>
        public bool AllFit { get; set; } = true;

        /// <summary>Why the layout does not fit, or null when it does.</summary>
        public string Problem { get; set; }
    }

    /// <summary>
    /// Lays items out like text on a page: left → right, wrapping into rows top → bottom. The
    /// input order is the reading order and is never changed — only the geometry is.
    /// <list type="bullet">
    /// <item>Rows are filled until the next item would not fit at the minimum horizontal gap.</item>
    /// <item><see cref="LayoutOptions.Horizontal"/> places each row across the width: spread to the
    /// full width, or packed at the minimum gap against the left edge, the right edge or the centre.</item>
    /// <item><see cref="LayoutOptions.Vertical"/> places the block of rows down the height: spread to
    /// the full height, or packed at the minimum gap against the top edge, the bottom edge or the centre.</item>
    /// <item><see cref="LayoutOptions.RowAlign"/> places views of differing height inside their row —
    /// a common bottom line by default, so the view titles line up.</item>
    /// <item><see cref="FlowMode.Columns"/> wraps on the other axis instead: the order runs top → bottom
    /// down a column and then across to the next column. The two placement modes swap roles — the
    /// columns are placed across the width and each column's views down the height — and
    /// <see cref="LayoutOptions.ColumnAlign"/> places views narrower than their column.</item>
    /// </list>
    /// Pure math, no Revit types — so it can be tested outside Revit.
    /// </summary>
    public static class ReadingTableLayout
    {
        private const double Eps = 1e-9;

        /// <summary>
        /// Computes the original layout (rows spread to the full width and height, views on a common
        /// bottom line). Kept so existing callers keep working; prefer the
        /// <see cref="LayoutOptions"/> overload.
        /// </summary>
        public static LayoutResult Compute(
            IReadOnlyList<(double Width, double Height)> sizes,
            LayoutRect area,
            double minHGap,
            double minVGap,
            LastRowMode lastRowMode)
            => Compute(sizes, area, new LayoutOptions
            {
                MinHorizontalGap = minHGap,
                MinVerticalGap = minVGap,
                LastRowMode = lastRowMode
            });

        /// <summary>
        /// Computes the layout. <paramref name="sizes"/> are footprint (width, height) pairs in
        /// reading order; all lengths in feet.
        /// </summary>
        public static LayoutResult Compute(
            IReadOnlyList<(double Width, double Height)> sizes,
            LayoutRect area,
            LayoutOptions options)
        {
            var result = new LayoutResult();
            if (sizes == null || sizes.Count == 0)
                return result;

            options ??= new LayoutOptions();

            if (!area.IsValid)
            {
                result.AllFit = false;
                result.Problem = "The margins leave no usable area on the sheet.";
                return result;
            }

            double minHGap = Math.Max(0, options.MinHorizontalGap);
            double minVGap = Math.Max(0, options.MinVerticalGap);

            if (options.Flow == FlowMode.Columns)
                return ComputeColumnFlow(sizes, area, minHGap, minVGap, options);

            var rows = BuildRows(sizes, area.Width, minHGap);
            result.RowCount = rows.Count;

            var xs = PlaceAcross(sizes, rows, area, minHGap, options);
            var (rowTops, rowHeights, heightFits, totalRowHeight) = PlaceDown(sizes, rows, area, minVGap, options);

            for (int r = 0; r < rows.Count; r++)
            {
                double rowTop = rowTops[r];
                double rowBottom = rowTop - rowHeights[r];
                for (int c = 0; c < rows[r].Count; c++)
                {
                    int i = rows[r][c];
                    var (w, h) = sizes[i];
                    double y = options.RowAlign switch
                    {
                        RowAlignMode.Top => rowTop - h,
                        RowAlignMode.Center => rowBottom + (rowHeights[r] - h) / 2.0,
                        _ => rowBottom
                    };

                    bool fits = xs[i] >= area.MinX - Eps
                                && xs[i] + w <= area.MaxX + Eps
                                && y >= area.MinY - Eps
                                && y + h <= area.MaxY + Eps;

                    result.Slots.Add(new LayoutSlot
                    {
                        Index = i,
                        Row = r,
                        Column = c,
                        MinX = xs[i],
                        MinY = y,
                        Fits = fits
                    });
                }
            }

            result.Slots.Sort((a, b) => a.Index.CompareTo(b.Index));
            result.AllFit = result.Slots.All(s => s.Fits);

            if (!result.AllFit)
                result.Problem = DescribeProblem(sizes, area, minVGap, rows.Count, totalRowHeight, heightFits);

            return result;
        }

        /// <summary>x of every item, row by row, per <see cref="LayoutOptions.Horizontal"/>.</summary>
        private static double[] PlaceAcross(
            IReadOnlyList<(double Width, double Height)> sizes,
            List<List<int>> rows,
            LayoutRect area,
            double minHGap,
            LayoutOptions options)
        {
            var xs = new double[sizes.Count];
            double previousGap = minHGap;

            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                double sumW = row.Sum(i => sizes[i].Width);
                double spreadGap = row.Count > 1 ? Math.Max(minHGap, (area.Width - sumW) / (row.Count - 1)) : minHGap;

                // Auto keeps the original behaviour: full rows are spread to the width and only a
                // short last row follows LastRowMode.
                bool isShortLastRow = r == rows.Count - 1 && rows.Count > 1;
                var mode = options.Horizontal;
                if (mode == HorizontalLayoutMode.Auto)
                {
                    mode = !isShortLastRow
                        ? HorizontalLayoutMode.Justify
                        : options.LastRowMode switch
                        {
                            LastRowMode.Justify => HorizontalLayoutMode.Justify,
                            LastRowMode.Center => HorizontalLayoutMode.Center,
                            _ => HorizontalLayoutMode.Left
                        };
                }

                double gap = mode == HorizontalLayoutMode.Justify ? spreadGap : minHGap;

                // A short last row that is not spread reuses the row above's gap, so it reads as
                // part of the same table — never wider than the width allows, never below the minimum.
                if (options.Horizontal == HorizontalLayoutMode.Auto && isShortLastRow
                    && mode != HorizontalLayoutMode.Justify && row.Count > 1)
                    gap = Math.Max(minHGap, Math.Min(previousGap, spreadGap));

                double rowWidth = sumW + gap * (row.Count - 1);
                double x = mode switch
                {
                    HorizontalLayoutMode.Right => area.MaxX - rowWidth,
                    HorizontalLayoutMode.Center => area.MinX + (area.Width - rowWidth) / 2.0,
                    _ => area.MinX
                };

                // An over-wide row starts at the left edge whatever the mode, so the overflow is
                // visible on one side only and the "does not fit" report stays readable.
                if (rowWidth > area.Width + Eps)
                    x = area.MinX;

                foreach (int i in row)
                {
                    xs[i] = x;
                    x += sizes[i].Width + gap;
                }

                if (row.Count > 1)
                    previousGap = gap;
            }

            return xs;
        }

        /// <summary>Top edge of every row, per <see cref="LayoutOptions.Vertical"/>.</summary>
        private static (double[] RowTops, List<double> RowHeights, bool HeightFits, double TotalRowHeight) PlaceDown(
            IReadOnlyList<(double Width, double Height)> sizes,
            List<List<int>> rows,
            LayoutRect area,
            double minVGap,
            LayoutOptions options)
        {
            var rowHeights = rows.Select(row => row.Max(i => sizes[i].Height)).ToList();
            double totalRowHeight = rowHeights.Sum();
            double packedHeight = totalRowHeight + minVGap * (rows.Count - 1);
            bool heightFits = packedHeight <= area.Height + Eps;

            var mode = options.Vertical;
            if (mode == VerticalLayoutMode.Auto)
                mode = VerticalLayoutMode.Spread;

            // Spreading needs the rows to fit; otherwise they are packed from the top so the
            // overflow runs off the bottom, as it always has.
            if (mode == VerticalLayoutMode.Spread && (!heightFits || rows.Count < 2))
                mode = VerticalLayoutMode.Top;

            double vGap = mode == VerticalLayoutMode.Spread
                ? (area.Height - totalRowHeight) / (rows.Count - 1)
                : minVGap;

            double top = mode switch
            {
                VerticalLayoutMode.Bottom => area.MinY + packedHeight,
                VerticalLayoutMode.Center => area.MinY + packedHeight + (area.Height - packedHeight) / 2.0,
                _ => area.MaxY
            };

            // An over-tall block starts at the top edge whatever the mode, for the same reason as
            // an over-wide row starts at the left.
            if (!heightFits)
                top = area.MaxY;

            var rowTops = new double[rows.Count];
            for (int r = 0; r < rows.Count; r++)
            {
                rowTops[r] = top;
                top -= rowHeights[r] + vGap;
            }

            return (rowTops, rowHeights, heightFits, totalRowHeight);
        }

        /// <summary>
        /// Lays the items out down columns instead of across rows: the order runs top → bottom down a
        /// column, then across to the next. A slot's <see cref="LayoutSlot.Row"/> is its column index
        /// and its <see cref="LayoutSlot.Column"/> its place down that column, so the rest of the tool
        /// reports "band, then position" either way.
        /// </summary>
        private static LayoutResult ComputeColumnFlow(
            IReadOnlyList<(double Width, double Height)> sizes,
            LayoutRect area,
            double minHGap,
            double minVGap,
            LayoutOptions options)
        {
            var result = new LayoutResult();
            var columns = BuildColumns(sizes, area.Height, minVGap);
            result.RowCount = columns.Count;

            var columnWidths = columns.Select(col => col.Max(i => sizes[i].Width)).ToList();
            var (columnXs, _) = Distribute(columnWidths, area.MinX, area.Width, minHGap, AnchorFor(options.Horizontal));

            double totalWidth = columnWidths.Sum();
            bool widthFits = totalWidth + minHGap * (columns.Count - 1) <= area.Width + Eps;
            double previousGap = minVGap;

            for (int c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                var heights = column.Select(i => sizes[i].Height).ToList();

                // Auto spreads every full column over the height and lets only the short last column
                // follow LastRowMode — the same rule row flow applies to its last row.
                bool isShortLastColumn = c == columns.Count - 1 && columns.Count > 1;
                var anchor = options.Vertical == VerticalLayoutMode.Auto
                    ? (isShortLastColumn ? AnchorForLast(options.LastRowMode) : Anchor.Spread)
                    : AnchorFor(options.Vertical);

                // A short last column reuses the column before it's gap, so it reads as part of the
                // same table rather than drifting to its own rhythm.
                double gapOverride = options.Vertical == VerticalLayoutMode.Auto && isShortLastColumn
                                     && anchor != Anchor.Spread && column.Count > 1
                    ? previousGap
                    : 0;

                var (offsets, usedGap) = Distribute(heights, 0, area.Height, Math.Max(minVGap, gapOverride), anchor);
                if (column.Count > 1)
                    previousGap = usedGap;

                for (int k = 0; k < column.Count; k++)
                {
                    int i = column[k];
                    var (w, h) = sizes[i];
                    double x = options.ColumnAlign switch
                    {
                        ColumnAlignMode.Right => columnXs[c] + columnWidths[c] - w,
                        ColumnAlignMode.Center => columnXs[c] + (columnWidths[c] - w) / 2.0,
                        _ => columnXs[c]
                    };
                    double y = area.MaxY - (offsets[k] + h);

                    result.Slots.Add(new LayoutSlot
                    {
                        Index = i,
                        Row = c,
                        Column = k,
                        MinX = x,
                        MinY = y,
                        Fits = x >= area.MinX - Eps && x + w <= area.MaxX + Eps
                               && y >= area.MinY - Eps && y + h <= area.MaxY + Eps
                    });
                }
            }

            result.Slots.Sort((a, b) => a.Index.CompareTo(b.Index));
            result.AllFit = result.Slots.All(s => s.Fits);

            if (!result.AllFit)
                result.Problem = DescribeColumnProblem(sizes, area, minHGap, columns.Count, totalWidth, widthFits);

            return result;
        }

        /// <summary>Where a block of items sits in the span it is distributed over.</summary>
        private enum Anchor
        {
            /// <summary>Spread with equal gaps to fill the span.</summary>
            Spread,

            /// <summary>Packed at the minimum gap against the start of the span.</summary>
            Start,

            /// <summary>Packed at the minimum gap against the end of the span.</summary>
            End,

            /// <summary>Packed at the minimum gap and centred in the span.</summary>
            Center
        }

        private static Anchor AnchorFor(HorizontalLayoutMode mode) => mode switch
        {
            HorizontalLayoutMode.Left => Anchor.Start,
            HorizontalLayoutMode.Right => Anchor.End,
            HorizontalLayoutMode.Center => Anchor.Center,
            _ => Anchor.Spread // Auto and Justify both spread the columns over the width
        };

        private static Anchor AnchorFor(VerticalLayoutMode mode) => mode switch
        {
            VerticalLayoutMode.Top => Anchor.Start,
            VerticalLayoutMode.Bottom => Anchor.End,
            VerticalLayoutMode.Center => Anchor.Center,
            _ => Anchor.Spread // Auto and Spread both spread the views over the height
        };

        private static Anchor AnchorForLast(LastRowMode mode) => mode switch
        {
            LastRowMode.Justify => Anchor.Spread,
            LastRowMode.Center => Anchor.Center,
            _ => Anchor.Start
        };

        /// <summary>
        /// Places items one after another along one axis: either spread with equal gaps to fill
        /// <paramref name="span"/>, or packed at <paramref name="minGap"/> against its start, its end
        /// or its middle. Returns each item's offset from <paramref name="start"/> and the gap used. A
        /// block longer than the span always starts at <paramref name="start"/>, so the overflow runs
        /// off one end only.
        /// </summary>
        private static (double[] Offsets, double Gap) Distribute(
            IReadOnlyList<double> lengths, double start, double span, double minGap, Anchor anchor)
        {
            int n = lengths.Count;
            var offsets = new double[n];
            if (n == 0)
                return (offsets, minGap);

            double total = lengths.Sum();
            double gap = anchor == Anchor.Spread && n > 1
                ? Math.Max(minGap, (span - total) / (n - 1))
                : minGap;

            double blockLength = total + gap * (n - 1);
            double pos = anchor switch
            {
                Anchor.End => start + span - blockLength,
                Anchor.Center => start + (span - blockLength) / 2.0,
                _ => start
            };
            if (blockLength > span + Eps)
                pos = start;

            for (int k = 0; k < n; k++)
            {
                offsets[k] = pos;
                pos += lengths[k] + gap;
            }

            return (offsets, gap);
        }

        /// <summary>Flow-fills columns: an item starts a new column when it would exceed the height.</summary>
        private static List<List<int>> BuildColumns(IReadOnlyList<(double Width, double Height)> sizes, double height, double minVGap)
        {
            var columns = new List<List<int>>();
            var current = new List<int>();
            double used = 0;

            for (int i = 0; i < sizes.Count; i++)
            {
                double h = sizes[i].Height;
                double needed = current.Count == 0 ? h : used + minVGap + h;
                if (current.Count > 0 && needed > height + Eps)
                {
                    columns.Add(current);
                    current = new List<int>();
                    needed = h;
                }
                current.Add(i);
                used = needed;
            }

            if (current.Count > 0)
                columns.Add(current);
            return columns;
        }

        private static string DescribeColumnProblem(
            IReadOnlyList<(double Width, double Height)> sizes,
            LayoutRect area,
            double minHGap,
            int columnCount,
            double totalWidth,
            bool widthFits)
        {
            const double FeetToMm = 304.8;
            var problems = new List<string>();

            int tooTall = sizes.Count(s => s.Height > area.Height + Eps);
            if (tooTall > 0)
                problems.Add($"{tooTall} view(s) are taller than the usable area");

            double shortBy = totalWidth + minHGap * (columnCount - 1) - area.Width;
            if (!widthFits && shortBy > Eps)
                problems.Add($"{columnCount} column(s) need {shortBy * FeetToMm:F0} mm more width than the usable area");

            return problems.Count == 0
                ? "Some views do not fit in the usable area."
                : string.Join("; ", problems) + ".";
        }

        /// <summary>Flow-fills rows: an item starts a new row when it would exceed the width.</summary>
        private static List<List<int>> BuildRows(IReadOnlyList<(double Width, double Height)> sizes, double width, double minHGap)
        {
            var rows = new List<List<int>>();
            var current = new List<int>();
            double used = 0;

            for (int i = 0; i < sizes.Count; i++)
            {
                double w = sizes[i].Width;
                double needed = current.Count == 0 ? w : used + minHGap + w;
                if (current.Count > 0 && needed > width + Eps)
                {
                    rows.Add(current);
                    current = new List<int>();
                    needed = w;
                }
                current.Add(i);
                used = needed;
            }

            if (current.Count > 0)
                rows.Add(current);
            return rows;
        }

        private static string DescribeProblem(
            IReadOnlyList<(double Width, double Height)> sizes,
            LayoutRect area,
            double minVGap,
            int rowCount,
            double totalRowHeight,
            bool heightFits)
        {
            const double FeetToMm = 304.8;
            var problems = new List<string>();

            int tooWide = sizes.Count(s => s.Width > area.Width + Eps);
            if (tooWide > 0)
                problems.Add($"{tooWide} view(s) are wider than the usable area");

            double shortBy = totalRowHeight + minVGap * (rowCount - 1) - area.Height;
            if (!heightFits && shortBy > Eps)
                problems.Add($"{rowCount} row(s) need {shortBy * FeetToMm:F0} mm more height than the usable area");

            return problems.Count == 0
                ? "Some views do not fit in the usable area."
                : string.Join("; ", problems) + ".";
        }
    }
}
