// =======================================================
// File: RidgeFinder.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
// New in V033. Finds the ridge lines between drain groups from values the
// run already has: each shape point's calculated elevation and the drain
// group its path runs to. It never changes an elevation. No Revit API, so
// it is unit-tested (Tests/AutoSlopeByPoint.Tests).
//
//   1. Short link: a straight path-graph link (inside the roof, never across
//      an opening) with no other point inside or on the circle drawn on it as
//      its diameter. These are the local neighbours, close to how Revit
//      triangulates the roof.
//   2. Ridge point: on every short link whose two ends run to different drain
//      groups, the higher end. Equal heights (within the tie tolerance) go to
//      the lower group number, so a ridge stays one point wide. Drains and
//      points left at 0 are never ridge points.
//   3. Junction: a ridge point on the ridges of two or more group pairs joins
//      every ridge among those groups, so the ridges meet.
//   4. Each group pair's ridge points are joined by the shortest set of short
//      links between them (a minimum spanning tree). A link longer than 3 ×
//      the tree's median link is a real gap and is left out.
//   5. Each run between junctions and ends is straightened (Douglas–Peucker,
//      tolerance 0.75 × the median segment length), so a diagonal ridge is one
//      line, not a staircase. Every line starts and ends on a ridge point.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services
{
    /// <summary>One ridge line, from shape point <see cref="A"/> to shape point <see cref="B"/> (vertex indices).</summary>
    public readonly record struct RidgeLine(int A, int B);

    /// <summary>Inputs for <see cref="RidgeFinder.Find"/>. All lists are indexed by vertex.</summary>
    public sealed class RidgeInput
    {
        /// <summary>Shape point positions; only X and Y are used.</summary>
        public IReadOnlyList<Point3> Positions { get; init; }

        /// <summary>The run's path graph. Only its straight links count as "inside the roof".</summary>
        public PathGraph Graph { get; init; }

        /// <summary>Drain group per point; -1 = not sloped (no path, or beyond the drainage distance).</summary>
        public IReadOnlyList<int> GroupOf { get; init; }

        /// <summary>Calculated elevation per point, in any unit (<see cref="TieTolerance"/> uses the same unit).</summary>
        public IReadOnlyList<double> Elevation { get; init; }

        /// <summary>True for drain points.</summary>
        public IReadOnlyList<bool> IsDrain { get; init; }

        /// <summary>Two link ends closer in height than this count as equal.</summary>
        public double TieTolerance { get; init; }
    }

    /// <summary>Output of <see cref="RidgeFinder.Find"/>.</summary>
    public sealed class RidgeResult
    {
        /// <summary>The lines to draw.</summary>
        public List<RidgeLine> Lines { get; } = new List<RidgeLine>();

        /// <summary>Every ridge point, ascending.</summary>
        public List<int> RidgePoints { get; } = new List<int>();

        /// <summary>Ridge points where three or more drain groups meet, ascending.</summary>
        public List<int> Junctions { get; } = new List<int>();

        /// <summary>Number of drain groups among the sloped points.</summary>
        public int GroupCount { get; set; }

        /// <summary>Drain-group pairs that got at least one line.</summary>
        public int RidgeCount { get; set; }
    }

    /// <summary>Finds ridge lines between drain groups. See the file header for the method.</summary>
    public static class RidgeFinder
    {
        /// <summary>Nearest points tried as short-link partners in step 2.</summary>
        public const int NeighbourCandidates = 16;

        /// <summary>Nearest ridge points tried as tree links in step 4.</summary>
        public const int RidgeNeighbourCandidates = 8;

        /// <summary>Step 4: a tree link longer than this × the median tree link is a gap.</summary>
        public const double MaxGapFactor = 3.0;

        /// <summary>Step 5: zig-zags smaller than this × the median segment length are straightened.</summary>
        public const double StraightenFactor = 0.75;

        private const double CircleEps = 1e-6;

        /// <summary>Runs steps 1–5. Returns no lines (never throws) when fewer than two drain groups are sloped.</summary>
        /// <exception cref="ArgumentException">The input lists are missing or of different lengths.</exception>
        /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
        public static RidgeResult Find(RidgeInput input, CancellationToken cancel = default)
        {
            Validate(input);
            var result = new RidgeResult();
            IReadOnlyList<Point3> pos = input.Positions;
            int n = pos.Count;

            result.GroupCount = input.GroupOf.Where(g => g >= 0).Distinct().Count();
            if (result.GroupCount < 2 || n < 2) return result;

            double spacing = TypicalSpacing(pos);
            double reach = Diagonal(pos) + spacing;
            var grid = new SpatialGrid2D(pos, spacing * 2);
            var straight = new StraightLinks(input.Graph, pos);

            // ── 2. Ridge points ────────────────────────────────────────────
            var pairs = new SortedDictionary<(int, int), HashSet<int>>();
            var pairsOfPoint = new SortedDictionary<int, HashSet<(int, int)>>();
            for (int i = 0; i < n; i++)
            {
                if ((i & 0xFF) == 0) cancel.ThrowIfCancellationRequested();
                int gi = input.GroupOf[i];
                if (gi < 0) continue;

                straight.Mark(i);
                foreach (int j in Nearest(pos, grid, i, NeighbourCandidates, spacing, reach))
                {
                    int gj = input.GroupOf[j];
                    if (gj < 0 || gj == gi || !straight.IsLinked(j)) continue;
                    if (!IsShortLink(pos, grid, i, j)) continue;

                    int top = Higher(input, i, j);
                    if (input.IsDrain[top]) continue;

                    var key = gi < gj ? (gi, gj) : (gj, gi);
                    if (!pairs.TryGetValue(key, out var set)) pairs[key] = set = new HashSet<int>();
                    set.Add(top);
                    if (!pairsOfPoint.TryGetValue(top, out var keys)) pairsOfPoint[top] = keys = new HashSet<(int, int)>();
                    keys.Add(key);
                }
            }

            // ── 3. Junctions ───────────────────────────────────────────────
            foreach (var (point, keys) in pairsOfPoint)
            {
                if (keys.Count < 2) continue;
                result.Junctions.Add(point);
                var groups = keys.SelectMany(k => new[] { k.Item1, k.Item2 }).Distinct().OrderBy(g => g).ToList();
                for (int a = 0; a < groups.Count; a++)
                    for (int b = a + 1; b < groups.Count; b++)
                        if (pairs.TryGetValue((groups[a], groups[b]), out var set)) set.Add(point);
            }

            // ── 4. Join each pair's ridge points ───────────────────────────
            var segments = new List<(int A, int B)>();
            var seen = new HashSet<(int, int)>();
            foreach (var set in pairs.Values)
            {
                cancel.ThrowIfCancellationRequested();
                int added = 0;
                foreach (var (a, b) in JoinRidgePoints(set.OrderBy(v => v).ToList(), pos, straight, spacing, reach))
                {
                    var key = a < b ? (a, b) : (b, a);
                    if (seen.Add(key)) { segments.Add(key); added++; }
                }
                if (added > 0) result.RidgeCount++;
            }

            // ── 5. Straighten ──────────────────────────────────────────────
            result.Lines.AddRange(Straighten(segments, pos));
            result.RidgePoints.AddRange(pairs.Values.SelectMany(s => s).Distinct().OrderBy(v => v));
            return result;
        }

        private static void Validate(RidgeInput input)
        {
            if (input?.Positions == null || input.Graph == null || input.GroupOf == null
                || input.Elevation == null || input.IsDrain == null)
                throw new ArgumentException("Ridge input is incomplete.", nameof(input));

            int n = input.Positions.Count;
            if (input.Graph.VertexCount != n || input.GroupOf.Count != n || input.Elevation.Count != n || input.IsDrain.Count != n)
                throw new ArgumentException("Ridge input lists differ in length.", nameof(input));
        }

        /// <summary>The higher end of link i–j; on a tie, the end in the lower-numbered group.</summary>
        private static int Higher(RidgeInput input, int i, int j)
        {
            double diff = input.Elevation[i] - input.Elevation[j];
            if (Math.Abs(diff) <= input.TieTolerance)
                return input.GroupOf[i] <= input.GroupOf[j] ? i : j;
            return diff > 0 ? i : j;
        }

        /// <summary>Step 4 for one group pair: minimum spanning tree over short links, gaps left out.</summary>
        private static IEnumerable<(int A, int B)> JoinRidgePoints(
            List<int> points, IReadOnlyList<Point3> pos, StraightLinks straight, double spacing, double reach)
        {
            int k = points.Count;
            if (k < 2) yield break;

            var sub = points.Select(v => pos[v]).ToList();
            var grid = new SpatialGrid2D(sub, spacing * 2);
            var candidates = new List<(double W, int A, int B)>();
            var tried = new HashSet<(int, int)>();
            for (int a = 0; a < k; a++)
            {
                straight.Mark(points[a]);
                foreach (int b in Nearest(sub, grid, a, RidgeNeighbourCandidates, spacing, reach))
                {
                    var key = a < b ? (a, b) : (b, a);
                    if (!tried.Add(key)) continue;
                    if (!straight.IsLinked(points[b]) || !IsShortLink(sub, grid, a, b)) continue;
                    candidates.Add((sub[a].DistanceXY(sub[b]), key.Item1, key.Item2));
                }
            }

            // Kruskal
            candidates.Sort((x, y) => x.W.CompareTo(y.W));
            var parent = Enumerable.Range(0, k).ToArray();
            int Root(int x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }
            var tree = new List<(double W, int A, int B)>();
            foreach (var c in candidates)
            {
                int ra = Root(c.A), rb = Root(c.B);
                if (ra == rb) continue;
                parent[ra] = rb;
                tree.Add(c);
            }
            if (tree.Count == 0) yield break;

            double maxGap = MaxGapFactor * Median(tree.Select(t => t.W));
            foreach (var t in tree)
                if (t.W <= maxGap + CircleEps) yield return (points[t.A], points[t.B]);
        }

        /// <summary>Step 5: walk each run between junctions and ends, then straighten it.</summary>
        private static List<RidgeLine> Straighten(List<(int A, int B)> segments, IReadOnlyList<Point3> pos)
        {
            var lines = new List<RidgeLine>();
            if (segments.Count == 0) return lines;

            double tol = StraightenFactor * Median(segments.Select(s => pos[s.A].DistanceXY(pos[s.B])));
            var adj = new SortedDictionary<int, List<int>>();
            void Link(int a, int b)
            {
                if (!adj.TryGetValue(a, out var list)) adj[a] = list = new List<int>();
                list.Add(b);
            }
            foreach (var (a, b) in segments) { Link(a, b); Link(b, a); }

            var used = new HashSet<(int, int)>();
            (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
            // Start at ends and junctions first, so closed loops are the only runs started mid-way.
            var starts = adj.Keys.OrderBy(v => adj[v].Count == 2 ? 1 : 0).ThenBy(v => v).ToList();

            foreach (int start in starts)
            {
                foreach (int first in adj[start])
                {
                    if (!used.Add(Key(start, first))) continue;
                    var run = new List<int> { start, first };
                    int prev = start, cur = first;
                    while (adj[cur].Count == 2)
                    {
                        int next = adj[cur][0] == prev ? adj[cur][1] : adj[cur][0];
                        if (!used.Add(Key(cur, next))) break;
                        run.Add(next);
                        prev = cur;
                        cur = next;
                    }

                    List<int> kept = Simplify(run, pos, tol);
                    for (int i = 1; i < kept.Count; i++)
                        if (kept[i - 1] != kept[i]) lines.Add(new RidgeLine(kept[i - 1], kept[i]));
                }
            }
            return lines;
        }

        /// <summary>Douglas–Peucker: keeps the run's ends, drops points within <paramref name="tol"/> of the straight line.</summary>
        private static List<int> Simplify(List<int> run, IReadOnlyList<Point3> pos, double tol)
        {
            if (run.Count < 3) return run;
            Point3 a = pos[run[0]], b = pos[run[run.Count - 1]];
            int iMax = -1;
            double dMax = 0;
            for (int i = 1; i < run.Count - 1; i++)
            {
                double d = DistanceToSegment(pos[run[i]], a, b);
                if (d > dMax) { dMax = d; iMax = i; }
            }
            if (dMax <= tol) return new List<int> { run[0], run[run.Count - 1] };

            List<int> left = Simplify(run.GetRange(0, iMax + 1), pos, tol);
            List<int> right = Simplify(run.GetRange(iMax, run.Count - iMax), pos, tol);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        private static double DistanceToSegment(Point3 p, Point3 a, Point3 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2)) : 0;
            double ex = p.X - a.X - t * dx, ey = p.Y - a.Y - t * dy;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>True when no other point lies inside or on the circle drawn on link i–j as its diameter.</summary>
        private static bool IsShortLink(IReadOnlyList<Point3> pos, SpatialGrid2D grid, int i, int j)
        {
            var c = new Point3((pos[i].X + pos[j].X) / 2, (pos[i].Y + pos[j].Y) / 2, 0);
            double r = pos[i].DistanceXY(pos[j]) / 2;
            foreach (int k in grid.QueryXY(c, r + CircleEps))
                if (k != i && k != j && pos[k].DistanceXY(c) <= r + CircleEps) return false;
            return true;
        }

        /// <summary>Up to <paramref name="count"/> nearest other points of <paramref name="i"/> (XY), nearest first.</summary>
        private static List<int> Nearest(IReadOnlyList<Point3> pos, SpatialGrid2D grid, int i, int count, double spacing, double reach)
        {
            double radius = spacing * 2;
            List<int> found;
            while (true)
            {
                found = grid.QueryXY(pos[i], radius);
                if (found.Count > count || radius >= reach) break;
                radius *= 2;
            }
            found.Remove(i);
            return found.OrderBy(j => pos[i].DistanceXY(pos[j])).ThenBy(j => j).Take(count).ToList();
        }

        /// <summary>Median distance from a point to its nearest neighbour (XY): the roof's point spacing.</summary>
        private static double TypicalSpacing(IReadOnlyList<Point3> pos)
        {
            double area = Math.Max(Width(pos, p => p.X) * Width(pos, p => p.Y), 1e-6);
            double guess = Math.Max(Math.Sqrt(area / pos.Count), 1e-3);
            var grid = new SpatialGrid2D(pos, guess * 2);
            double reach = Diagonal(pos) + guess;
            var nearest = new List<double>(pos.Count);
            for (int i = 0; i < pos.Count; i++)
            {
                double radius = guess * 2;
                double best = double.MaxValue;
                while (true)
                {
                    foreach (int j in grid.QueryXY(pos[i], radius))
                    {
                        double d = pos[i].DistanceXY(pos[j]);
                        if (j != i && d > CircleEps && d < best) best = d;
                    }
                    if (best <= radius || radius >= reach) break;
                    radius *= 2;
                }
                if (best < double.MaxValue) nearest.Add(best);
            }
            return nearest.Count == 0 ? 1.0 : Math.Max(Median(nearest), 1e-3);
        }

        private static double Width(IReadOnlyList<Point3> pos, Func<Point3, double> axis) =>
            pos.Count == 0 ? 0 : pos.Max(axis) - pos.Min(axis);

        private static double Diagonal(IReadOnlyList<Point3> pos)
        {
            double w = Width(pos, p => p.X), h = Width(pos, p => p.Y);
            return Math.Sqrt(w * w + h * h);
        }

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
        }

        /// <summary>
        /// "Is j linked to the marked point by a straight path-graph link?" in O(1), after an
        /// O(degree) <see cref="Mark"/>. A link whose weight is longer than its chord goes
        /// round an arc, so a straight line along it could leave the roof: it does not count.
        /// </summary>
        private sealed class StraightLinks
        {
            private readonly PathGraph _graph;
            private readonly IReadOnlyList<Point3> _pos;
            private readonly int[] _stamp;
            private int _marked = -1;

            public StraightLinks(PathGraph graph, IReadOnlyList<Point3> pos)
            {
                _graph = graph;
                _pos = pos;
                _stamp = Enumerable.Repeat(-1, pos.Count).ToArray();
            }

            public void Mark(int i)
            {
                _marked = i;
                foreach (var (to, weight) in _graph.Neighbours(i))
                {
                    double chord = _pos[i].DistanceTo(_pos[to]);
                    if (weight <= chord * (1 + 1e-9) + 1e-9) _stamp[to] = i;
                }
            }

            public bool IsLinked(int j) => _stamp[j] == _marked;
        }
    }
}
