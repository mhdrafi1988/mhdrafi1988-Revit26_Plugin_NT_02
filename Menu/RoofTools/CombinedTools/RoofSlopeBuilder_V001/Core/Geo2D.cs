// Pure 2D geometry helpers. No Revit references - can be unit tested on its own.
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Core
{
    /// <summary>Plan point or vector (feet).</summary>
    public struct P2
    {
        public readonly double X;
        public readonly double Y;

        public P2(double x, double y) { X = x; Y = y; }

        public static P2 operator +(P2 a, P2 b) { return new P2(a.X + b.X, a.Y + b.Y); }
        public static P2 operator -(P2 a, P2 b) { return new P2(a.X - b.X, a.Y - b.Y); }
        public static P2 operator *(P2 a, double k) { return new P2(a.X * k, a.Y * k); }

        public double Dot(P2 o) { return X * o.X + Y * o.Y; }
        public double Cross(P2 o) { return X * o.Y - Y * o.X; }
        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public double DistanceTo(P2 o) { return (this - o).Length; }
        public P2 Perp() { return new P2(-Y, X); }

        public P2 Normalized()
        {
            double l = Length;
            return l < 1e-12 ? new P2(0, 0) : new P2(X / l, Y / l);
        }

        public override string ToString() { return "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ")"; }
    }

    /// <summary>2D polygon and segment helpers.</summary>
    public static class Geo2D
    {
        /// <summary>Signed area (positive = counter-clockwise).</summary>
        public static double SignedArea(IList<P2> poly)
        {
            double a = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                P2 p = poly[i], q = poly[(i + 1) % poly.Count];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a / 2.0;
        }

        public static double Area(IList<P2> poly) { return Math.Abs(SignedArea(poly)); }

        public static P2 Centroid(IList<P2> poly)
        {
            double a = 0, cx = 0, cy = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                P2 p = poly[i], q = poly[(i + 1) % poly.Count];
                double w = p.X * q.Y - q.X * p.Y;
                a += w; cx += (p.X + q.X) * w; cy += (p.Y + q.Y) * w;
            }
            if (Math.Abs(a) < 1e-12)
            {
                double sx = 0, sy = 0;
                foreach (P2 p in poly) { sx += p.X; sy += p.Y; }
                return new P2(sx / poly.Count, sy / poly.Count);
            }
            return new P2(cx / (3 * a), cy / (3 * a));
        }

        /// <summary>Ray-casting point-in-polygon test (boundary result is undefined - use DistToPolygon for tolerance).</summary>
        public static bool Inside(IList<P2> poly, P2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                P2 a = poly[i], b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) &&
                    p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>Distance from p to segment ab. t is the clamped parameter of the foot along ab.</summary>
        public static double DistToSegment(P2 p, P2 a, P2 b, out P2 foot, out double tRaw)
        {
            P2 ab = b - a;
            double l2 = ab.Dot(ab);
            tRaw = l2 < 1e-18 ? 0 : (p - a).Dot(ab) / l2;
            double t = Math.Max(0, Math.Min(1, tRaw));
            foot = a + ab * t;
            return p.DistanceTo(foot);
        }

        public static double DistToSegment(P2 p, P2 a, P2 b)
        {
            P2 f; double t;
            return DistToSegment(p, a, b, out f, out t);
        }

        public static double DistToPolygon(IList<P2> poly, P2 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
                best = Math.Min(best, DistToSegment(p, poly[i], poly[(i + 1) % poly.Count]));
            return best;
        }

        /// <summary>Parameter t along ab (0..1) where ab crosses cd, or NaN when they do not cross.</summary>
        public static double SegmentCrossParam(P2 a, P2 b, P2 c, P2 d)
        {
            P2 r = b - a, s = d - c;
            double den = r.Cross(s);
            if (Math.Abs(den) < 1e-14) return double.NaN;
            double t = (c - a).Cross(s) / den;
            double u = (c - a).Cross(r) / den;
            if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) return double.NaN;
            return Math.Max(0, Math.Min(1, t));
        }
    }
}
