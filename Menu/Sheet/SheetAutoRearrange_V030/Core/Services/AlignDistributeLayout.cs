using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Services
{
    /// <summary>One view box handed to <see cref="AlignDistributeLayout"/>: its packed centre and its size (any one length unit, Y up).</summary>
    public readonly struct LayoutBox
    {
        /// <summary>Caller's id for the box; returned as the key of <see cref="AlignDistributeResult.Centers"/>.</summary>
        public int Key { get; }
        public double CenterX { get; }
        public double CenterY { get; }
        public double Width { get; }
        public double Height { get; }

        public LayoutBox(int key, double centerX, double centerY, double width, double height)
        {
            Key = key;
            CenterX = centerX;
            CenterY = centerY;
            Width = width;
            Height = height;
        }

        internal double Top => CenterY + Height / 2.0;
        internal double Bottom => CenterY - Height / 2.0;
        internal double Left => CenterX - Width / 2.0;
        internal double Right => CenterX + Width / 2.0;
    }

    /// <summary>Why <see cref="AlignDistributeLayout.Arrange"/> could not build the aligned layout.</summary>
    public enum AlignFailReason { None, WidthExceeded, HeightExceeded }

    /// <summary>Outcome of <see cref="AlignDistributeLayout.Arrange"/>.</summary>
    public sealed class AlignDistributeResult
    {
        /// <summary>True when the aligned layout fits the area; <see cref="Centers"/> then holds every box.</summary>
        public bool Applied { get; set; }

        /// <summary>New centre per box key. Empty when not applied.</summary>
        public Dictionary<int, (double X, double Y)> Centers { get; } = new();

        /// <summary>Rows in the aligned layout (0 when not applied).</summary>
        public int Rows { get; set; }

        /// <summary>Columns in the aligned layout: the table's column count, or the widest row / column count.</summary>
        public int Columns { get; set; }

        /// <summary>Which side ran out of room when not applied.</summary>
        public AlignFailReason FailReason { get; set; }

        /// <summary>Length the aligned layout needs on the failing side.</summary>
        public double Needed { get; set; }

        /// <summary>Length the usable area has on the failing side.</summary>
        public double Available { get; set; }
    }

    /// <summary>
    /// V029: rebuilds a packed layout so views line up on shared row lines and / or column
    /// lines, and spreads those rows / columns across the usable area.
    ///
    /// Rows only: rows are read from the packed positions, each row shares one line.
    /// Columns only: the mirror, by columns. Both: a table (a column is as wide as its
    /// widest view, a row as tall as its tallest view). Revit-free, so it is unit tested
    /// (Tests/SheetAutoRearrange.Tests). It never returns a layout that leaves the area:
    /// when the aligned layout does not fit, <see cref="AlignDistributeResult.Applied"/>
    /// is false and the caller keeps the packed layout.
    /// </summary>
    public static class AlignDistributeLayout
    {
        private const double Eps = 1e-6;

        /// <summary>
        /// Builds the aligned layout for <paramref name="boxes"/> inside the area
        /// (<paramref name="minX"/>..<paramref name="maxX"/>, <paramref name="minY"/>..<paramref name="maxY"/>).
        /// </summary>
        /// <param name="hGap">Smallest horizontal gap between views.</param>
        /// <param name="vGap">Smallest vertical gap between views.</param>
        /// <param name="rowTolerance">Centre-Y distance within which two views count as one row.</param>
        /// <param name="columnTolerance">Centre-X distance within which two views count as one column.</param>
        /// <param name="anchorH">Where a row / the block sits when it is not distributed horizontally.</param>
        /// <param name="anchorV">Where a column / the block sits when it is not distributed vertically.</param>
        public static AlignDistributeResult Arrange(
            IReadOnlyList<LayoutBox> boxes,
            double minX, double minY, double maxX, double maxY,
            AlignDistributeSettings settings,
            double hGap, double vGap,
            double rowTolerance, double columnTolerance,
            BlockAlignmentH anchorH, BlockAlignmentV anchorV)
        {
            var result = new AlignDistributeResult();
            if (boxes == null || boxes.Count == 0 || settings == null || !settings.IsActive)
                return result;

            double width = maxX - minX;
            double height = maxY - minY;
            hGap = Math.Max(0, hGap);
            vGap = Math.Max(0, vGap);

            int startH = anchorH == BlockAlignmentH.Left ? -1 : anchorH == BlockAlignmentH.Right ? 1 : 0;
            int startV = anchorV == BlockAlignmentV.Top ? -1 : anchorV == BlockAlignmentV.Bottom ? 1 : 0;

            bool rowsOn = settings.AlignRows != GridRowAlign.Off;
            bool colsOn = settings.AlignColumns != GridColumnAlign.Off;

            if (rowsOn && colsOn)
                ArrangeTable(boxes, result, minX, maxY, width, height, settings, hGap, vGap, rowTolerance, startH, startV);
            else if (rowsOn)
                ArrangeRows(boxes, result, minX, maxY, width, height, settings, hGap, vGap, rowTolerance, startH, startV);
            else
                ArrangeColumns(boxes, result, minX, maxY, width, height, settings, hGap, vGap, columnTolerance, startH, startV);

            if (!result.Applied)
                result.Centers.Clear();
            return result;
        }

        // ── Rows only ───────────────────────────────────────────────────
        private static void ArrangeRows(IReadOnlyList<LayoutBox> boxes, AlignDistributeResult result,
            double minX, double maxY, double width, double height, AlignDistributeSettings s,
            double hGap, double vGap, double rowTolerance, int startH, int startV)
        {
            var rows = new List<List<LayoutBox>>();
            foreach (var row in DetectRows(boxes, rowTolerance))
                rows.AddRange(Wrap(row, b => b.Width, width, hGap));

            var rowHeights = rows.Select(r => r.Max(b => b.Height)).ToList();
            var rowOffsets = PlaceTracks(rowHeights, height, vGap, s.DistributeV, startV);
            if (rowOffsets == null)
            {
                Fail(result, AlignFailReason.HeightExceeded, Span(rowHeights, vGap), height);
                return;
            }

            for (int r = 0; r < rows.Count; r++)
            {
                var widths = rows[r].Select(b => b.Width).ToList();
                var xOffsets = PlaceTracks(widths, width, hGap, s.DistributeH, startH);
                if (xOffsets == null)
                {
                    Fail(result, AlignFailReason.WidthExceeded, Span(widths, hGap), width);
                    return;
                }

                for (int i = 0; i < rows[r].Count; i++)
                {
                    var b = rows[r][i];
                    double left = minX + xOffsets[i];
                    double top = maxY - rowOffsets[r] - RowInset(s.AlignRows, rowHeights[r], b.Height);
                    result.Centers[b.Key] = (left + b.Width / 2.0, top - b.Height / 2.0);
                }
            }

            result.Applied = true;
            result.Rows = rows.Count;
            result.Columns = rows.Max(r => r.Count);
        }

        // ── Columns only ────────────────────────────────────────────────
        private static void ArrangeColumns(IReadOnlyList<LayoutBox> boxes, AlignDistributeResult result,
            double minX, double maxY, double width, double height, AlignDistributeSettings s,
            double hGap, double vGap, double columnTolerance, int startH, int startV)
        {
            var columns = new List<List<LayoutBox>>();
            foreach (var column in DetectColumns(boxes, columnTolerance))
                columns.AddRange(Wrap(column, b => b.Height, height, vGap));

            var columnWidths = columns.Select(c => c.Max(b => b.Width)).ToList();
            var columnOffsets = PlaceTracks(columnWidths, width, hGap, s.DistributeH, startH);
            if (columnOffsets == null)
            {
                Fail(result, AlignFailReason.WidthExceeded, Span(columnWidths, hGap), width);
                return;
            }

            for (int c = 0; c < columns.Count; c++)
            {
                var heights = columns[c].Select(b => b.Height).ToList();
                var yOffsets = PlaceTracks(heights, height, vGap, s.DistributeV, startV);
                if (yOffsets == null)
                {
                    Fail(result, AlignFailReason.HeightExceeded, Span(heights, vGap), height);
                    return;
                }

                for (int i = 0; i < columns[c].Count; i++)
                {
                    var b = columns[c][i];
                    double left = minX + columnOffsets[c] + ColumnInset(s.AlignColumns, columnWidths[c], b.Width);
                    double top = maxY - yOffsets[i];
                    result.Centers[b.Key] = (left + b.Width / 2.0, top - b.Height / 2.0);
                }
            }

            result.Applied = true;
            result.Columns = columns.Count;
            result.Rows = columns.Max(c => c.Count);
        }

        // ── Rows and columns: table ─────────────────────────────────────
        private static void ArrangeTable(IReadOnlyList<LayoutBox> boxes, AlignDistributeResult result,
            double minX, double maxY, double width, double height, AlignDistributeSettings s,
            double hGap, double vGap, double rowTolerance, int startH, int startV)
        {
            var detected = DetectRows(boxes, rowTolerance);
            var sequence = detected.SelectMany(r => r).ToList();
            int count = sequence.Count;
            int preferred = Math.Min(count, detected.Max(r => r.Count));

            // Column counts to try: the packed layout's own widest row first, then fewer
            // columns (narrower table), then more columns (shorter table).
            var candidates = new List<int>();
            for (int n = preferred; n >= 1; n--) candidates.Add(n);
            for (int n = preferred + 1; n <= count; n++) candidates.Add(n);

            AlignFailReason firstReason = AlignFailReason.None;
            double firstNeeded = 0, firstAvailable = 0;

            foreach (int n in candidates)
            {
                int rowCount = (count + n - 1) / n;
                var columnWidths = new double[n];
                var rowHeights = new double[rowCount];
                for (int i = 0; i < count; i++)
                {
                    columnWidths[i % n] = Math.Max(columnWidths[i % n], sequence[i].Width);
                    rowHeights[i / n] = Math.Max(rowHeights[i / n], sequence[i].Height);
                }

                var xOffsets = PlaceTracks(columnWidths, width, hGap, s.DistributeH, startH);
                var yOffsets = PlaceTracks(rowHeights, height, vGap, s.DistributeV, startV);
                if (xOffsets == null || yOffsets == null)
                {
                    if (firstReason == AlignFailReason.None)
                    {
                        firstReason = xOffsets == null ? AlignFailReason.WidthExceeded : AlignFailReason.HeightExceeded;
                        firstNeeded = xOffsets == null ? Span(columnWidths, hGap) : Span(rowHeights, vGap);
                        firstAvailable = xOffsets == null ? width : height;
                    }
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    var b = sequence[i];
                    int c = i % n, r = i / n;
                    double left = minX + xOffsets[c] + ColumnInset(s.AlignColumns, columnWidths[c], b.Width);
                    double top = maxY - yOffsets[r] - RowInset(s.AlignRows, rowHeights[r], b.Height);
                    result.Centers[b.Key] = (left + b.Width / 2.0, top - b.Height / 2.0);
                }

                result.Applied = true;
                result.Rows = rowCount;
                result.Columns = n;
                return;
            }

            Fail(result, firstReason, firstNeeded, firstAvailable);
        }

        // ── Shared helpers ──────────────────────────────────────────────

        /// <summary>
        /// Reads rows off the packed positions, top row first, each row left to right.
        /// A view joins the highest remaining view's row when its centre lies inside that
        /// view's height, or within <paramref name="tolerance"/> of its centre.
        /// </summary>
        internal static List<List<LayoutBox>> DetectRows(IReadOnlyList<LayoutBox> boxes, double tolerance)
        {
            var remaining = boxes.OrderByDescending(b => b.Top).ThenByDescending(b => b.Height).ThenBy(b => b.CenterX).ToList();
            var rows = new List<List<LayoutBox>>();
            while (remaining.Count > 0)
            {
                var seed = remaining[0];
                var row = remaining
                    .Where(b => b.CenterY >= seed.Bottom - Eps || Math.Abs(b.CenterY - seed.CenterY) <= tolerance)
                    .ToList();
                remaining = remaining.Except(row).ToList();
                rows.Add(row.OrderBy(b => b.Left).ThenByDescending(b => b.Top).ToList());
            }
            return rows;
        }

        /// <summary>The column mirror of <see cref="DetectRows"/>: left column first, each column top to bottom.</summary>
        internal static List<List<LayoutBox>> DetectColumns(IReadOnlyList<LayoutBox> boxes, double tolerance)
        {
            var remaining = boxes.OrderBy(b => b.Left).ThenByDescending(b => b.Width).ThenByDescending(b => b.CenterY).ToList();
            var columns = new List<List<LayoutBox>>();
            while (remaining.Count > 0)
            {
                var seed = remaining[0];
                var column = remaining
                    .Where(b => b.CenterX <= seed.Right + Eps || Math.Abs(b.CenterX - seed.CenterX) <= tolerance)
                    .ToList();
                remaining = remaining.Except(column).ToList();
                columns.Add(column.OrderByDescending(b => b.Top).ThenBy(b => b.Left).ToList());
            }
            return columns;
        }

        /// <summary>Splits one row / column into several when its views plus the gaps are longer than <paramref name="length"/>.</summary>
        private static List<List<LayoutBox>> Wrap(List<LayoutBox> line, Func<LayoutBox, double> size, double length, double gap)
        {
            var lines = new List<List<LayoutBox>>();
            var current = new List<LayoutBox>();
            double used = 0;
            foreach (var b in line)
            {
                double advance = current.Count > 0 ? size(b) + gap : size(b);
                if (current.Count > 0 && used + advance > length + Eps)
                {
                    lines.Add(current);
                    current = new List<LayoutBox>();
                    used = 0;
                    advance = size(b);
                }
                current.Add(b);
                used += advance;
            }
            if (current.Count > 0)
                lines.Add(current);
            return lines;
        }

        /// <summary>Total length of the tracks with the smallest allowed gap between them.</summary>
        private static double Span(IReadOnlyList<double> sizes, double gap) =>
            sizes.Sum() + Math.Max(0, sizes.Count - 1) * gap;

        /// <summary>
        /// Positions tracks (row heights or column widths) along a span of <paramref name="length"/>.
        /// Returns each track's start offset from the span's start (left edge, or top edge),
        /// or null when the tracks with <paramref name="minGap"/> between them do not fit.
        /// </summary>
        /// <param name="start">Where the block sits when it is not spread: -1 start, 0 centre, 1 end.</param>
        internal static double[]? PlaceTracks(IReadOnlyList<double> sizes, double length, double minGap, DistributeMode mode, int start)
        {
            int n = sizes.Count;
            double total = sizes.Sum();
            double block = total + Math.Max(0, n - 1) * minGap;
            if (n == 0 || block > length + Eps)
                return null;

            double gap = minGap;
            double first = start < 0 ? 0 : start > 0 ? length - block : (length - block) / 2.0;

            if (mode == DistributeMode.EdgeToEdge && n > 1)
            {
                gap = (length - total) / (n - 1);
                first = 0;
            }
            else if (mode == DistributeMode.EqualSpacing)
            {
                double even = (length - total) / (n + 1);
                if (even >= minGap - Eps)
                {
                    gap = even;
                    first = even;
                }
                else
                {
                    // Too tight for equal gaps all round: keep the minimum gap between views and centre the block.
                    first = (length - block) / 2.0;
                }
            }

            var offsets = new double[n];
            double cursor = Math.Max(0, first);
            for (int i = 0; i < n; i++)
            {
                offsets[i] = cursor;
                cursor += sizes[i] + gap;
            }
            return offsets;
        }

        /// <summary>Distance from a row's top line down to a view's top.</summary>
        private static double RowInset(GridRowAlign align, double rowHeight, double boxHeight) => align switch
        {
            GridRowAlign.Bottom => rowHeight - boxHeight,
            GridRowAlign.Center => (rowHeight - boxHeight) / 2.0,
            _ => 0
        };

        /// <summary>Distance from a column's left line to a view's left.</summary>
        private static double ColumnInset(GridColumnAlign align, double columnWidth, double boxWidth) => align switch
        {
            GridColumnAlign.Right => columnWidth - boxWidth,
            GridColumnAlign.Center => (columnWidth - boxWidth) / 2.0,
            _ => 0
        };

        private static void Fail(AlignDistributeResult result, AlignFailReason reason, double needed, double available)
        {
            result.Applied = false;
            result.FailReason = reason;
            result.Needed = needed;
            result.Available = available;
        }
    }
}
