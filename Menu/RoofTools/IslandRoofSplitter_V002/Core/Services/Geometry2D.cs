// IslandRoofSplitter V002 — small 2D geometry helpers shared by the Core services. No Revit API.
#nullable enable
using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Services
{
    internal static class Geometry2D
    {
        /// <summary>Shoelace signed area: positive = counter-clockwise.</summary>
        public static double SignedArea(IReadOnlyList<Pt2> pts)
        {
            double a = 0;
            for (int i = 0, n = pts.Count; i < n; i++)
            {
                var p = pts[i];
                var q = pts[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5;
        }

        /// <summary>Even-odd point-in-polygon (boundary points are undefined; callers keep a tolerance).</summary>
        public static bool PointInPolygon(Pt2 p, IReadOnlyList<Pt2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    double x = (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (p.X < x) inside = !inside;
                }
            }
            return inside;
        }

        public static double DistancePointSegment(Pt2 p, Pt2 a, Pt2 b)
        {
            var ab = b - a;
            double len2 = Pt2.Dot(ab, ab);
            if (len2 < 1e-24) return p.DistanceTo(a);
            double t = Math.Clamp(Pt2.Dot(p - a, ab) / len2, 0, 1);
            return p.DistanceTo(a + ab * t);
        }

        public static double DistanceToLoop(Pt2 p, IReadOnlyList<Pt2> loop)
        {
            double best = double.MaxValue;
            for (int i = 0; i < loop.Count; i++)
                best = Math.Min(best, DistancePointSegment(p, loop[i], loop[(i + 1) % loop.Count]));
            return best;
        }

        /// <summary>
        /// Proper or touching intersection of segments ab and cd. Returns parameters along each (0..1).
        /// Parallel/collinear returns false (callers handle overlap separately).
        /// </summary>
        public static bool SegmentIntersection(Pt2 a, Pt2 b, Pt2 c, Pt2 d, double padA, double padC,
                                               out double t, out double u)
        {
            var r = b - a;
            var s = d - c;
            double denom = Pt2.Cross(r, s);
            t = u = 0;
            double rl = r.Length, sl = s.Length;
            if (rl < 1e-12 || sl < 1e-12) return false;
            if (Math.Abs(denom) <= 1e-12 * rl * sl) return false;
            var qp = c - a;
            t = Pt2.Cross(qp, s) / denom;
            u = Pt2.Cross(qp, r) / denom;
            double ta = padA / rl, tc = padC / sl;
            return t >= -ta && t <= 1 + ta && u >= -tc && u <= 1 + tc;
        }

        /// <summary>Circle through three points. False when (nearly) collinear.</summary>
        public static bool Circumcircle(Pt2 a, Pt2 b, Pt2 c, out Pt2 center, out double radius)
        {
            double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
            center = default;
            radius = 0;
            double scale = Math.Max(a.DistanceTo(b), b.DistanceTo(c));
            if (Math.Abs(d) < 1e-9 * scale * scale) return false;
            double a2 = a.X * a.X + a.Y * a.Y, b2 = b.X * b.X + b.Y * b.Y, c2 = c.X * c.X + c.Y * c.Y;
            center = new Pt2(
                (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d,
                (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
            radius = center.DistanceTo(a);
            return true;
        }

        public static double NormalizePositive(double ang)
        {
            const double TwoPi = 2 * Math.PI;
            ang %= TwoPi;
            if (ang < 0) ang += TwoPi;
            return ang;
        }

        /// <summary>True if any two non-adjacent edges of the closed loop intersect or touch.</summary>
        public static bool IsSelfIntersecting(IReadOnlyList<Pt2> loop, double tol, out Pt2 at)
        {
            at = default;
            int n = loop.Count;
            if (n < 4) return false;

            // sweep by min X to stay near O(n log n) for typical loops
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            double MinX(int i) => Math.Min(loop[i].X, loop[(i + 1) % n].X);
            double MaxX(int i) => Math.Max(loop[i].X, loop[(i + 1) % n].X);
            Array.Sort(order, (x, y) => MinX(x).CompareTo(MinX(y)));

            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                double maxX = MaxX(i) + tol;
                for (int oj = oi + 1; oj < n && MinX(order[oj]) <= maxX; oj++)
                {
                    int j = order[oj];
                    int diff = Math.Abs(i - j);
                    if (diff == 1 || diff == n - 1) continue; // adjacent edges share a vertex
                    Pt2 a = loop[i], b = loop[(i + 1) % n], c = loop[j], d = loop[(j + 1) % n];
                    if (SegmentIntersection(a, b, c, d, 0, 0, out double t, out _))
                    {
                        at = a + (b - a) * t;
                        return true;
                    }
                    // touching within tolerance (vertex on a non-adjacent edge)
                    if (DistancePointSegment(c, a, b) < tol) { at = c; return true; }
                    if (DistancePointSegment(a, c, d) < tol) { at = a; return true; }
                }
            }
            return false;
        }
    }

    /// <summary>Tolerance-based vertex index using a spatial hash.</summary>
    internal sealed class VertexIndex
    {
        private readonly double _tol;
        private readonly Dictionary<(long, long), List<int>> _cells = new();
        public List<Pt2> Points { get; } = new();

        public VertexIndex(double tol) { _tol = Math.Max(tol, 1e-9); }

        private (long, long) Cell(Pt2 p) => ((long)Math.Floor(p.X / _tol), (long)Math.Floor(p.Y / _tol));

        public int GetOrAdd(Pt2 p)
        {
            var (cx, cy) = Cell(p);
            int best = -1;
            double bestD = _tol;
            for (long dx = -1; dx <= 1; dx++)
            for (long dy = -1; dy <= 1; dy++)
            {
                if (!_cells.TryGetValue((cx + dx, cy + dy), out var list)) continue;
                foreach (int idx in list)
                {
                    double d = Points[idx].DistanceTo(p);
                    if (d <= bestD) { bestD = d; best = idx; }
                }
            }
            if (best >= 0) return best;

            Points.Add(p);
            int id = Points.Count - 1;
            if (!_cells.TryGetValue((cx, cy), out var cell)) _cells[(cx, cy)] = cell = new List<int>();
            cell.Add(id);
            return id;
        }
    }

    /// <summary>Minimal union-find.</summary>
    internal sealed class DisjointSet
    {
        private readonly int[] _p;
        public DisjointSet(int n) { _p = new int[n]; for (int i = 0; i < n; i++) _p[i] = i; }
        public int Find(int x) { while (_p[x] != x) { _p[x] = _p[_p[x]]; x = _p[x]; } return x; }
        public void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) _p[a] = b; }
    }
}
