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

        /// <summary>How views of differing height sit inside their row.</summary>
        public RowAlignMode RowAlign { get; init; } = RowAlignMode.Bottom;
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
