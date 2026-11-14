using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Core.Geometry
{
    /// <summary>Measurements and checks on closed outlines (first point not repeated).</summary>
    public static class Polyline2D
    {
        /// <summary>Shoelace area in mm²; positive when counter-clockwise.</summary>
        public static double SignedAreaMm2(IReadOnlyList<Pt> pts)
        {
            if (pts == null || pts.Count < 3) return 0;
            double sum = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Pt a = pts[i], b = pts[(i + 1) % pts.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2;
        }

        /// <summary>Unsigned area in m².</summary>
        public static double AreaM2(IReadOnlyList<Pt> pts) => Math.Abs(SignedAreaMm2(pts)) / 1e6;

        /// <summary>Area-weighted centre; the average of the points for a degenerate outline.</summary>
        public static Pt Centroid(IReadOnlyList<Pt> pts)
        {
            if (pts == null || pts.Count == 0) return default;
            double a = SignedAreaMm2(pts);
            if (Math.Abs(a) < 1e-9)
            {
                double sx = 0, sy = 0;
                foreach (Pt p in pts) { sx += p.X; sy += p.Y; }
                return new Pt(sx / pts.Count, sy / pts.Count);
            }
            double cx = 0, cy = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Pt p = pts[i], q = pts[(i + 1) % pts.Count];
                double cross = p.X * q.Y - q.X * p.Y;
                cx += (p.X + q.X) * cross;
                cy += (p.Y + q.Y) * cross;
            }
            return new Pt(cx / (6 * a), cy / (6 * a));
        }

        /// <summary>True when two edges that are not neighbours cross or touch.</summary>
        public static bool SelfIntersects(IReadOnlyList<Pt> pts)
        {
            if (pts == null) return false;
            int n = pts.Count;
            if (n < 4) return false;
            for (int i = 0; i < n; i++)
            {
                Pt a = pts[i], b = pts[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue; // neighbours across the seam
                    if (SegmentsIntersect(a, b, pts[j], pts[(j + 1) % n])) return true;
                }
            }
            return false;
        }

        /// <summary>True when <paramref name="p"/> is inside the outline (even-odd rule).</summary>
        public static bool Contains(IReadOnlyList<Pt> pts, Pt p)
        {
            if (pts == null || pts.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                Pt a = pts[i], b = pts[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        private static double Cross(Pt o, Pt a, Pt b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        private static bool OnSegment(Pt a, Pt b, Pt p)
            => Math.Min(a.X, b.X) - 1e-9 <= p.X && p.X <= Math.Max(a.X, b.X) + 1e-9
            && Math.Min(a.Y, b.Y) - 1e-9 <= p.Y && p.Y <= Math.Max(a.Y, b.Y) + 1e-9;

        private static bool SegmentsIntersect(Pt a, Pt b, Pt c, Pt d)
        {
            double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            const double eps = 1e-6;
            if (Math.Abs(d1) < eps && OnSegment(c, d, a)) return true;
            if (Math.Abs(d2) < eps && OnSegment(c, d, b)) return true;
            if (Math.Abs(d3) < eps && OnSegment(a, b, c)) return true;
            if (Math.Abs(d4) < eps && OnSegment(a, b, d)) return true;
            return false;
        }
    }
}
