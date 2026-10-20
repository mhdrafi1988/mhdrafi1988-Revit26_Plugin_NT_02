// =======================================================
// File: ReachEstimator.cs
// Location: Core/Planning/
// Estimates, before running, which shape points the slope step (Auto
// Slope By Drain V013) will leave at 0 — the points that show as dips.
// Mirrors V013's rules in plan, without any Revit call:
//   • drain points = points on the edge of an opening ticked as a drain
//     (within 5 mm);
//   • two points are linked when they are 10 mm – Max edge apart and the
//     straight line between them stays on the roof (inside the boundary,
//     out of every opening);
//   • a point's distance is its shortest linked path to any drain point;
//     no path → "no path", longer than Max path → "beyond max path".
// V013 checks lines against the roof's top face with a few samples and
// routes round curved edges; this checks against the plan outline
// exactly, so the counts are an estimate, close but not guaranteed.
// =======================================================

using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Planning
{
    /// <summary>Outcome of a reach estimate.</summary>
    public sealed class ReachEstimate
    {
        /// <summary>Points considered (existing + planned).</summary>
        public int PointCount { get; init; }

        /// <summary>Points on a drain's edge.</summary>
        public int DrainPointCount { get; init; }

        /// <summary>Points with no linked path to any drain.</summary>
        public List<P2> NoPath { get; } = new List<P2>();

        /// <summary>Points whose shortest path is longer than Max path.</summary>
        public List<P2> BeyondMaxPath { get; } = new List<P2>();

        /// <summary>Points that would be left at 0.</summary>
        public int LeftAtZero => NoPath.Count + BeyondMaxPath.Count;
    }

    /// <summary>Estimates which points the slope step can't reach (see the file header).</summary>
    public static class ReachEstimator
    {
        /// <summary>More points than this are not estimated (it would take too long).</summary>
        public const int MaxPoints = 6000;

        private const double Tol = 5.0 / 304.8;
        private const double MinChord = 10.0 / 304.8;

        /// <summary>
        /// Estimates reach for <paramref name="points"/> (feet, plan). <paramref name="drains"/> are the
        /// drain openings, <paramref name="openings"/> every opening, <paramref name="boundaries"/> the roof outline.
        /// </summary>
        public static ReachEstimate Estimate(IReadOnlyList<P2> points, IReadOnlyList<Loop2D> drains,
                                             IReadOnlyList<Loop2D> openings, IReadOnlyList<Loop2D> boundaries,
                                             double maxEdgeFeet, double maxPathFeet, CancellationToken cancel = default)
        {
            var pts = Dedupe(points ?? Array.Empty<P2>());
            int n = pts.Count;
            var drainLoops = drains ?? Array.Empty<Loop2D>();
            var obstacles = new Outline(boundaries ?? Array.Empty<Loop2D>(), openings ?? Array.Empty<Loop2D>());

            var isDrain = new bool[n];
            for (int i = 0; i < n; i++)
                isDrain[i] = drainLoops.Any(d => d.DistanceTo(pts[i]) <= Tol);

            var result = new ReachEstimate { PointCount = n, DrainPointCount = isDrain.Count(d => d) };
            if (n == 0) return result;

            // Dijkstra from every drain point at once; edges are found lazily per settled point.
            var grid = new Grid(pts, Math.Max(maxEdgeFeet, 1e-3));
            var dist = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
            var done = new bool[n];
            var queue = new PriorityQueue<int, double>();
            for (int i = 0; i < n; i++)
            {
                if (!isDrain[i]) continue;
                dist[i] = 0;
                queue.Enqueue(i, 0);
            }

            while (queue.TryDequeue(out int u, out double du))
            {
                if (done[u] || du > dist[u]) continue;
                done[u] = true;
                cancel.ThrowIfCancellationRequested();

                foreach (int v in grid.Near(pts[u]))
                {
                    if (done[v]) continue;
                    double chord = pts[u].DistanceTo(pts[v]);
                    if (chord < MinChord || chord > maxEdgeFeet) continue;
                    double nd = du + chord;
                    if (nd >= dist[v]) continue;
                    if (!obstacles.SegmentOnRoof(pts[u], pts[v])) continue;
                    dist[v] = nd;
                    queue.Enqueue(v, nd);
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (isDrain[i]) continue;
                if (double.IsPositiveInfinity(dist[i])) result.NoPath.Add(pts[i]);
                else if (dist[i] > maxPathFeet) result.BeyondMaxPath.Add(pts[i]);
            }

            return result;
        }

        private static List<P2> Dedupe(IEnumerable<P2> points)
        {
            var kept = new List<P2>();
            var grid = new Dictionary<(long, long), List<P2>>();
            foreach (P2 p in points)
            {
                long cx = (long)Math.Floor(p.X / Tol), cy = (long)Math.Floor(p.Y / Tol);
                bool dup = false;
                for (long dx = -1; dx <= 1 && !dup; dx++)
                    for (long dy = -1; dy <= 1 && !dup; dy++)
                        if (grid.TryGetValue((cx + dx, cy + dy), out var cell) && cell.Any(q => q.DistanceTo(p) <= Tol))
                            dup = true;
                if (dup) continue;
                kept.Add(p);
                if (!grid.TryGetValue((cx, cy), out var list)) grid[(cx, cy)] = list = new List<P2>();
                list.Add(p);
            }
            return kept;
        }

        /// <summary>Square buckets of side = Max edge, so a point's candidates are in the 3×3 cells around it.</summary>
        private sealed class Grid
        {
            private readonly IReadOnlyList<P2> _pts;
            private readonly double _size;
            private readonly Dictionary<(long, long), List<int>> _cells = new();

            public Grid(IReadOnlyList<P2> pts, double size)
            {
                _pts = pts;
                _size = size;
                for (int i = 0; i < pts.Count; i++)
                {
                    var key = Key(pts[i]);
                    if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>();
                    list.Add(i);
                }
            }

            private (long, long) Key(P2 p) => ((long)Math.Floor(p.X / _size), (long)Math.Floor(p.Y / _size));

            public IEnumerable<int> Near(P2 p)
            {
                var (cx, cy) = Key(p);
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                        if (_cells.TryGetValue((cx + dx, cy + dy), out var list))
                            foreach (int i in list) yield return i;
            }
        }

        /// <summary>The roof in plan: is a straight line on it?</summary>
        private sealed class Outline
        {
            private readonly List<(P2 A, P2 B)> _edges = new();
            private readonly IReadOnlyList<Loop2D> _boundaries;
            private readonly IReadOnlyList<Loop2D> _openings;

            public Outline(IReadOnlyList<Loop2D> boundaries, IReadOnlyList<Loop2D> openings)
            {
                _boundaries = boundaries;
                _openings = openings;
                foreach (Loop2D loop in boundaries.Concat(openings))
                {
                    var s = loop.Samples;
                    for (int i = 0; i < s.Count; i++)
                        _edges.Add((s[i], s[(i + 1) % s.Count]));
                }
            }

            /// <summary>
            /// True when the line a–b crosses no boundary or opening edge (touching within
            /// 5 mm is allowed, so lines along an edge count) and its midpoint is on the roof.
            /// </summary>
            public bool SegmentOnRoof(P2 a, P2 b)
            {
                foreach (var (c, d) in _edges)
                    if (ProperlyCross(a, b, c, d)) return false;

                P2 mid = (a + b) * 0.5;
                bool inBoundary = _boundaries.Any(l => l.Contains(mid) || l.DistanceTo(mid) <= Tol);
                if (!inBoundary) return false;
                return !_openings.Any(o => o.Contains(mid) && o.DistanceTo(mid) > Tol);
            }

            /// <summary>
            /// The line a–b crosses the outline edge c–d more than 5 mm from a and b (so a line may
            /// start or end on an edge). u is half-open so a crossing exactly at an outline vertex
            /// counts once; no tolerance there, so short sample edges of small circles still block.
            /// </summary>
            private static bool ProperlyCross(P2 a, P2 b, P2 c, P2 d)
            {
                P2 r = b - a, s = d - c;
                double denom = r.Cross(s);
                if (Math.Abs(denom) < 1e-12) return false; // parallel or collinear: not a crossing
                double t = (c - a).Cross(s) / denom;
                double u = (c - a).Cross(r) / denom;
                double lenR = r.Length;
                double et = lenR > 0 ? Tol / lenR : 1;
                return t > et && t < 1 - et && u >= 0 && u < 1;
            }
        }
    }
}
