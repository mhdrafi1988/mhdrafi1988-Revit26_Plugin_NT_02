using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout
{
    /// <summary>
    /// One box handed to the layout maths: a footprint plus the clearance its view carries.
    /// Revit-free, so the detection and alignment can be unit tested.
    /// </summary>
    public readonly struct TrackBox
    {
        /// <summary>The caller's index for this box; every result is given back in these terms.</summary>
        public int Key { get; }

        /// <summary>Footprint in sheet feet.</summary>
        public LayoutRect Rect { get; }

        /// <summary>Extra clearance this view keeps on its left (feet).</summary>
        public double PadLeft { get; }

        /// <summary>Extra clearance this view keeps on its right (feet).</summary>
        public double PadRight { get; }

        /// <summary>Extra clearance this view keeps below it (feet).</summary>
        public double PadBottom { get; }

        /// <summary>Extra clearance this view keeps above it (feet).</summary>
        public double PadTop { get; }

        /// <summary>Creates a box. Negative pads are treated as none.</summary>
        public TrackBox(int key, LayoutRect rect,
            double padLeft = 0, double padRight = 0, double padBottom = 0, double padTop = 0)
        {
            Key = key;
            Rect = rect;
            PadLeft = Math.Max(0, padLeft);
            PadRight = Math.Max(0, padRight);
            PadBottom = Math.Max(0, padBottom);
            PadTop = Math.Max(0, padTop);
        }
    }

    /// <summary>
    /// Reads the rows and columns the views are <i>already</i> in, off their current positions.
    /// Nothing is re-ordered: a view's row and column membership is the only thing taken from the
    /// sheet, and the alignment then moves it as little as possible inside that membership.
    ///
    /// Two views share a row when their footprints overlap vertically at all, or when any pair of
    /// their horizontal edges (bottom, centre, top) is within the row tolerance. Columns are the
    /// mirror. Membership is transitive (union-find), so the result does not depend on the order
    /// the views are read in. One consequence is deliberate: a tall view that genuinely spans two
    /// bands of the sheet joins them into one row, which the plan then reports as a single wide
    /// row rather than silently splitting it.
    /// </summary>
    public static class TrackDetection
    {
        private const double Eps = 1e-9;

        /// <summary>
        /// Rows, top row first, each row ordered left to right. Every index of
        /// <paramref name="boxes"/> appears exactly once.
        /// </summary>
        public static List<List<int>> DetectRows(IReadOnlyList<TrackBox> boxes, double tolerance)
            => Cluster(boxes, tolerance, SameRow)
                .OrderByDescending(group => group.Max(i => boxes[i].Rect.MaxY))
                .Select(group => group.OrderBy(i => boxes[i].Rect.MinX).ThenByDescending(i => boxes[i].Rect.MaxY).ToList())
                .ToList();

        /// <summary>
        /// Columns, left column first, each column ordered top to bottom. Every index of
        /// <paramref name="boxes"/> appears exactly once.
        /// </summary>
        public static List<List<int>> DetectColumns(IReadOnlyList<TrackBox> boxes, double tolerance)
            => Cluster(boxes, tolerance, SameColumn)
                .OrderBy(group => group.Min(i => boxes[i].Rect.MinX))
                .Select(group => group.OrderByDescending(i => boxes[i].Rect.MaxY).ThenBy(i => boxes[i].Rect.MinX).ToList())
                .ToList();

        /// <summary>True when the two footprints belong to one row.</summary>
        internal static bool SameRow(LayoutRect a, LayoutRect b, double tolerance)
            => a.VerticalOverlap(b) > Eps
            || Math.Abs(a.MinY - b.MinY) <= tolerance
            || Math.Abs(a.CenterY - b.CenterY) <= tolerance
            || Math.Abs(a.MaxY - b.MaxY) <= tolerance;

        /// <summary>True when the two footprints belong to one column.</summary>
        internal static bool SameColumn(LayoutRect a, LayoutRect b, double tolerance)
            => a.HorizontalOverlap(b) > Eps
            || Math.Abs(a.MinX - b.MinX) <= tolerance
            || Math.Abs(a.CenterX - b.CenterX) <= tolerance
            || Math.Abs(a.MaxX - b.MaxX) <= tolerance;

        // Union-find over every pair, so membership is transitive and order-independent. The lists
        // here are at most a sheet's worth of views, so the O(n²) pass costs nothing.
        private static List<List<int>> Cluster(IReadOnlyList<TrackBox> boxes, double tolerance,
            Func<LayoutRect, LayoutRect, double, bool> together)
        {
            int n = boxes?.Count ?? 0;
            var groups = new List<List<int>>();
            if (n == 0) return groups;

            double tol = Math.Max(0, tolerance);
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (together(boxes[i].Rect, boxes[j].Rect, tol))
                    {
                        int ri = Find(i), rj = Find(j);
                        if (ri != rj) parent[ri] = rj;
                    }

            var byRoot = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!byRoot.TryGetValue(root, out var list))
                    byRoot[root] = list = new List<int>();
                list.Add(i);
            }

            groups.AddRange(byRoot.Values);
            return groups;
        }
    }
}
