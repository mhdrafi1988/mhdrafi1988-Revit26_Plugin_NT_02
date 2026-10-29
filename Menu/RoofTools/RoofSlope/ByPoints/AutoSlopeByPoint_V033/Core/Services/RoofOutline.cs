// =======================================================
// File: RoofOutline.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services
// New in V033 (true ridge position). The roof's plan outline as closed
// polylines (outer boundary plus openings), in Revit internal feet, Z
// ignored. Answers the geometry questions the ridge finder needs: is a
// point on the roof, can two points see each other without leaving it,
// is a small cell clear of every edge, where does a ray meet an edge,
// and which corners bend the shortest path (reflex corners).
// Brute force over all edges with bounding-box rejects; roofs have tens
// of edges, so no spatial index. Revit-free (no Autodesk usings).
// =======================================================

using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services
{
    /// <summary>
    /// Plan outline of a roof: one or more closed loops (outer boundary and openings, any
    /// orientation). A point is on the roof by the even-odd rule over all loops.
    /// </summary>
    public sealed class RoofOutline
    {
        /// <summary>
        /// Distance in feet within which a point counts as lying on an edge. A point on an edge is
        /// inside the roof.
        /// </summary>
        public const double OnEdgeTolerance = 1e-6;

        /// <summary>Points closer than this (feet) are the same point when cleaning loops.</summary>
        private const double DuplicateTolerance = 1e-9;

        /// <summary>Rays only report edges farther than this (feet) from their start.</summary>
        private const double MinRayDistance = 1e-9;

        /// <summary>A corner is reflex when the turn cross product is below this (right turn).</summary>
        private const double ReflexCrossLimit = -1e-12;

        /// <summary>Two directions count as parallel when |cross| ≤ this × the product of lengths.</summary>
        private const double ParallelLimit = 1e-12;

        /// <summary>One loop edge with its bounding box.</summary>
        private readonly struct Edge
        {
            public readonly double Ax, Ay, Bx, By;
            public readonly double MinX, MinY, MaxX, MaxY;

            public Edge(Point3 a, Point3 b)
            {
                Ax = a.X; Ay = a.Y; Bx = b.X; By = b.Y;
                MinX = Math.Min(Ax, Bx); MaxX = Math.Max(Ax, Bx);
                MinY = Math.Min(Ay, By); MaxY = Math.Max(Ay, By);
            }
        }

        private readonly List<IReadOnlyList<Point3>> _loops = new List<IReadOnlyList<Point3>>();
        private readonly List<Point3> _pivots = new List<Point3>();
        private readonly Edge[] _edges;

        /// <summary>
        /// Builds the outline from closed plan polylines. A repeated closing point and consecutive
        /// duplicates (&lt; 1e-9 ft) are dropped, loops with fewer than 3 points are ignored, Z is
        /// ignored.
        /// </summary>
        /// <param name="loops">Closed loops: the outer boundary and any openings, in any orientation.</param>
        public RoofOutline(IEnumerable<IReadOnlyList<Point3>> loops)
        {
            if (loops == null) throw new ArgumentNullException(nameof(loops));

            var edges = new List<Edge>();
            foreach (var raw in loops)
            {
                var pts = Clean(raw);
                if (pts.Count < 3) continue;

                _loops.Add(pts.AsReadOnly());
                for (int i = 0; i < pts.Count; i++)
                    edges.Add(new Edge(pts[i], pts[(i + 1) % pts.Count]));
            }
            _edges = edges.ToArray();

            if (_edges.Length > 0)
            {
                MinX = double.MaxValue; MinY = double.MaxValue;
                MaxX = double.MinValue; MaxY = double.MinValue;
                foreach (var e in _edges)
                {
                    if (e.MinX < MinX) MinX = e.MinX;
                    if (e.MinY < MinY) MinY = e.MinY;
                    if (e.MaxX > MaxX) MaxX = e.MaxX;
                    if (e.MaxY > MaxY) MaxY = e.MaxY;
                }
            }

            FindPivots();
        }

        /// <summary>The cleaned loops (no repeated closing point, no consecutive duplicates).</summary>
        public IReadOnlyList<IReadOnlyList<Point3>> Loops => _loops;

        /// <summary>Smallest plan X of all loops (0 when there are no loops).</summary>
        public double MinX { get; }

        /// <summary>Smallest plan Y of all loops (0 when there are no loops).</summary>
        public double MinY { get; }

        /// <summary>Largest plan X of all loops (0 when there are no loops).</summary>
        public double MaxX { get; }

        /// <summary>Largest plan Y of all loops (0 when there are no loops).</summary>
        public double MaxY { get; }

        /// <summary>
        /// Reflex corners: loop vertices where the roof's inside angle is more than 180°. Every
        /// opening corner is one. Shortest paths across the roof only bend at these.
        /// </summary>
        public IReadOnlyList<Point3> Pivots => _pivots;

        // ------------------------------------------------------------------
        // Inside test
        // ------------------------------------------------------------------

        /// <summary>
        /// True when the point is on the roof: inside by the even-odd rule over all loops, or within
        /// <see cref="OnEdgeTolerance"/> of an edge. Z is ignored.
        /// </summary>
        /// <param name="p">Point to test.</param>
        public bool Contains(Point3 p) => ContainsXY(p.X, p.Y);

        private bool ContainsXY(double x, double y)
        {
            if (_edges.Length == 0) return false;
            if (x < MinX - OnEdgeTolerance || x > MaxX + OnEdgeTolerance ||
                y < MinY - OnEdgeTolerance || y > MaxY + OnEdgeTolerance)
                return false;

            const double tolSq = OnEdgeTolerance * OnEdgeTolerance;
            bool inside = false;
            foreach (var e in _edges)
            {
                if (PointSegmentDistSq(x, y, e.Ax, e.Ay, e.Bx, e.By) <= tolSq) return true;

                // Ray towards +X: count edges that straddle the point's Y and cross to its right.
                if ((e.Ay > y) != (e.By > y))
                {
                    double xAtY = e.Ax + (y - e.Ay) * (e.Bx - e.Ax) / (e.By - e.Ay);
                    if (xAtY > x) inside = !inside;
                }
            }
            return inside;
        }

        // ------------------------------------------------------------------
        // Line of sight
        // ------------------------------------------------------------------

        /// <summary>
        /// True when the straight segment from <paramref name="a"/> to <paramref name="b"/> stays on
        /// the roof. Touching or running along an edge is allowed; a proper crossing of any edge is
        /// not. Z is ignored.
        /// </summary>
        /// <param name="a">Segment start.</param>
        /// <param name="b">Segment end.</param>
        public bool IsVisible(Point3 a, Point3 b)
        {
            double rx = b.X - a.X, ry = b.Y - a.Y;
            double len = Math.Sqrt(rx * rx + ry * ry);
            if (len < DuplicateTolerance) return ContainsXY(a.X, a.Y);

            double loX = Math.Min(a.X, b.X) - OnEdgeTolerance, hiX = Math.Max(a.X, b.X) + OnEdgeTolerance;
            double loY = Math.Min(a.Y, b.Y) - OnEdgeTolerance, hiY = Math.Max(a.Y, b.Y) + OnEdgeTolerance;
            double tolT = OnEdgeTolerance / len;

            // Every parameter t (0 = a, 1 = b) where the segment touches an edge.
            var contacts = new List<double> { 0.0, 1.0 };
            foreach (var e in _edges)
            {
                if (e.MaxX < loX || e.MinX > hiX || e.MaxY < loY || e.MinY > hiY) continue;

                double sx = e.Bx - e.Ax, sy = e.By - e.Ay;
                double lenS = Math.Sqrt(sx * sx + sy * sy);
                double qx = e.Ax - a.X, qy = e.Ay - a.Y;
                double denom = rx * sy - ry * sx;

                if (Math.Abs(denom) <= ParallelLimit * len * lenS)
                {
                    // Parallel: only a collinear edge can touch. Both ends of the overlap are contacts.
                    double dA = Math.Abs(qx * ry - qy * rx) / len;
                    double ex = e.Bx - a.X, ey = e.By - a.Y;
                    double dB = Math.Abs(ex * ry - ey * rx) / len;
                    if (dA > OnEdgeTolerance || dB > OnEdgeTolerance) continue;

                    double tA = (qx * rx + qy * ry) / (len * len);
                    double tB = (ex * rx + ey * ry) / (len * len);
                    double lo = Math.Max(0.0, Math.Min(tA, tB));
                    double hi = Math.Min(1.0, Math.Max(tA, tB));
                    if (lo <= hi) { contacts.Add(lo); contacts.Add(hi); }
                    continue;
                }

                double t = (qx * sy - qy * sx) / denom;   // along a→b
                double u = (qx * ry - qy * rx) / denom;   // along the edge
                double tolU = OnEdgeTolerance / lenS;
                if (t < -tolT || t > 1.0 + tolT || u < -tolU || u > 1.0 + tolU) continue;

                // Interiors meet at one point: the segment leaves the roof (or enters an opening).
                if (t > tolT && t < 1.0 - tolT && u > tolU && u < 1.0 - tolU) return false;

                contacts.Add(Math.Min(1.0, Math.Max(0.0, t)));
            }

            // Between contacts the segment cannot cross an edge, so one midpoint per piece decides.
            contacts.Sort();
            for (int i = 1; i < contacts.Count; i++)
            {
                double t0 = contacts[i - 1], t1 = contacts[i];
                if (t1 <= t0) continue;
                double tm = 0.5 * (t0 + t1);
                if (!ContainsXY(a.X + tm * rx, a.Y + tm * ry)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Clear cell
        // ------------------------------------------------------------------

        /// <summary>
        /// True when the axis-aligned rectangle lies wholly on the roof and clear of the outline:
        /// all four corners inside, no loop edge crossing or touching a side, no loop vertex inside.
        /// The corners may be given in any order.
        /// </summary>
        /// <param name="x0">One X bound.</param>
        /// <param name="y0">One Y bound.</param>
        /// <param name="x1">The other X bound.</param>
        /// <param name="y1">The other Y bound.</param>
        public bool IsClearRect(double x0, double y0, double x1, double y1)
        {
            double lx = Math.Min(x0, x1), hx = Math.Max(x0, x1);
            double ly = Math.Min(y0, y1), hy = Math.Max(y0, y1);

            if (!ContainsXY(lx, ly) || !ContainsXY(hx, ly) || !ContainsXY(hx, hy) || !ContainsXY(lx, hy))
                return false;

            double loX = lx - OnEdgeTolerance, hiX = hx + OnEdgeTolerance;
            double loY = ly - OnEdgeTolerance, hiY = hy + OnEdgeTolerance;
            foreach (var e in _edges)
            {
                // Each vertex is the start of exactly one edge, so this visits every vertex once.
                if (e.Ax >= loX && e.Ax <= hiX && e.Ay >= loY && e.Ay <= hiY) return false;

                if (e.MaxX < loX || e.MinX > hiX || e.MaxY < loY || e.MinY > hiY) continue;

                if (SegmentsTouch(e.Ax, e.Ay, e.Bx, e.By, lx, ly, hx, ly) ||
                    SegmentsTouch(e.Ax, e.Ay, e.Bx, e.By, hx, ly, hx, hy) ||
                    SegmentsTouch(e.Ax, e.Ay, e.Bx, e.By, hx, hy, lx, hy) ||
                    SegmentsTouch(e.Ax, e.Ay, e.Bx, e.By, lx, hy, lx, ly))
                    return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Ray
        // ------------------------------------------------------------------

        /// <summary>
        /// First point where the ray from <paramref name="from"/> meets a loop edge at a distance in
        /// (1e-9, <paramref name="maxLength"/>] feet. Edges parallel to the ray are skipped: a ray
        /// running along an edge meets the next non-parallel edge at that edge's end. The result
        /// has the Z of <paramref name="from"/>.
        /// </summary>
        /// <param name="from">Ray start.</param>
        /// <param name="dirX">Ray direction X (need not be unit length).</param>
        /// <param name="dirY">Ray direction Y (need not be unit length).</param>
        /// <param name="maxLength">Farthest distance to look, in feet.</param>
        /// <returns>The nearest hit, or null when there is none (or the direction is zero).</returns>
        public Point3? RayHit(Point3 from, double dirX, double dirY, double maxLength)
        {
            double dl = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (dl < DuplicateTolerance || maxLength <= MinRayDistance) return null;
            double rx = dirX / dl, ry = dirY / dl;

            double best = double.MaxValue;
            foreach (var e in _edges)
            {
                double sx = e.Bx - e.Ax, sy = e.By - e.Ay;
                double lenS = Math.Sqrt(sx * sx + sy * sy);
                double denom = rx * sy - ry * sx;
                if (Math.Abs(denom) <= ParallelLimit * lenS) continue;

                double qx = e.Ax - from.X, qy = e.Ay - from.Y;
                double s = (qx * sy - qy * sx) / denom;   // distance along the ray
                double u = (qx * ry - qy * rx) / denom;   // along the edge
                double tolU = OnEdgeTolerance / lenS;
                if (u < -tolU || u > 1.0 + tolU) continue;
                if (s <= MinRayDistance || s > maxLength) continue;

                if (s < best) best = s;
            }

            if (best == double.MaxValue) return null;
            return new Point3(from.X + best * rx, from.Y + best * ry, from.Z);
        }

        // ------------------------------------------------------------------
        // Reflex corners
        // ------------------------------------------------------------------

        /// <summary>
        /// Winds every loop so the roof is on its left, by nesting depth: a loop inside an even
        /// number of other loops (an outer part, or an island inside an opening) is wound
        /// counter-clockwise, one inside an odd number (an opening) clockwise. A right turn is then
        /// a reflex corner, whatever the number of outer parts and openings.
        /// </summary>
        private void FindPivots()
        {
            if (_loops.Count == 0) return;

            for (int i = 0; i < _loops.Count; i++)
            {
                var pts = new List<Point3>(_loops[i]);
                bool roofInside = NestingDepth(i) % 2 == 0;
                bool counterClockwise = SignedArea(pts) > 0;
                if (counterClockwise != roofInside) pts.Reverse();

                int n = pts.Count;
                for (int j = 0; j < n; j++)
                {
                    Point3 prev = pts[(j + n - 1) % n], v = pts[j], next = pts[(j + 1) % n];
                    double cross = (v.X - prev.X) * (next.Y - v.Y) - (v.Y - prev.Y) * (next.X - v.X);
                    if (cross < ReflexCrossLimit) _pivots.Add(v);
                }
            }
        }

        private static double SignedArea(IReadOnlyList<Point3> pts)
        {
            double sum = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Point3 a = pts[i], b = pts[(i + 1) % pts.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return 0.5 * sum;
        }

        /// <summary>How many of the other loops enclose loop <paramref name="index"/>.</summary>
        private int NestingDepth(int index)
        {
            var loop = _loops[index];
            int depth = 0;
            for (int k = 0; k < _loops.Count; k++)
            {
                if (k == index) continue;

                // One point of the loop decides, as loops do not cross. Take one that is clear of
                // the other loop's edges; if every vertex touches them, try the edge midpoints.
                bool? inside = null;
                for (int i = 0; i < loop.Count && inside == null; i++)
                    inside = InsideLoop(_loops[k], loop[i].X, loop[i].Y);
                for (int i = 0; i < loop.Count && inside == null; i++)
                {
                    Point3 a = loop[i], b = loop[(i + 1) % loop.Count];
                    inside = InsideLoop(_loops[k], 0.5 * (a.X + b.X), 0.5 * (a.Y + b.Y));
                }
                if (inside == true) depth++;
            }
            return depth;
        }

        /// <summary>
        /// Even-odd point-in-polygon test against a single loop. Null when the point lies on the
        /// loop's edges, so the caller can pick another point.
        /// </summary>
        private static bool? InsideLoop(IReadOnlyList<Point3> loop, double x, double y)
        {
            const double tolSq = OnEdgeTolerance * OnEdgeTolerance;
            bool inside = false;
            for (int i = 0; i < loop.Count; i++)
            {
                Point3 a = loop[i], b = loop[(i + 1) % loop.Count];
                if (PointSegmentDistSq(x, y, a.X, a.Y, b.X, b.Y) <= tolSq) return null;
                if ((a.Y > y) != (b.Y > y) && a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y) > x)
                    inside = !inside;
            }
            return inside;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>Drops consecutive duplicates and a repeated closing point.</summary>
        private static List<Point3> Clean(IReadOnlyList<Point3> raw)
        {
            var pts = new List<Point3>();
            if (raw == null) return pts;

            foreach (var p in raw)
                if (pts.Count == 0 || p.DistanceXY(pts[pts.Count - 1]) >= DuplicateTolerance)
                    pts.Add(p);

            while (pts.Count > 1 && pts[pts.Count - 1].DistanceXY(pts[0]) < DuplicateTolerance)
                pts.RemoveAt(pts.Count - 1);

            return pts;
        }

        private static double PointSegmentDistSq(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double lenSq = dx * dx + dy * dy;
            double t = lenSq > 0 ? ((px - ax) * dx + (py - ay) * dy) / lenSq : 0.0;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double cx = ax + t * dx - px, cy = ay + t * dy - py;
            return cx * cx + cy * cy;
        }

        /// <summary>True when segment ab crosses segment cd or touches it within the tolerance.</summary>
        private static bool SegmentsTouch(
            double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            double d1 = Cross(cx, cy, dx, dy, ax, ay), d2 = Cross(cx, cy, dx, dy, bx, by);
            double d3 = Cross(ax, ay, bx, by, cx, cy), d4 = Cross(ax, ay, bx, by, dx, dy);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;

            const double tolSq = OnEdgeTolerance * OnEdgeTolerance;
            return PointSegmentDistSq(ax, ay, cx, cy, dx, dy) <= tolSq ||
                   PointSegmentDistSq(bx, by, cx, cy, dx, dy) <= tolSq ||
                   PointSegmentDistSq(cx, cy, ax, ay, bx, by) <= tolSq ||
                   PointSegmentDistSq(dx, dy, ax, ay, bx, by) <= tolSq;
        }

        /// <summary>Cross product of (b − a) and (p − a): positive when p is left of a→b.</summary>
        private static double Cross(double ax, double ay, double bx, double by, double px, double py) =>
            (bx - ax) * (py - ay) - (by - ay) * (px - ax);
    }
}
