using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.IsolatedSection.V001.Core.Geometry
{
    /// <summary>
    /// A rectangle in plan, possibly rotated: the smallest one that encloses an element's footprint.
    /// <see cref="Along"/> runs along the long sides, so <see cref="Length"/> ≥ <see cref="Width"/>.
    /// </summary>
    public sealed class PlanRectangle
    {
        /// <summary>Creates a rectangle from its centre, long-side direction and size.</summary>
        public PlanRectangle(Vec2 center, Vec2 along, double length, double width)
        {
            Center = center;
            Along = along.Normalized();
            Length = length;
            Width = width;
        }

        /// <summary>Centre of the rectangle.</summary>
        public Vec2 Center { get; }

        /// <summary>Unit direction of the long sides.</summary>
        public Vec2 Along { get; }

        /// <summary>Unit direction of the short sides (<see cref="Along"/> turned 90° counter-clockwise).</summary>
        public Vec2 Across => Along.Perp();

        /// <summary>Size along <see cref="Along"/> (the long sides).</summary>
        public double Length { get; }

        /// <summary>Size along <see cref="Across"/> (the short sides).</summary>
        public double Width { get; }

        /// <summary>The four corners, counter-clockwise.</summary>
        public IReadOnlyList<Vec2> Corners()
        {
            Vec2 a = Along * (Length / 2), b = Across * (Width / 2);
            return new[] { Center - a - b, Center + a - b, Center + a + b, Center - a + b };
        }
    }

    /// <summary>Plan geometry helpers: convex hull and smallest enclosing rectangle.</summary>
    public static class PlanGeometry
    {
        private const double Eps = 1e-9;

        /// <summary>
        /// Convex hull of the points, counter-clockwise, without repeated or collinear points
        /// (Andrew's monotone chain). One or two distinct points come back as they are.
        /// </summary>
        public static IReadOnlyList<Vec2> ConvexHull(IEnumerable<Vec2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var pts = points
                .OrderBy(p => p.X).ThenBy(p => p.Y)
                .ToList();
            var distinct = new List<Vec2>(pts.Count);
            foreach (Vec2 p in pts)
                if (distinct.Count == 0 || (p - distinct[^1]).Length > Eps) distinct.Add(p);

            if (distinct.Count < 3) return distinct;

            var hull = new List<Vec2>(distinct.Count * 2);
            foreach (Vec2 p in distinct) AddHullPoint(hull, p, 2);
            int lowerCount = hull.Count;
            for (int i = distinct.Count - 2; i >= 0; i--) AddHullPoint(hull, distinct[i], lowerCount + 1);
            hull.RemoveAt(hull.Count - 1); // the first point closes the loop
            return hull;
        }

        // Pops points that would make a clockwise (or straight) turn while the chain has at least minCount points.
        private static void AddHullPoint(List<Vec2> hull, Vec2 p, int minCount)
        {
            while (hull.Count >= minCount && (hull[^1] - hull[^2]).Cross(p - hull[^2]) <= Eps)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        /// <summary>
        /// Smallest-area rectangle enclosing the points (rotating calipers over the hull edges).
        /// A single point gives a 0 × 0 rectangle along project X; collinear points a 0-wide one.
        /// </summary>
        public static PlanRectangle MinAreaRectangle(IEnumerable<Vec2> points)
        {
            IReadOnlyList<Vec2> hull = ConvexHull(points);
            if (hull.Count == 0) throw new InvalidOperationException("No points to enclose.");
            if (hull.Count == 1) return new PlanRectangle(hull[0], new Vec2(1, 0), 0, 0);

            PlanRectangle best = null;
            double bestArea = double.MaxValue;
            for (int i = 0; i < hull.Count; i++)
            {
                Vec2 edge = hull[(i + 1) % hull.Count] - hull[i];
                if (edge.Length < Eps) continue;

                PlanRectangle r = Fit(hull, edge.Normalized());
                double area = r.Length * r.Width;
                // Prefer the smaller area; on a tie keep the first (stable for rectangles and squares).
                if (area < bestArea - 1e-9)
                {
                    bestArea = area;
                    best = r;
                }
            }
            return best ?? Fit(hull, new Vec2(1, 0));
        }

        /// <summary>Rectangle with one side along <paramref name="dir"/> enclosing the points.</summary>
        public static PlanRectangle Fit(IReadOnlyList<Vec2> points, Vec2 dir)
        {
            Vec2 u = dir.Normalized(), v = u.Perp();
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (Vec2 p in points)
            {
                double pu = p.Dot(u), pv = p.Dot(v);
                minU = Math.Min(minU, pu); maxU = Math.Max(maxU, pu);
                minV = Math.Min(minV, pv); maxV = Math.Max(maxV, pv);
            }

            double lenU = maxU - minU, lenV = maxV - minV;
            Vec2 center = u * ((minU + maxU) / 2) + v * ((minV + maxV) / 2);
            return lenU >= lenV
                ? new PlanRectangle(center, u, lenU, lenV)
                : new PlanRectangle(center, v, lenV, lenU);
        }
    }
}
