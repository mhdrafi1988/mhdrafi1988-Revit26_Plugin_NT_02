using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout
{
    /// <summary>Everything <see cref="AlignEngine"/> needs, with every length already in sheet feet.</summary>
    public sealed class AlignEngineOptions
    {
        /// <summary>The area views may occupy: the title block inset by the margins.</summary>
        public LayoutRect Usable { get; init; }

        /// <summary>How far apart two views' edges may be and still count as one row.</summary>
        public double RowTolerance { get; init; }

        /// <summary>How far apart two views' edges may be and still count as one column.</summary>
        public double ColumnTolerance { get; init; }

        /// <summary>The shared horizontal line each row snaps to.</summary>
        public RowAlign AlignRows { get; init; } = RowAlign.Bottom;

        /// <summary>The shared vertical line each column snaps to.</summary>
        public ColumnAlign AlignColumns { get; init; } = ColumnAlign.Left;

        /// <summary>What happens to the gaps across a row.</summary>
        public DistributeMode DistributeHorizontal { get; init; } = DistributeMode.Off;

        /// <summary>What happens to the gaps between rows.</summary>
        public DistributeMode DistributeVertical { get; init; } = DistributeMode.Off;

        /// <summary>Smallest gap between two views side by side.</summary>
        public double MinGapHorizontal { get; init; }

        /// <summary>Smallest gap between two rows.</summary>
        public double MinGapVertical { get; init; }

        /// <summary>A view already this close to its line is left untouched.</summary>
        public double SnapTolerance { get; init; }

        /// <summary>A view is never moved further than this. Zero or less turns the limit off.</summary>
        public double MaxMove { get; init; }

        /// <summary>True to report a view over <see cref="MaxMove"/> and leave it; false to move it anyway.</summary>
        public bool ReportOverLimit { get; init; } = true;

        /// <summary>Leave a view where it is when its aligned position would cross the margins.</summary>
        public bool KeepInsideMargins { get; init; } = true;

        /// <summary>Refuse the whole plan if any two elements would end up overlapping.</summary>
        public bool StopOnOverlap { get; init; } = true;
    }

    /// <summary>What the engine decided for one box.</summary>
    public sealed class BoxPlacement
    {
        /// <summary>The <see cref="TrackBox.Key"/> this is for.</summary>
        public int Key { get; init; }

        /// <summary>Where the box ends up. Equal to its current footprint whenever it is not moved.</summary>
        public LayoutRect Target { get; init; }

        /// <summary>1-based row on the sheet, counted from the top.</summary>
        public int Row { get; init; }

        /// <summary>1-based column on the sheet, counted from the left.</summary>
        public int Column { get; init; }

        /// <summary>What the engine does with it.</summary>
        public AlignStatus Status { get; init; }

        /// <summary>Short explanation, or null.</summary>
        public string Note { get; init; }

        /// <summary>How far it would travel, in feet (the distance the limit is checked against).</summary>
        public double Distance { get; init; }
    }

    /// <summary>The outcome of one engine run.</summary>
    public sealed class AlignEngineResult
    {
        /// <summary>One entry per input box, in the input's order.</summary>
        public List<BoxPlacement> Placements { get; } = new();

        /// <summary>Rows read off the sheet.</summary>
        public int RowCount { get; set; }

        /// <summary>Columns read off the sheet.</summary>
        public int ColumnCount { get; set; }

        /// <summary>Things worth saying in the log that are not about one view.</summary>
        public List<string> Notes { get; } = new();

        /// <summary>When two elements would end up overlapping, the keys of the first such pair; otherwise null.</summary>
        public (int A, int B)? Overlap { get; set; }
    }

    /// <summary>
    /// The whole layout decision, with no Revit types in sight so it can be unit tested
    /// (<c>Tests/SheetAlignDistribute.Tests</c>).
    ///
    /// The shape of a run: read the rows and columns the views are already in, give each row one
    /// shared horizontal line and each column one shared vertical line, optionally re-space the
    /// gaps, then decide per view whether the resulting move is worth making — too small to bother
    /// with, too large to be an alignment, or outside the margins.
    ///
    /// Rows only ever change a view's Y and columns only ever change its X, so the two passes never
    /// fight. Distribute is the exception and is documented where it happens.
    /// </summary>
    public static class AlignEngine
    {
        private const double Eps = 1e-9;

        /// <summary>
        /// Plans <paramref name="boxes"/>. <paramref name="frozen"/> are elements that will not move
        /// (unticked, pinned, owned by someone else) but still take up room, so the overlap check
        /// sees them.
        /// </summary>
        public static AlignEngineResult Plan(IReadOnlyList<TrackBox> boxes, IReadOnlyList<TrackBox> frozen,
            AlignEngineOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var result = new AlignEngineResult();
            if (boxes == null || boxes.Count == 0) return result;

            var rows = TrackDetection.DetectRows(boxes, options.RowTolerance);
            var columns = TrackDetection.DetectColumns(boxes, options.ColumnTolerance);
            result.RowCount = rows.Count;
            result.ColumnCount = columns.Count;

            var rowOf = IndexMap(rows);
            var columnOf = IndexMap(columns);

            // Each box's planned bottom-left corner, starting from where it is now.
            var lowX = boxes.Select(b => b.Rect.MinX).ToArray();
            var lowY = boxes.Select(b => b.Rect.MinY).ToArray();

            AlignRowsPass(boxes, rows, options, lowY, result.Notes);
            AlignColumnsPass(boxes, columns, options, lowX, result.Notes);
            DistributeRowsPass(boxes, rows, options, lowY, result.Notes);
            DistributeAcrossRowsPass(boxes, rows, options, lowX, result.Notes);

            Decide(boxes, rows, columns, rowOf, columnOf, options, lowX, lowY, result);
            CheckOverlaps(boxes, frozen, options, result);
            return result;
        }

        // ── Alignment ───────────────────────────────────────────────────────

        private static void AlignRowsPass(IReadOnlyList<TrackBox> boxes, List<List<int>> rows,
            AlignEngineOptions options, double[] lowY, List<string> notes)
        {
            if (options.AlignRows == RowAlign.Off) return;

            for (int r = 0; r < rows.Count; r++)
            {
                var members = rows[r];
                if (members.Count < 2) continue; // nothing to share a line with

                var edges = members.Select(i => boxes[i].Rect.MinY).ToList();
                var sizes = members.Select(i => boxes[i].Rect.Height).ToList();

                EdgeChoice choice;
                double line;
                bool ok;
                if (options.AlignRows == RowAlign.LeastMovement)
                {
                    ok = AxisAlign.LeastMovementEdge(edges, sizes, options.Usable.MinY, options.Usable.MaxY, out choice, out line);
                }
                else
                {
                    choice = EdgeFor(options.AlignRows);
                    ok = AxisAlign.SharedLine(edges, sizes, choice, options.Usable.MinY, options.Usable.MaxY, out line);
                }

                if (!ok)
                {
                    notes.Add($"Row {r + 1} is taller than the usable height, so it was left as it is.");
                    continue;
                }

                for (int i = 0; i < members.Count; i++)
                    lowY[members[i]] = AxisAlign.LowEdgeFor(line, sizes[i], choice);
            }
        }

        private static void AlignColumnsPass(IReadOnlyList<TrackBox> boxes, List<List<int>> columns,
            AlignEngineOptions options, double[] lowX, List<string> notes)
        {
            if (options.AlignColumns == ColumnAlign.Off) return;

            for (int c = 0; c < columns.Count; c++)
            {
                var members = columns[c];
                if (members.Count < 2) continue;

                var edges = members.Select(i => boxes[i].Rect.MinX).ToList();
                var sizes = members.Select(i => boxes[i].Rect.Width).ToList();

                EdgeChoice choice;
                double line;
                bool ok;
                if (options.AlignColumns == ColumnAlign.LeastMovement)
                {
                    ok = AxisAlign.LeastMovementEdge(edges, sizes, options.Usable.MinX, options.Usable.MaxX, out choice, out line);
                }
                else
                {
                    choice = EdgeFor(options.AlignColumns);
                    ok = AxisAlign.SharedLine(edges, sizes, choice, options.Usable.MinX, options.Usable.MaxX, out line);
                }

                if (!ok)
                {
                    notes.Add($"Column {c + 1} is wider than the usable width, so it was left as it is.");
                    continue;
                }

                for (int i = 0; i < members.Count; i++)
                    lowX[members[i]] = AxisAlign.LowEdgeFor(line, sizes[i], choice);
            }
        }

        // ── Distribute ──────────────────────────────────────────────────────

        /// <summary>
        /// Spreads the rows down the sheet. Each row moves as a block, so the row alignment just
        /// made still holds; only the space between rows changes.
        /// </summary>
        private static void DistributeRowsPass(IReadOnlyList<TrackBox> boxes, List<List<int>> rows,
            AlignEngineOptions options, double[] lowY, List<string> notes)
        {
            if (options.DistributeVertical == DistributeMode.Off || rows.Count < 2) return;

            // DetectRows gives the top row first; Spread lays out along a rising axis, so work bottom-up.
            var bottomUp = Enumerable.Range(0, rows.Count).Reverse().ToList();
            var rowLow = bottomUp.Select(r => rows[r].Min(i => lowY[i])).ToList();
            var rowHigh = bottomUp.Select(r => rows[r].Max(i => lowY[i] + boxes[i].Rect.Height)).ToList();
            var sizes = rowLow.Select((low, k) => rowHigh[k] - low).ToList();

            var gaps = new List<double>();
            for (int k = 0; k + 1 < bottomUp.Count; k++)
            {
                double padAbove = rows[bottomUp[k]].Max(i => boxes[i].PadTop);
                double padBelow = rows[bottomUp[k + 1]].Max(i => boxes[i].PadBottom);
                gaps.Add(options.MinGapVertical + padAbove + padBelow);
            }

            double spanMin = options.DistributeVertical == DistributeMode.FitMargins ? options.Usable.MinY : rowLow[0];
            double spanMax = options.DistributeVertical == DistributeMode.FitMargins ? options.Usable.MaxY : rowHigh[^1];

            var placed = AxisAlign.Spread(sizes, gaps, spanMin, spanMax);
            if (placed == null)
            {
                notes.Add("The rows plus the minimum vertical gap are taller than the space available, so the rows were left where they are.");
                return;
            }

            for (int k = 0; k < bottomUp.Count; k++)
            {
                double shift = placed[k] - rowLow[k];
                if (Math.Abs(shift) <= Eps) continue;
                foreach (int i in rows[bottomUp[k]]) lowY[i] += shift;
            }
        }

        /// <summary>
        /// Re-spaces each row across the sheet. This sets X, so where it runs it replaces the column
        /// alignment for that row — which is why Distribute defaults to Off.
        /// </summary>
        private static void DistributeAcrossRowsPass(IReadOnlyList<TrackBox> boxes, List<List<int>> rows,
            AlignEngineOptions options, double[] lowX, List<string> notes)
        {
            if (options.DistributeHorizontal == DistributeMode.Off) return;

            for (int r = 0; r < rows.Count; r++)
            {
                var ordered = rows[r].OrderBy(i => lowX[i]).ToList();
                if (ordered.Count < 2) continue;

                var sizes = ordered.Select(i => boxes[i].Rect.Width).ToList();
                var gaps = new List<double>();
                for (int k = 0; k + 1 < ordered.Count; k++)
                    gaps.Add(options.MinGapHorizontal + boxes[ordered[k]].PadRight + boxes[ordered[k + 1]].PadLeft);

                double spanMin = options.DistributeHorizontal == DistributeMode.FitMargins
                    ? options.Usable.MinX : lowX[ordered[0]];
                double spanMax = options.DistributeHorizontal == DistributeMode.FitMargins
                    ? options.Usable.MaxX : lowX[ordered[^1]] + boxes[ordered[^1]].Rect.Width;

                var placed = AxisAlign.Spread(sizes, gaps, spanMin, spanMax);
                if (placed == null)
                {
                    notes.Add($"Row {r + 1} needs more width than it has at the minimum gap, so its spacing was left as it is.");
                    continue;
                }

                for (int k = 0; k < ordered.Count; k++) lowX[ordered[k]] = placed[k];
            }
        }

        // ── Per-view decision ───────────────────────────────────────────────

        private static void Decide(IReadOnlyList<TrackBox> boxes, List<List<int>> rows, List<List<int>> columns,
            int[] rowOf, int[] columnOf, AlignEngineOptions options, double[] lowX, double[] lowY,
            AlignEngineResult result)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                LayoutRect current = boxes[i].Rect;
                LayoutRect target = current.MoveTo(lowX[i], lowY[i]);
                double dx = lowX[i] - current.MinX;
                double dy = lowY[i] - current.MinY;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                bool alone = rows[rowOf[i]].Count < 2 && columns[columnOf[i]].Count < 2;
                AlignStatus status;
                string note = null;

                if (distance <= options.SnapTolerance + Eps)
                {
                    target = current;
                    status = alone ? AlignStatus.NoPartner : AlignStatus.InPlace;
                    if (alone) note = "No other view shares its row or column, so there is no line to snap to.";
                }
                else if (options.MaxMove > 0 && distance > options.MaxMove + Eps && options.ReportOverLimit)
                {
                    target = current;
                    status = AlignStatus.OverLimit;
                    note = $"Needs {Units.ForDisplay(Units.FeetToMm(distance))} mm, over the "
                         + $"{Units.ForDisplay(Units.FeetToMm(options.MaxMove))} mm limit.";
                }
                else if (options.KeepInsideMargins && !target.IsInside(options.Usable, Eps))
                {
                    target = current;
                    status = AlignStatus.Blocked;
                    note = "Its aligned position would cross the margins.";
                }
                else
                {
                    status = AlignStatus.Move;
                }

                result.Placements.Add(new BoxPlacement
                {
                    Key = boxes[i].Key,
                    Target = target,
                    Row = rowOf[i] + 1,
                    Column = columnOf[i] + 1,
                    Status = status,
                    Note = note,
                    Distance = status == AlignStatus.Move || status == AlignStatus.OverLimit ? distance : 0
                });
            }
        }

        /// <summary>
        /// Looks for two elements that would share space once the plan is applied. Elements that are
        /// not moving are included, because a view moving into a pinned one is exactly the clash
        /// worth catching. A hair of overlap is allowed so that touching edges do not count.
        /// </summary>
        private static void CheckOverlaps(IReadOnlyList<TrackBox> boxes, IReadOnlyList<TrackBox> frozen,
            AlignEngineOptions options, AlignEngineResult result)
        {
            if (!options.StopOnOverlap) return;

            var rects = result.Placements.Select(p => (p.Key, p.Target)).ToList();
            if (frozen != null)
                rects.AddRange(frozen.Select(f => (f.Key, f.Rect)));

            const double touch = 1e-5; // ~0.003 mm: edges that meet are not an overlap
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    if (rects[i].Item2.Overlaps(rects[j].Item2, touch))
                    {
                        result.Overlap = (rects[i].Key, rects[j].Key);
                        return;
                    }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static EdgeChoice EdgeFor(RowAlign align) => align switch
        {
            RowAlign.Top => EdgeChoice.High,
            RowAlign.Center => EdgeChoice.Center,
            _ => EdgeChoice.Low
        };

        private static EdgeChoice EdgeFor(ColumnAlign align) => align switch
        {
            ColumnAlign.Right => EdgeChoice.High,
            ColumnAlign.Center => EdgeChoice.Center,
            _ => EdgeChoice.Low
        };

        /// <summary>Which track each box index belongs to.</summary>
        private static int[] IndexMap(List<List<int>> tracks)
        {
            int count = tracks.Sum(t => t.Count);
            var map = new int[count];
            for (int t = 0; t < tracks.Count; t++)
                foreach (int i in tracks[t]) map[i] = t;
            return map;
        }
    }
}
