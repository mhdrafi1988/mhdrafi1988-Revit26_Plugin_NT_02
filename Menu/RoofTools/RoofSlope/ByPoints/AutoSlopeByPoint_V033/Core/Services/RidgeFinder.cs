// =======================================================
// File: RidgeFinder.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services
// New in V033; rewritten for the true ridge position. Finds the ridge lines
// between drain groups at the real watershed: the line where two groups
// are equally far away along the roof (a straight line, or round opening
// and edge corners — see GeodesicField). On an open roof that is the
// perpendicular bisector (Voronoi edge) between the drains. Ridge points
// are not snapped to shape points, and no elevation is changed. No Revit
// API, so it is unit-tested (Tests/AutoSlopeByPoint.Tests).
//
//   1. Validate the input. A drain just outside the outline (up to 150 mm,
//      e.g. a shape point on an arc between the outline's tessellation
//      points) is moved onto it; one further out is counted and reaches
//      nothing. Build the GeodesicField. Fewer than two groups: no lines.
//   2. Grid: square cells of side h = max(√(bounding-box area / 40 000),
//      50 mm) over the outline's bounding box. Each node gets the group
//      nearest to it, or -1 when it is off the roof or further than the
//      drainage distance from every drain.
//   3. Crossings: in each cell that shows two groups, every side whose ends
//      belong to different groups and see each other across the roof gets
//      the point where both groups are equally far, found by bisection to
//      1e-5 ft. Neighbouring cells share the crossing on their common side.
//      Cells cut by the outline count too, so a ridge runs on up to an edge
//      it meets at a shallow angle.
//   4. Segments: two crossings in a cell are joined when they see each
//      other. Four (a saddle) are joined in two pairs, round the corners
//      whose group differs from the cell centre's. 0 or 1 crossings (a
//      corner off the roof or beyond the drainage distance), or 3 (a side
//      cut by the outline), give none. Cells with three or more groups are
//      skipped here.
//   5. Chain and straighten: zero-length segments are dropped (their ends
//      become one point). Free ends of the same group pair within 3 h that
//      see each other are bridged, closest first, so a ridge broken up along
//      an edge is one run again. Each run between ends and branch points (or
//      a whole closed loop) is straightened by Douglas–Peucker within 20 mm,
//      so a straight ridge is one line.
//   6. Reach the roof edge: a line end that no other line uses and that lies
//      within 3 h of the outline is moved on, away from the line's other end,
//      to the first edge within 20 h, if that edge point is still sloped. So
//      an end where the drainage distance stops the slope stays where it is.
//   7. RidgeCount = the drain-group pairs with at least one segment. Lines
//      come out in the order the runs were walked.
// =======================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services
{
    /// <summary>One ridge line between two plan points (Revit feet, Z = 0).</summary>
    /// <param name="A">One end.</param>
    /// <param name="B">The other end.</param>
    public readonly record struct RidgeLine(Point3 A, Point3 B);

    /// <summary>Inputs for <see cref="RidgeFinder.Find"/>.</summary>
    public sealed class RidgeInput
    {
        /// <summary>The roof's plan outline: outer boundary and openings.</summary>
        public RoofOutline Outline { get; init; }

        /// <summary>Drain positions; only X and Y are used.</summary>
        public IReadOnlyList<Point3> Drains { get; init; }

        /// <summary>
        /// Group id of each drain, same length as <see cref="Drains"/>, not negative. Ridges run only
        /// between different groups.
        /// </summary>
        public IReadOnlyList<int> DrainGroup { get; init; }

        /// <summary>
        /// Drainage distance in feet: roof further than this from every drain is not sloped, so no
        /// ridge runs there. +∞ (the default) = no limit.
        /// </summary>
        public double MaxDistance { get; init; } = double.PositiveInfinity;
    }

    /// <summary>Output of <see cref="RidgeFinder.Find"/>.</summary>
    public sealed class RidgeResult
    {
        /// <summary>The lines to draw, in plan (Z = 0).</summary>
        public List<RidgeLine> Lines { get; } = new List<RidgeLine>();

        /// <summary>Points where three or more drain groups meet.</summary>
        public List<Point3> Junctions { get; } = new List<Point3>();

        /// <summary>Number of distinct drain groups.</summary>
        public int GroupCount { get; set; }

        /// <summary>Drain-group pairs with a ridge between them.</summary>
        public int RidgeCount { get; set; }

        /// <summary>Side of the grid cells in feet; 0 when no grid was needed (fewer than two groups).</summary>
        public double GridStep { get; set; }

        /// <summary>
        /// Drains that lay just outside the outline (within <see cref="RidgeFinder.DrainSnapToleranceFt"/>,
        /// e.g. a shape point on an arc between its tessellation points) and were moved onto it.
        /// </summary>
        public int DrainsMoved { get; set; }

        /// <summary>
        /// Drains further outside the outline than <see cref="RidgeFinder.DrainSnapToleranceFt"/>. They are
        /// left where they are, so they reach no roof point and get no ridge.
        /// </summary>
        public int DrainsOutside { get; set; }
    }

    /// <summary>Finds the ridge lines between drain groups. See the file header for the method.</summary>
    public static class RidgeFinder
    {
        /// <summary>Step 2: the grid over the outline's bounding box gets about this many nodes.</summary>
        public const int TargetGridNodes = 40_000;

        /// <summary>Step 2: the smallest grid step, 50 mm in feet.</summary>
        public const double MinGridStepFt = 50 / 304.8;

        /// <summary>Step 5: points within this distance (20 mm, in feet) of a straight line are dropped.</summary>
        public const double StraightenToleranceFt = 20 / 304.8;

        /// <summary>Step 6: only a free line end within this × the grid step of the outline is extended.</summary>
        public const double EndReachFactor = 3.0;

        /// <summary>Step 6: a free line end is extended by at most this × the grid step.</summary>
        public const double MaxReachFactor = 20.0;

        /// <summary>
        /// Step 1: a drain outside the outline by at most this (150 mm, in feet) is moved onto it.
        /// </summary>
        public const double DrainSnapToleranceFt = 150 / 304.8;

        /// <summary>Junctions closer than this × the grid step count as one junction.</summary>
        public const double JunctionMergeFactor = 0.5;

        /// <summary>Step 3: bisection stops when the interval is shorter than this (feet).</summary>
        private const double CrossingToleranceFt = 1e-5;

        /// <summary>Step 3: most bisection steps per crossing.</summary>
        private const int MaxBisectionSteps = 40;

        /// <summary>Step 5: a segment shorter than this (feet) has zero length.</summary>
        private const double ZeroLengthFt = 1e-4;

        /// <summary>Step 5: free ends closer than this × the grid step are bridged.</summary>
        private const double BridgeFactor = 3.0;

        /// <summary>Cancellation is checked once per this many grid nodes or cells.</summary>
        private const int CancelCheckInterval = 256;

        /// <summary>Runs steps 1–7.</summary>
        /// <param name="input">Outline, drains, their groups and the drainage distance.</param>
        /// <param name="cancel">Cancels the search; checked per grid row and every 256 nodes or cells.</param>
        /// <returns>The ridge lines; none when there are fewer than two drain groups.</returns>
        /// <exception cref="ArgumentException">
        /// The outline, drains or groups are missing, the drain and group lists differ in length, or a
        /// group id is negative.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancel"/> was cancelled.</exception>
        public static RidgeResult Find(RidgeInput input, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            Validate(input);

            // ── 1. Drains on the outline, distance field ───────────────────
            var result = new RidgeResult();
            List<Point3> drains = SnapDrains(input.Outline, input.Drains, result);
            var field = new GeodesicField(input.Outline, drains, input.DrainGroup, cancel);
            result.GroupCount = field.GroupCount;
            if (field.GroupCount < 2) return result;

            // ── 2. Labelled grid ───────────────────────────────────────────
            var grid = new LabelGrid(input.Outline, field, input.MaxDistance, cancel);
            result.GridStep = grid.Step;

            // ── 3–4. Crossings and segments ────────────────────────────────
            var net = new RidgeNet();
            FindSegments(grid, field, input.Outline, net, cancel);

            // ── 5. Chain and straighten ────────────────────────────────────
            net.DropZeroLength();
            BridgeGaps(net, input.Outline, BridgeFactor * grid.Step);
            result.RidgeCount = net.PairCount();
            List<(int A, int B)> lines = Straighten(net);

            // ── 6. Reach the roof edge ─────────────────────────────────────
            Point3[] at = net.Points.ToArray();
            ReachEdge(lines, at, input.Outline, field, input.MaxDistance, grid.Step);

            foreach (var (a, b) in lines)
                result.Lines.Add(new RidgeLine(at[a], at[b]));
            return result;
        }

        private static void Validate(RidgeInput input)
        {
            if (input == null) throw new ArgumentException("Ridge input is required.", nameof(input));
            if (input.Outline == null) throw new ArgumentException("The roof outline is required.", nameof(input));
            if (input.Drains == null) throw new ArgumentException("The drain list is required.", nameof(input));
            if (input.DrainGroup == null) throw new ArgumentException("The drain group list is required.", nameof(input));
            if (input.Drains.Count != input.DrainGroup.Count)
                throw new ArgumentException("Every drain needs exactly one group id.", nameof(input));
            for (int i = 0; i < input.DrainGroup.Count; i++)
                if (input.DrainGroup[i] < 0)
                    throw new ArgumentException("Group ids must not be negative.", nameof(input));
        }

        /// <summary>
        /// Step 1: the drains, each one that <see cref="RoofOutline.Contains"/> rejects moved to the
        /// closest outline point when that is within <see cref="DrainSnapToleranceFt"/>. Counts the moved
        /// and the outside drains in <paramref name="result"/>.
        /// </summary>
        private static List<Point3> SnapDrains(RoofOutline outline, IReadOnlyList<Point3> drains, RidgeResult result)
        {
            var snapped = new List<Point3>(drains.Count);
            foreach (Point3 d in drains)
            {
                if (outline.Contains(d)) { snapped.Add(d); continue; }

                Point3 on = outline.ClosestPoint(d, out double gap);
                if (gap <= DrainSnapToleranceFt)
                {
                    snapped.Add(on);
                    result.DrainsMoved++;
                }
                else
                {
                    snapped.Add(d);
                    result.DrainsOutside++;
                }
            }
            return snapped;
        }

        // ------------------------------------------------------------------
        // Steps 3–4: crossings and segments
        // ------------------------------------------------------------------

        /// <summary>
        /// Walks the grid cells and adds each two-group cell's segments to <paramref name="net"/>. Sides
        /// and segments that leave the roof (cross an outline edge) are left out.
        /// </summary>
        private static void FindSegments(LabelGrid grid, GeodesicField field, RoofOutline outline, RidgeNet net, CancellationToken cancel)
        {
            var crossingOfSide = new Dictionary<long, int>();
            var corner = new int[4];   // labels, counter-clockwise from the lower-left corner
            var side = new int[4];     // crossing on side k (corner k to corner k + 1), or -1
            int visited = 0;

            for (int j = 0; j < grid.Ny; j++)
            {
                cancel.ThrowIfCancellationRequested();
                for (int i = 0; i < grid.Nx; i++)
                {
                    if (++visited % CancelCheckInterval == 0) cancel.ThrowIfCancellationRequested();

                    corner[0] = grid.Label(i, j);
                    corner[1] = grid.Label(i + 1, j);
                    corner[2] = grid.Label(i + 1, j + 1);
                    corner[3] = grid.Label(i, j + 1);
                    // One group: no ridge here. Three or more: they meet in this cell (a junction), skipped.
                    if (CountGroups(corner, out int ga, out int gb) != 2) continue;

                    int count = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        side[k] = -1;
                        int a = corner[k], b = corner[(k + 1) % 4];
                        if (a < 0 || b < 0 || a == b) continue;
                        side[k] = Crossing(grid, field, outline, net, crossingOfSide, i, j, k);
                        if (side[k] >= 0) count++;
                    }

                    var pair = (Math.Min(ga, gb), Math.Max(ga, gb));
                    if (count == 2)
                    {
                        int first = -1;
                        for (int k = 0; k < 4; k++)
                        {
                            if (side[k] < 0) continue;
                            if (first < 0) first = side[k];
                            else Join(net, outline, first, side[k], pair);
                        }
                    }
                    else if (count == 4)
                    {
                        // Saddle: the centre's group (the nearer of the two; a tie goes to the lower
                        // id, as for the nodes) decides which two corners are cut off.
                        Point3 lo = grid.Node(i, j), hi = grid.Node(i + 1, j + 1);
                        Point3 c = new Point3(0.5 * (lo.X + hi.X), 0.5 * (lo.Y + hi.Y), 0);
                        double f = field.DistanceTo(c, ga) - field.DistanceTo(c, gb);
                        int centre = f > 0 || (f == 0 && gb < ga) ? gb : ga;
                        for (int k = 0; k < 4; k++)
                            if (corner[k] != centre)
                                Join(net, outline, side[(k + 3) % 4], side[k], pair);
                    }
                    // 0 or 1 crossings (a corner off the roof or beyond the drainage distance), or 3 (a
                    // side cut by the outline): no pair to join.
                }
            }
        }

        /// <summary>Adds the segment between two crossings when it stays on the roof.</summary>
        private static void Join(RidgeNet net, RoofOutline outline, int a, int b, (int, int) pair)
        {
            if (outline.IsVisible(net.Points[a], net.Points[b])) net.AddSegment(a, b, pair);
        }

        /// <summary>Number of different groups (≥ 0) among the four corners; the first two found.</summary>
        private static int CountGroups(int[] corner, out int ga, out int gb)
        {
            ga = -1;
            gb = -1;
            int count = 0;
            for (int k = 0; k < 4; k++)
            {
                int g = corner[k];
                if (g < 0 || Array.IndexOf(corner, g) < k) continue;   // no group, or seen at an earlier corner
                count++;
                if (count == 1) ga = g;
                else if (count == 2) gb = g;
            }
            return count;
        }

        /// <summary>
        /// The crossing on side <paramref name="k"/> of cell (i, j), computed once per side so the
        /// two cells that share it share the point; -1 when the side leaves the roof.
        /// </summary>
        private static int Crossing(
            LabelGrid grid, GeodesicField field, RoofOutline outline, RidgeNet net,
            Dictionary<long, int> crossingOfSide, int i, int j, int k)
        {
            // Each side is stored by its lower-left node and direction (0 = towards +X, 1 = towards +Y).
            int ni, nj, dir;
            switch (k)
            {
                case 0: ni = i; nj = j; dir = 0; break;         // bottom
                case 1: ni = i + 1; nj = j; dir = 1; break;     // right
                case 2: ni = i; nj = j + 1; dir = 0; break;     // top
                default: ni = i; nj = j; dir = 1; break;        // left
            }
            long key = 2 * ((long)nj * (grid.Nx + 1) + ni) + dir;
            if (crossingOfSide.TryGetValue(key, out int id)) return id;

            int mi = dir == 0 ? ni + 1 : ni, mj = dir == 0 ? nj : nj + 1;
            Point3 p = grid.Node(ni, nj), q = grid.Node(mi, mj);
            id = outline.IsVisible(p, q)
                ? net.AddPoint(Bisect(field, p, grid.Label(ni, nj), q, grid.Label(mi, mj)))
                : -1;
            crossingOfSide[key] = id;
            return id;
        }

        /// <summary>
        /// The point between <paramref name="p"/> (nearest to group <paramref name="gp"/>) and
        /// <paramref name="q"/> (nearest to <paramref name="gq"/>) where both groups are equally far:
        /// bisection on D(gp) − D(gq) until the interval is under 1e-5 ft, at most 40 steps. A NaN
        /// (neither group reachable) stops it at that midpoint.
        /// </summary>
        private static Point3 Bisect(GeodesicField field, Point3 p, int gp, Point3 q, int gq)
        {
            double len = p.DistanceXY(q);
            double lo = 0, hi = 1;
            for (int step = 0; step < MaxBisectionSteps && (hi - lo) * len >= CrossingToleranceFt; step++)
            {
                double m = 0.5 * (lo + hi);
                Point3 x = Lerp(p, q, m);
                double f = field.DistanceTo(x, gp) - field.DistanceTo(x, gq);
                if (double.IsNaN(f)) { lo = hi = m; break; }
                if (f > 0) hi = m;
                else lo = m;
            }
            return Lerp(p, q, 0.5 * (lo + hi));
        }

        private static Point3 Lerp(Point3 p, Point3 q, double t) =>
            new Point3(p.X + t * (q.X - p.X), p.Y + t * (q.Y - p.Y), 0);

        // ------------------------------------------------------------------
        // Step 5: chain and straighten
        // ------------------------------------------------------------------

        /// <summary>
        /// Joins free ends (points on exactly one segment) of the same group pair that lie within
        /// <paramref name="reach"/> of each other and see each other: closest pair first, each end
        /// once, never two ends of the same piece (that would close it into a loop).
        /// </summary>
        private static void BridgeGaps(RidgeNet net, RoofOutline outline, double reach)
        {
            int n = net.Points.Count;
            var degree = new int[n];
            var pairOf = new (int, int)[n];
            var piece = new int[n];
            for (int i = 0; i < n; i++) piece[i] = i;
            int Root(int x)
            {
                while (piece[x] != x) { piece[x] = piece[piece[x]]; x = piece[x]; }
                return x;
            }
            foreach (var (a, b, pair) in net.Segments)
            {
                degree[a]++;
                degree[b]++;
                pairOf[a] = pairOf[b] = pair;
                piece[Root(a)] = Root(b);
            }

            var ends = new List<int>();
            for (int v = 0; v < n; v++)
                if (degree[v] == 1) ends.Add(v);
            ends.Sort((u, v) => net.Points[u].X.CompareTo(net.Points[v].X));

            // Sweep along X: only ends within `reach` in X can be within `reach`.
            var candidates = new List<(double D, int U, int V)>();
            for (int i = 0; i < ends.Count; i++)
                for (int j = i + 1; j < ends.Count; j++)
                {
                    Point3 pu = net.Points[ends[i]], pv = net.Points[ends[j]];
                    if (pv.X - pu.X > reach) break;
                    double d = pu.DistanceXY(pv);
                    if (d <= reach && pairOf[ends[i]] == pairOf[ends[j]])
                        candidates.Add((d, Math.Min(ends[i], ends[j]), Math.Max(ends[i], ends[j])));
                }
            candidates.Sort();

            var used = new bool[n];
            foreach (var (_, u, v) in candidates)
            {
                if (used[u] || used[v]) continue;
                int ru = Root(u), rv = Root(v);
                if (ru == rv || !outline.IsVisible(net.Points[u], net.Points[v])) continue;
                net.AddSegment(u, v, pairOf[u]);
                used[u] = used[v] = true;
                piece[ru] = rv;
            }
        }

        /// <summary>
        /// Walks each run between nodes that are not simple links (ends and branch points), then each
        /// remaining closed loop, and straightens it. Returns the lines as point pairs of the net.
        /// </summary>
        private static List<(int A, int B)> Straighten(RidgeNet net)
        {
            var lines = new List<(int A, int B)>();
            int n = net.Points.Count;
            var adj = new List<(int To, int Seg)>[n];
            for (int s = 0; s < net.Segments.Count; s++)
            {
                var (a, b, _) = net.Segments[s];
                (adj[a] ??= new List<(int, int)>()).Add((b, s));
                (adj[b] ??= new List<(int, int)>()).Add((a, s));
            }

            var used = new bool[net.Segments.Count];
            void Walk(int start, int to, int seg)
            {
                used[seg] = true;
                var run = new List<int> { start, to };
                int cur = to;
                while (adj[cur].Count == 2)
                {
                    var next = adj[cur][0].Seg == seg ? adj[cur][1] : adj[cur][0];
                    if (used[next.Seg]) break;   // back at the start of a closed loop
                    used[next.Seg] = true;
                    run.Add(next.To);
                    seg = next.Seg;
                    cur = next.To;
                }

                var pts = new List<Point3>(run.Count);
                foreach (int v in run) pts.Add(net.Points[v]);
                var ids = new List<int>();
                foreach (int k in Simplify(pts, StraightenToleranceFt)) ids.Add(run[k]);

                // A closed loop starts wherever the walk did; drop that point when it sits on a straight side.
                if (ids.Count >= 5 && ids[0] == ids[ids.Count - 1] &&
                    DistanceToSegment(net.Points[ids[0]], net.Points[ids[ids.Count - 2]], net.Points[ids[1]]) <= StraightenToleranceFt)
                {
                    ids.RemoveAt(0);
                    ids[ids.Count - 1] = ids[0];
                }

                for (int i = 1; i < ids.Count; i++)
                    if (ids[i - 1] != ids[i])   // a closed loop smaller than the tolerance
                        lines.Add((ids[i - 1], ids[i]));
            }

            // Ends and branch points first, so only closed loops start part-way along a run.
            for (int v = 0; v < n; v++)
                if (adj[v] != null && adj[v].Count != 2)
                    foreach (var (to, seg) in adj[v])
                        if (!used[seg]) Walk(v, to, seg);
            for (int v = 0; v < n; v++)
                if (adj[v] != null)
                    foreach (var (to, seg) in adj[v])
                        if (!used[seg]) Walk(v, to, seg);
            return lines;
        }

        /// <summary>
        /// Douglas–Peucker: indices of the points kept so that every dropped point lies within
        /// <paramref name="tol"/> of the line between its kept neighbours. The first and last point
        /// are always kept. Iterative, so long runs cannot overflow the stack.
        /// </summary>
        private static List<int> Simplify(IReadOnlyList<Point3> pts, double tol)
        {
            int n = pts.Count;
            var keep = new bool[n];
            keep[0] = keep[n - 1] = true;
            var todo = new Stack<(int First, int Last)>();
            todo.Push((0, n - 1));
            while (todo.Count > 0)
            {
                var (first, last) = todo.Pop();
                int iMax = -1;
                double dMax = 0;
                for (int i = first + 1; i < last; i++)
                {
                    double d = DistanceToSegment(pts[i], pts[first], pts[last]);
                    if (d > dMax) { dMax = d; iMax = i; }
                }
                if (iMax < 0 || dMax <= tol) continue;
                keep[iMax] = true;
                todo.Push((first, iMax));
                todo.Push((iMax, last));
            }

            var kept = new List<int>();
            for (int i = 0; i < n; i++)
                if (keep[i]) kept.Add(i);
            return kept;
        }

        private static double DistanceToSegment(Point3 p, Point3 a, Point3 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2)) : 0;
            double ex = p.X - a.X - t * dx, ey = p.Y - a.Y - t * dy;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        // ------------------------------------------------------------------
        // Step 6: reach the roof edge
        // ------------------------------------------------------------------

        /// <summary>
        /// Moves every line end that no other line uses on along its line, away from the other end,
        /// to the roof edge when <see cref="Reach"/> allows it. A junction always has three or more
        /// lines, so it is never moved.
        /// </summary>
        private static void ReachEdge(
            List<(int A, int B)> lines, Point3[] at, RoofOutline outline, GeodesicField field, double maxDistance, double step)
        {
            var uses = new Dictionary<int, int>();
            foreach (var (a, b) in lines)
            {
                uses[a] = uses.TryGetValue(a, out int ua) ? ua + 1 : 1;
                uses[b] = uses.TryGetValue(b, out int ub) ? ub + 1 : 1;
            }

            // Directions come from the unmoved ends, so a line free at both ends keeps its direction.
            var moves = new List<(int Node, Point3 To)>();
            foreach (var (a, b) in lines)
            {
                if (uses[a] == 1 && Reach(at[a], at[b], outline, field, maxDistance, step) is Point3 ta) moves.Add((a, ta));
                if (uses[b] == 1 && Reach(at[b], at[a], outline, field, maxDistance, step) is Point3 tb) moves.Add((b, tb));
            }
            foreach (var (node, to) in moves) at[node] = to;
        }

        /// <summary>
        /// Where an open end at <paramref name="end"/> (line from <paramref name="other"/>) reaches the
        /// outline, or null: the end must lie within <see cref="EndReachFactor"/> × step of the outline
        /// (and not on it), the first edge along the line must be within <see cref="MaxReachFactor"/> ×
        /// step, and that edge point must still be sloped (within the drainage distance of a drain).
        /// </summary>
        private static Point3? Reach(
            Point3 end, Point3 other, RoofOutline outline, GeodesicField field, double maxDistance, double step)
        {
            outline.ClosestPoint(end, out double gap);
            if (gap > EndReachFactor * step || gap <= RoofOutline.OnEdgeTolerance) return null;

            // The first edge the ray meets, so the way there stays on the roof.
            Point3? hit = outline.RayHit(end, end.X - other.X, end.Y - other.Y, MaxReachFactor * step);
            if (hit == null) return null;
            if (field.Nearest(hit.Value, out double d) < 0 || !(d <= maxDistance)) return null;
            return hit;
        }

        // ------------------------------------------------------------------
        // Grid and segment store
        // ------------------------------------------------------------------

        /// <summary>Step 2: the nodes of a square grid over the outline's bounding box, each with its group.</summary>
        private sealed class LabelGrid
        {
            private readonly double _x0, _y0;
            private readonly int[] _label;

            public LabelGrid(RoofOutline outline, GeodesicField field, double maxDistance, CancellationToken cancel)
            {
                _x0 = outline.MinX;
                _y0 = outline.MinY;
                double w = outline.MaxX - outline.MinX, d = outline.MaxY - outline.MinY;
                Step = Math.Max(Math.Sqrt(w * d / TargetGridNodes), MinGridStepFt);
                Nx = Math.Max(1, (int)Math.Ceiling(w / Step));
                Ny = Math.Max(1, (int)Math.Ceiling(d / Step));

                _label = new int[(Nx + 1) * (Ny + 1)];
                int visited = 0;
                for (int j = 0; j <= Ny; j++)
                {
                    cancel.ThrowIfCancellationRequested();
                    for (int i = 0; i <= Nx; i++)
                    {
                        if (++visited % CancelCheckInterval == 0) cancel.ThrowIfCancellationRequested();
                        int g = field.Nearest(Node(i, j), out double dist);   // -1 off the roof
                        _label[j * (Nx + 1) + i] = g >= 0 && dist <= maxDistance ? g : -1;
                    }
                }
            }

            /// <summary>Grid step (cell side) in feet.</summary>
            public double Step { get; }

            /// <summary>Cells along X (nodes: Nx + 1).</summary>
            public int Nx { get; }

            /// <summary>Cells along Y (nodes: Ny + 1).</summary>
            public int Ny { get; }

            public Point3 Node(int i, int j) => new Point3(_x0 + i * Step, _y0 + j * Step, 0);

            public int Label(int i, int j) => _label[j * (Nx + 1) + i];
        }

        /// <summary>Ridge points and the segments between them, each tagged with its group pair.</summary>
        private sealed class RidgeNet
        {
            public List<Point3> Points { get; } = new List<Point3>();

            public List<(int A, int B, (int, int) Pair)> Segments { get; private set; } =
                new List<(int, int, (int, int))>();

            public int AddPoint(Point3 p)
            {
                Points.Add(p);
                return Points.Count - 1;
            }

            public void AddSegment(int a, int b, (int, int) pair) => Segments.Add((a, b, pair));

            /// <summary>
            /// Drops segments shorter than <see cref="ZeroLengthFt"/>, joining their ends into one
            /// point (at their mean), so the runs through them stay connected. Repeated segments go too.
            /// </summary>
            public void DropZeroLength()
            {
                int n = Points.Count;
                var parent = new int[n];
                for (int i = 0; i < n; i++) parent[i] = i;
                int Root(int x)
                {
                    while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                    return x;
                }

                bool merged = false;
                foreach (var (a, b, _) in Segments)
                    if (Points[a].DistanceXY(Points[b]) < ZeroLengthFt)
                    {
                        int ra = Root(a), rb = Root(b);
                        if (ra != rb) { parent[Math.Max(ra, rb)] = Math.Min(ra, rb); merged = true; }
                    }

                if (merged)
                {
                    var sx = new double[n];
                    var sy = new double[n];
                    var count = new int[n];
                    for (int i = 0; i < n; i++)
                    {
                        int r = Root(i);
                        sx[r] += Points[i].X;
                        sy[r] += Points[i].Y;
                        count[r]++;
                    }
                    for (int i = 0; i < n; i++)
                        if (count[i] > 1) Points[i] = new Point3(sx[i] / count[i], sy[i] / count[i], 0);
                }

                var kept = new List<(int, int, (int, int))>(Segments.Count);
                var seen = new HashSet<(int, int)>();
                foreach (var (a, b, pair) in Segments)
                {
                    int ra = Root(a), rb = Root(b);
                    if (ra == rb || !seen.Add((Math.Min(ra, rb), Math.Max(ra, rb)))) continue;
                    kept.Add((ra, rb, pair));
                }
                Segments = kept;
            }

            /// <summary>Number of different group pairs among the segments.</summary>
            public int PairCount()
            {
                var pairs = new HashSet<(int, int)>();
                foreach (var (_, _, pair) in Segments) pairs.Add(pair);
                return pairs.Count;
            }
        }
    }
}
