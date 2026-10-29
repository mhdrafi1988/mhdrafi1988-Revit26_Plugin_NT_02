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
//      1e-5 ft. When a third group is nearer at that point, its area cuts
//      the side between the corners: the side gets the two points where
//      that area begins and ends instead. Neighbouring cells share the
//      crossings on their common side. Cells cut by the outline count too,
//      so a ridge runs on up to an edge it meets at a shallow angle.
//   4. Segments: in a cell with two groups, two crossings are joined when
//      they see each other. Four (a saddle) are joined in two pairs, round
//      the corners whose group differs from the cell centre's. 0 or 1
//      crossings (a corner off the roof or beyond the drainage distance), or
//      3 (a side cut by the outline), give none.
//      Junctions: in a cell with three or more groups (at the corners or on
//      the sides), Newton's method finds the point where three of them are
//      equally far (on an open roof the circumcentre of their drains). A
//      point that lies in the cell, on the roof, with no other group nearer
//      is a junction joined to each of the cell's crossings it sees. A cell
//      next to it holds ridges that only pass through: the two crossings of
//      each pair are joined, any others go to that junction. A point that
//      lies off the roof means the ridges reach the edge separately; when
//      Newton does not converge, the junction is the mean of the crossings.
//      Junctions closer than h / 2 become one at their mean; a crossing that
//      two cells holding the same junction both join to lies between them
//      and is dropped (the ridge runs on to a further crossing).
//   5. Chain and straighten: zero-length segments are dropped (their ends
//      become one point). Free ends of the same group pair within 3 h that
//      see each other are bridged, closest first, so a ridge broken up along
//      an edge is one run again. Each run between ends, branch points,
//      junctions and changes of group pair (or a whole closed loop) is
//      straightened by Douglas–Peucker within 20 mm, so a straight ridge is
//      one line.
//   6. Reach the roof edge: a line end that no other line uses, is not a
//      junction and lies within 3 h of the outline is moved on, away from
//      the line's other end, to the first edge within 20 h, if that edge
//      point is still sloped by one of the line's own two groups. So an end
//      where the drainage distance stops the slope stays where it is, and an
//      end never runs on into a third group's area.
//   7. RidgeCount = the drain-group pairs with at least one segment.
//      Junctions = the junctions that end three or more segments, ascending
//      by (X, Y). Lines come out in the order the runs were walked.
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

        /// <summary>
        /// Points where three or more drain groups meet (on an open roof the Voronoi vertices of the
        /// drains): line ends shared by three or more lines, ascending by (X, Y), Z = 0.
        /// </summary>
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

        /// <summary>Step 4: junctions closer than this × the grid step become one junction at their mean.</summary>
        public const double JunctionMergeFactor = 0.5;

        /// <summary>Step 4: the junction's Newton search stops when a step is shorter than this (feet).</summary>
        private const double JunctionToleranceFt = 1e-7;

        /// <summary>Step 4: most Newton iterations per junction.</summary>
        private const int MaxJunctionIterations = 20;

        /// <summary>Step 4: the Newton Jacobian's central-difference step, as a fraction of the grid step.</summary>
        private const double JunctionJacobianFactor = 1e-3;

        /// <summary>
        /// Step 4: a junction counts as inside a cell up to this far (feet) outside it, so one on a side
        /// or node shared by cells is not lost between them.
        /// </summary>
        private const double OwnToleranceFt = 1e-6;

        /// <summary>Steps 3–4: another group is nearer only when it is nearer by more than this (feet).</summary>
        private const double IntruderToleranceFt = 1e-6;

        /// <summary>Step 4: the vertex search tries the triples of at most this many of a cell's groups.</summary>
        private const int MaxJunctionGroups = 4;

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

        /// <summary>Runs steps 1–7 (see the file header).</summary>
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

            // ── 3–4. Crossings, segments and junctions ─────────────────────
            var net = new RidgeNet();
            FindSegments(grid, field, input.Outline, net, cancel);
            net.MergeJunctions(JunctionMergeFactor * grid.Step);

            // ── 5. Chain and straighten ────────────────────────────────────
            net.DropZeroLength();
            BridgeGaps(net, input.Outline, BridgeFactor * grid.Step);
            result.RidgeCount = net.PairCount();
            List<(int A, int B, (int, int) Pair)> lines = Straighten(net);

            // ── 6. Reach the roof edge ─────────────────────────────────────
            Point3[] at = net.Points.ToArray();
            ReachEdge(lines, at, net.Junctions, input.Outline, field, input.MaxDistance, grid.Step);

            foreach (var (a, b, _) in lines)
                result.Lines.Add(new RidgeLine(at[a], at[b]));
            result.Junctions.AddRange(net.JunctionPoints());
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
        // Steps 3–4: crossings, segments and junctions
        // ------------------------------------------------------------------

        /// <summary>
        /// Walks the grid cells and adds their segments, junctions and junction legs to
        /// <paramref name="net"/>. Sides and segments that leave the roof (cross an outline edge) are
        /// left out.
        /// </summary>
        private static void FindSegments(LabelGrid grid, GeodesicField field, RoofOutline outline, RidgeNet net, CancellationToken cancel)
        {
            var crossingsOfSide = new Dictionary<long, (int, int)>();
            var corner = new int[4];             // labels, counter-clockwise from the lower-left corner
            var side = new int[4];               // first crossing on side k (corner k to corner k + 1), or -1
            var crossings = new List<int>(8);    // every crossing of the cell
            var groups = new List<int>(8);       // groups of the corners and the crossings, ascending
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
                    // One group: no ridge here.
                    if (CountGroups(corner, out int ga, out int gb) < 2) continue;

                    crossings.Clear();
                    int count = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        side[k] = -1;
                        int a = corner[k], b = corner[(k + 1) % 4];
                        if (a < 0 || b < 0 || a == b) continue;
                        var (first, second) = Crossings(grid, field, outline, net, crossingsOfSide, i, j, k);
                        side[k] = first;
                        if (first >= 0) { crossings.Add(first); count++; }
                        if (second >= 0) crossings.Add(second);
                    }

                    groups.Clear();
                    foreach (int g in corner)
                        if (g >= 0 && !groups.Contains(g)) groups.Add(g);
                    foreach (int c in crossings)
                    {
                        var (pa, pb) = net.PairOf(c);
                        if (!groups.Contains(pa)) groups.Add(pa);
                        if (!groups.Contains(pb)) groups.Add(pb);
                    }
                    groups.Sort();

                    // Three or more groups meet in or next to this cell.
                    if (groups.Count >= 3)
                    {
                        JunctionCell(grid, field, outline, net, i, j, groups, crossings);
                        continue;
                    }

                    // Two groups (ga and gb): each side holds at most one crossing (two need a third group).
                    var pair = (groups[0], groups[1]);
                    if (count == 2)
                    {
                        Join(net, outline, crossings[0], crossings[1], pair);
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

        /// <summary>
        /// Step 4 for a cell where three or more groups show, at its corners or on its sides. When a
        /// vertex (<see cref="FindVertex"/>) lies in the cell and the cell has three or more crossings,
        /// the vertex is a junction joined to every crossing it sees. When no vertex search converged
        /// (or ran off the roof) at all, the junction is the mean of three or more crossings, joined to
        /// all of them. Otherwise the ridges only pass through: the two crossings of each pair that has
        /// exactly two are joined, and the others are joined to the vertex next door, if one was found
        /// (it merges with the junction of the cell that holds it).
        /// </summary>
        private static void JunctionCell(
            LabelGrid grid, GeodesicField field, RoofOutline outline, RidgeNet net, int i, int j, List<int> groups, List<int> crossings)
        {
            if (crossings.Count == 0) return;
            Point3 lo = grid.Node(i, j), hi = grid.Node(i + 1, j + 1);
            Point3 mean = Mean(net, crossings);
            Point3? vertex = FindVertex(field, outline, groups, mean, lo, hi, grid.Step, out bool unsolved);

            if (vertex is Point3 v && crossings.Count >= 3 && InBox(v, lo, hi, OwnToleranceFt))
            {
                AddLegs(net, outline, v, crossings, own: true);
                return;
            }
            if (unsolved && crossings.Count >= 3)
            {
                if (outline.Contains(mean)) AddLegs(net, outline, mean, crossings, own: true);
                return;
            }

            var rest = new List<int>();
            foreach (int x in crossings)
            {
                var pair = net.PairOf(x);
                int other = -1, same = 0;
                foreach (int y in crossings)
                    if (net.PairOf(y) == pair) { same++; if (y != x) other = y; }
                if (same != 2) rest.Add(x);
                else if (x < other) Join(net, outline, x, other, pair);
            }
            if (rest.Count > 0 && vertex is Point3 near) AddLegs(net, outline, near, rest, own: false);
        }

        /// <summary>
        /// Adds a junction at <paramref name="at"/> and a leg from it to each crossing it sees, tagged
        /// with the crossing's pair. <paramref name="own"/>: the junction is this cell's own.
        /// </summary>
        private static void AddLegs(RidgeNet net, RoofOutline outline, Point3 at, List<int> crossings, bool own)
        {
            int id = net.AddJunction(at);
            foreach (int x in crossings)
                if (outline.IsVisible(at, net.Points[x])) net.AddLeg(id, x, net.PairOf(x), own);
        }

        /// <summary>
        /// A vertex of the drain groups' Voronoi diagram near the cell (inside it grown by one step on
        /// each side): a roof point where three of <paramref name="groups"/> are equally far and no other
        /// group is nearer. Tries the triples of the (at most four) lowest ids, lowest first, each by
        /// <see cref="SolveJunction"/> from <paramref name="start"/>. <paramref name="unsolved"/> is true
        /// when no search converged or ran off the roof, so nothing is known about a vertex.
        /// </summary>
        private static Point3? FindVertex(
            GeodesicField field, RoofOutline outline, List<int> groups, Point3 start, Point3 lo, Point3 hi, double step, out bool unsolved)
        {
            unsolved = true;
            int n = Math.Min(groups.Count, MaxJunctionGroups);
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                    for (int c = b + 1; c < n; c++)
                    {
                        Point3? v = SolveJunction(field, outline, groups[a], groups[b], groups[c], start, step, out bool offRoof);
                        if (v == null && !offRoof) continue;
                        unsolved = false;
                        if (v is Point3 p && InBox(p, lo, hi, step) && Intruder(field, p, groups[a], groups[b]) < 0) return p;
                    }
            return null;
        }

        /// <summary>
        /// Newton's method on F = (D(a) − D(b), D(a) − D(c)) from <paramref name="start"/>, with a
        /// central-difference Jacobian (step <paramref name="step"/> × 1e-3): at most 20 iterations,
        /// done when a step is under 1e-7 ft. Null when it does not converge; then
        /// <paramref name="offRoof"/> tells whether a step left the roof (the point is outside it).
        /// </summary>
        private static Point3? SolveJunction(
            GeodesicField field, RoofOutline outline, int a, int b, int c, Point3 start, double step, out bool offRoof)
        {
            offRoof = false;
            double e = step * JunctionJacobianFactor;
            (double, double) F(double x, double y)
            {
                var p = new Point3(x, y, 0);
                double da = field.DistanceTo(p, a);
                return (da - field.DistanceTo(p, b), da - field.DistanceTo(p, c));
            }

            double jx = start.X, jy = start.Y;
            for (int it = 0; it < MaxJunctionIterations; it++)
            {
                var (f1, f2) = F(jx, jy);
                var (xp1, xp2) = F(jx + e, jy);
                var (xm1, xm2) = F(jx - e, jy);
                var (yp1, yp2) = F(jx, jy + e);
                var (ym1, ym2) = F(jx, jy - e);
                double j11 = (xp1 - xm1) / (2 * e), j21 = (xp2 - xm2) / (2 * e);
                double j12 = (yp1 - ym1) / (2 * e), j22 = (yp2 - ym2) / (2 * e);
                double det = j11 * j22 - j12 * j21;
                double dx = -(j22 * f1 - j12 * f2) / det, dy = -(j11 * f2 - j21 * f1) / det;
                if (!double.IsFinite(dx) || !double.IsFinite(dy)) return null;   // no unique point, or a group unreachable

                jx += dx;
                jy += dy;
                var at = new Point3(jx, jy, 0);
                if (!outline.Contains(at)) { offRoof = true; return null; }
                if (Math.Sqrt(dx * dx + dy * dy) < JunctionToleranceFt) return at;
            }
            return null;
        }

        /// <summary>
        /// A group nearer to <paramref name="x"/> than both <paramref name="ga"/> and
        /// <paramref name="gb"/> (by more than 1e-6 ft), or -1 when there is none.
        /// </summary>
        private static int Intruder(GeodesicField field, Point3 x, int ga, int gb)
        {
            int g = field.Nearest(x, out double d);
            if (g < 0 || g == ga || g == gb) return -1;
            return d < Math.Min(field.DistanceTo(x, ga), field.DistanceTo(x, gb)) - IntruderToleranceFt ? g : -1;
        }

        private static bool InBox(Point3 p, Point3 lo, Point3 hi, double margin) =>
            p.X >= lo.X - margin && p.X <= hi.X + margin && p.Y >= lo.Y - margin && p.Y <= hi.Y + margin;

        private static Point3 Mean(RidgeNet net, List<int> ids)
        {
            double sx = 0, sy = 0;
            foreach (int id in ids) { sx += net.Points[id].X; sy += net.Points[id].Y; }
            return new Point3(sx / ids.Count, sy / ids.Count, 0);
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
        /// The crossings on side <paramref name="k"/> of cell (i, j), computed once per side so the two
        /// cells that share it share the points: none (-1, -1) when the side leaves the roof; else the
        /// point where its two end groups are equally far. When a third group is nearer at that point
        /// (its area cuts the side between the two corners), the side meets that area instead: the two
        /// points where it begins and ends, each kept only when no fourth group is nearer there.
        /// </summary>
        private static (int First, int Second) Crossings(
            LabelGrid grid, GeodesicField field, RoofOutline outline, RidgeNet net,
            Dictionary<long, (int, int)> crossingsOfSide, int i, int j, int k)
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
            if (crossingsOfSide.TryGetValue(key, out var ids)) return ids;

            int mi = dir == 0 ? ni + 1 : ni, mj = dir == 0 ? nj : nj + 1;
            Point3 p = grid.Node(ni, nj), q = grid.Node(mi, mj);
            int gp = grid.Label(ni, nj), gq = grid.Label(mi, mj);
            ids = (-1, -1);
            if (outline.IsVisible(p, q))
            {
                Point3 x = Bisect(field, p, gp, q, gq);
                int g = Intruder(field, x, gp, gq);
                if (g < 0)
                {
                    ids = (net.AddPoint(x, Pair(gp, gq)), -1);
                }
                else
                {
                    Point3 x1 = Bisect(field, p, gp, x, g), x2 = Bisect(field, x, g, q, gq);
                    int i1 = Intruder(field, x1, gp, g) < 0 ? net.AddPoint(x1, Pair(gp, g)) : -1;
                    int i2 = Intruder(field, x2, g, gq) < 0 ? net.AddPoint(x2, Pair(g, gq)) : -1;
                    ids = i1 >= 0 ? (i1, i2) : (i2, -1);
                }
            }
            crossingsOfSide[key] = ids;
            return ids;
        }

        private static (int, int) Pair(int a, int b) => (Math.Min(a, b), Math.Max(a, b));

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
        /// Joins free ends (points on exactly one segment, not junctions) of the same group pair that
        /// lie within <paramref name="reach"/> of each other and see each other: closest pair first,
        /// each end once, never two ends of the same piece (that would close it into a loop).
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
                if (degree[v] == 1 && !net.Junctions.Contains(v)) ends.Add(v);
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
        /// Walks each run between nodes that are not simple links (ends, branch points, junctions and
        /// points where the group pair changes), then each remaining closed loop, and straightens it.
        /// Returns the lines as point pairs of the net, each with its run's group pair.
        /// </summary>
        private static List<(int A, int B, (int, int) Pair)> Straighten(RidgeNet net)
        {
            var lines = new List<(int A, int B, (int, int) Pair)>();
            int n = net.Points.Count;
            var adj = new List<(int To, int Seg)>[n];
            for (int s = 0; s < net.Segments.Count; s++)
            {
                var (a, b, _) = net.Segments[s];
                (adj[a] ??= new List<(int, int)>()).Add((b, s));
                (adj[b] ??= new List<(int, int)>()).Add((a, s));
            }

            // A run goes on only through a link: two segments of the same pair, not a junction.
            bool IsLink(int v) =>
                adj[v].Count == 2 && !net.Junctions.Contains(v) &&
                net.Segments[adj[v][0].Seg].Pair == net.Segments[adj[v][1].Seg].Pair;

            var used = new bool[net.Segments.Count];
            void Walk(int start, int to, int seg)
            {
                used[seg] = true;
                var pair = net.Segments[seg].Pair;
                var run = new List<int> { start, to };
                int cur = to;
                while (IsLink(cur))
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
                        lines.Add((ids[i - 1], ids[i], pair));
            }

            // Ends, branch points and junctions first, so only closed loops start part-way along a run.
            for (int v = 0; v < n; v++)
                if (adj[v] != null && !IsLink(v))
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
        /// Moves every line end that no other line uses and that is not a junction on along its line,
        /// away from the other end, to the roof edge when <see cref="Reach"/> allows it.
        /// </summary>
        private static void ReachEdge(
            List<(int A, int B, (int, int) Pair)> lines, Point3[] at, ISet<int> junctions,
            RoofOutline outline, GeodesicField field, double maxDistance, double step)
        {
            var uses = new Dictionary<int, int>();
            foreach (var (a, b, _) in lines)
            {
                uses[a] = uses.TryGetValue(a, out int ua) ? ua + 1 : 1;
                uses[b] = uses.TryGetValue(b, out int ub) ? ub + 1 : 1;
            }

            // Directions come from the unmoved ends, so a line free at both ends keeps its direction.
            var moves = new List<(int Node, Point3 To)>();
            foreach (var (a, b, pair) in lines)
            {
                if (uses[a] == 1 && !junctions.Contains(a) &&
                    Reach(at[a], at[b], pair, outline, field, maxDistance, step) is Point3 ta) moves.Add((a, ta));
                if (uses[b] == 1 && !junctions.Contains(b) &&
                    Reach(at[b], at[a], pair, outline, field, maxDistance, step) is Point3 tb) moves.Add((b, tb));
            }
            foreach (var (node, to) in moves) at[node] = to;
        }

        /// <summary>
        /// Where an open end at <paramref name="end"/> (line from <paramref name="other"/>, between the
        /// groups of <paramref name="pair"/>) reaches the outline, or null: the end must lie within
        /// <see cref="EndReachFactor"/> × step of the outline (and not on it), the first edge along the
        /// line must be within <see cref="MaxReachFactor"/> × step, and that edge point must still be
        /// sloped by one of the line's own two groups (nearest to it, within the drainage distance).
        /// </summary>
        private static Point3? Reach(
            Point3 end, Point3 other, (int A, int B) pair, RoofOutline outline, GeodesicField field, double maxDistance, double step)
        {
            outline.ClosestPoint(end, out double gap);
            if (gap > EndReachFactor * step || gap <= RoofOutline.OnEdgeTolerance) return null;

            // The first edge the ray meets, so the way there stays on the roof.
            Point3? hit = outline.RayHit(end, end.X - other.X, end.Y - other.Y, MaxReachFactor * step);
            if (hit == null) return null;
            int g = field.Nearest(hit.Value, out double d);
            if ((g != pair.A && g != pair.B) || !(d <= maxDistance)) return null;
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

        /// <summary>
        /// Ridge points and the segments between them, each tagged with its group pair, and which
        /// points are junctions.
        /// </summary>
        private sealed class RidgeNet
        {
            /// <summary>
            /// Junction legs (junction, crossing, pair, whether the junction is the cell's own) until
            /// <see cref="MergeJunctions"/>.
            /// </summary>
            private readonly List<(int J, int X, (int, int) Pair, bool Own)> _legs = new List<(int, int, (int, int), bool)>();

            /// <summary>The group pair of each crossing; (-1, -1) for a junction.</summary>
            private readonly List<(int, int)> _pairs = new List<(int, int)>();

            public List<Point3> Points { get; } = new List<Point3>();

            public List<(int A, int B, (int, int) Pair)> Segments { get; private set; } =
                new List<(int, int, (int, int))>();

            /// <summary>The points that are junctions (where three or more groups meet).</summary>
            public HashSet<int> Junctions { get; private set; } = new HashSet<int>();

            public int AddPoint(Point3 p, (int, int) pair)
            {
                Points.Add(p);
                _pairs.Add(pair);
                return Points.Count - 1;
            }

            public (int, int) PairOf(int point) => _pairs[point];

            public int AddJunction(Point3 p)
            {
                int id = AddPoint(p, (-1, -1));
                Junctions.Add(id);
                return id;
            }

            public void AddSegment(int a, int b, (int, int) pair) => Segments.Add((a, b, pair));

            public void AddLeg(int junction, int crossing, (int, int) pair, bool own) =>
                _legs.Add((junction, crossing, pair, own));

            /// <summary>
            /// Junctions closer than <paramref name="radius"/> become one point at their mean (chains
            /// too), and the legs move with them into the segments. A crossing that two cells both join
            /// to their own junction, now one point, lies on the side between those cells: the ridge
            /// through it runs on to a further crossing that has its own leg, so both legs to it go.
            /// </summary>
            public void MergeJunctions(double radius)
            {
                var ids = new List<int>(Junctions);
                ids.Sort((u, v) => Points[u].X.CompareTo(Points[v].X));
                var parent = new Dictionary<int, int>();
                foreach (int id in ids) parent[id] = id;
                int Root(int x)
                {
                    while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                    return x;
                }

                // Sweep along X: only junctions within `radius` in X can be within `radius`.
                for (int i = 0; i < ids.Count; i++)
                    for (int k = i + 1; k < ids.Count; k++)
                    {
                        Point3 p = Points[ids[i]], q = Points[ids[k]];
                        if (q.X - p.X >= radius) break;
                        if (p.DistanceXY(q) >= radius) continue;
                        int ra = Root(ids[i]), rb = Root(ids[k]);
                        if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                    }

                var sum = new Dictionary<int, (double X, double Y, int N)>();
                foreach (int id in ids)
                {
                    int r = Root(id);
                    var (x, y, n) = sum.TryGetValue(r, out var t) ? t : (0.0, 0.0, 0);
                    sum[r] = (x + Points[id].X, y + Points[id].Y, n + 1);
                }
                foreach (var (r, (x, y, n)) in sum) Points[r] = new Point3(x / n, y / n, 0);
                Junctions = new HashSet<int>(sum.Keys);

                var owners = new Dictionary<(int, int), int>();
                foreach (var (jn, x, _, own) in _legs)
                    if (own) owners[(Root(jn), x)] = owners.TryGetValue((Root(jn), x), out int o) ? o + 1 : 1;
                var added = new HashSet<(int, int)>();
                foreach (var (jn, x, pair, _) in _legs)
                {
                    var leg = (Root(jn), x);
                    if (owners.TryGetValue(leg, out int o) && o >= 2) continue;
                    if (added.Add(leg)) Segments.Add((leg.Item1, x, pair));
                }
                _legs.Clear();
            }

            /// <summary>The junctions that end three or more segments, ascending by (X, Y).</summary>
            public List<Point3> JunctionPoints()
            {
                var degree = new Dictionary<int, int>();
                foreach (var (a, b, _) in Segments)
                {
                    degree[a] = degree.TryGetValue(a, out int da) ? da + 1 : 1;
                    degree[b] = degree.TryGetValue(b, out int db) ? db + 1 : 1;
                }
                var points = new List<Point3>();
                foreach (int j in Junctions)
                    if (degree.TryGetValue(j, out int d) && d >= 3) points.Add(Points[j]);
                points.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));
                return points;
            }

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

                var junctions = new HashSet<int>();
                foreach (int j in Junctions) junctions.Add(Root(j));
                Junctions = junctions;
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
