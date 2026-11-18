// Split Roof V001 — arc helpers shared by the analyzer, slope mapper and arc re-fit.
using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.SplitRoof.V001.Core.Models;

namespace Revit26_Plugin_Revised.SplitRoof.V001.Core.Services
{
    /// <summary>Circle/arc maths on plan points.</summary>
    public static class ArcGeometry
    {
        /// <summary>Largest angle between flattened arc points (radians, 5 degrees).</summary>
        public const double MaxStep = 5.0 * Math.PI / 180.0;

        /// <summary>Circle through three points; null when they are (nearly) collinear.</summary>
        public static (Pt Center, double Radius)? Circle(Pt a, Pt m, Pt b)
        {
            double d = 2 * (a.X * (m.Y - b.Y) + m.X * (b.Y - a.Y) + b.X * (a.Y - m.Y));
            if (Math.Abs(d) < 1e-12) return null;
            double a2 = a.X * a.X + a.Y * a.Y, m2 = m.X * m.X + m.Y * m.Y, b2 = b.X * b.X + b.Y * b.Y;
            double ux = (a2 * (m.Y - b.Y) + m2 * (b.Y - a.Y) + b2 * (a.Y - m.Y)) / d;
            double uy = (a2 * (b.X - m.X) + m2 * (a.X - b.X) + b2 * (m.X - a.X)) / d;
            var c = new Pt(ux, uy);
            return (c, Math.Sqrt((a.X - ux) * (a.X - ux) + (a.Y - uy) * (a.Y - uy)));
        }

        /// <summary>Points of the edge from A (inclusive) to B (exclusive), arcs flattened.</summary>
        public static List<Pt> Flatten(Edge e)
        {
            var pts = new List<Pt> { e.A };
            if (e.ArcMid is not Pt m) return pts;
            var circ = Circle(e.A, m, e.B);
            if (circ is null) return pts;
            var (c, r) = circ.Value;
            double a0 = Math.Atan2(e.A.Y - c.Y, e.A.X - c.X);
            double am = Math.Atan2(m.Y - c.Y, m.X - c.X);
            double a1 = Math.Atan2(e.B.Y - c.Y, e.B.X - c.X);
            double sweepM = Norm(am - a0), sweepB = Norm(a1 - a0);
            bool ccw = sweepM < sweepB;                       // mid lies between A and B going counter-clockwise
            double sweep = ccw ? sweepB : sweepB - 2 * Math.PI;
            if (Math.Abs(sweep) < 1e-12) sweep = ccw ? 2 * Math.PI : -2 * Math.PI;
            int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / MaxStep));
            for (int i = 1; i < n; i++)
            {
                double t = a0 + sweep * i / n;
                pts.Add(new Pt(c.X + r * Math.Cos(t), c.Y + r * Math.Sin(t)));
            }
            return pts;
        }

        /// <summary>Closed ring of points for a loop, arcs flattened.</summary>
        public static List<Pt> Ring(Loop loop)
        {
            var pts = new List<Pt>();
            foreach (var e in loop.Edges) pts.AddRange(Flatten(e));
            return pts;
        }

        /// <summary>Distance from a point to the segment p-q.</summary>
        public static double DistToSegment(Pt s, Pt p, Pt q)
        {
            double dx = q.X - p.X, dy = q.Y - p.Y, len2 = dx * dx + dy * dy;
            double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((s.X - p.X) * dx + (s.Y - p.Y) * dy) / len2));
            double px = p.X + t * dx - s.X, py = p.Y + t * dy - s.Y;
            return Math.Sqrt(px * px + py * py);
        }

        private static double Norm(double a)
        {
            a %= 2 * Math.PI;
            return a < 0 ? a + 2 * Math.PI : a;
        }
    }
}
