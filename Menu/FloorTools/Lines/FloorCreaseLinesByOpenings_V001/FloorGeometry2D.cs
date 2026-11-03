using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Models;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Services
{
    /// <summary>
    /// Shared 2D (XY-plane, Z ignored) geometry primitives used across the Voronoi
    /// ridge-line pipeline: point distance, point-in-polygon, segment intersection,
    /// nearest-drain-group lookup, and curve/edge-loop tessellation with polygon area.
    ///
    /// These were previously duplicated, with minor variations, across
    /// <see cref="DrainGroupingService"/>, <see cref="RidgeValidationService"/>,
    /// <see cref="VoronoiClippingService"/>, <see cref="VoronoiComputationService"/>,
    /// <see cref="RidgeCreationService"/>, <see cref="FloorBoundaryService"/>, and
    /// <see cref="InnerLoopService"/>. Centralising them here means every service
    /// shares one tolerance/precision behaviour instead of five independently
    /// drifting copies.
    /// </summary>
    public static class FloorGeometry2D
    {
        // ── Distance ──────────────────────────────────────────────────────────────

        /// <summary>2D (XY-plane) distance between two points; Z is ignored.</summary>
        public static double Dist2D(XYZ a, XYZ b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // ── Point-in-polygon ──────────────────────────────────────────────────────

        /// <summary>
        /// Ray-casting point-in-polygon test (2D, Z ignored). Works for any simple
        /// polygon, convex or concave (e.g. notched/L-shaped/amoeba floor footprints).
        /// </summary>
        public static bool PointInPolygon(XYZ p, List<XYZ> poly)
        {
            int n = poly.Count;
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i].X, yi = poly[i].Y;
                double xj = poly[j].X, yj = poly[j].Y;
                if (((yi > p.Y) != (yj > p.Y)) &&
                    (p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi))
                    inside = !inside;
            }
            return inside;
        }

        // ── Segment intersection ─────────────────────────────────────────────────

        /// <summary>
        /// Segment-segment intersection (2D). Returns true and the intersection point
        /// when segments a→b and c→d cross strictly within both segments' bounds
        /// (parametric t, u ∈ [0,1]).
        /// </summary>
        public static bool TrySegmentIntersect(XYZ a, XYZ b, XYZ c, XYZ d, out XYZ ip)
            => TrySegmentIntersectParametric(a, b, c, d, out _, out ip);

        /// <summary>
        /// Like <see cref="TrySegmentIntersect"/> but also returns the parametric t value
        /// along segment a→b. Used by callers that need to sort multiple crossings
        /// along one edge (e.g. clipping against a non-convex boundary).
        /// </summary>
        public static bool TrySegmentIntersectParametric(XYZ a, XYZ b, XYZ c, XYZ d, out double t, out XYZ ip)
        {
            t = 0; ip = null;
            double r1 = b.X - a.X, r2 = b.Y - a.Y;
            double s1 = d.X - c.X, s2 = d.Y - c.Y;
            double denom = r1 * s2 - r2 * s1;
            if (Math.Abs(denom) < 1e-10) return false; // parallel

            t = ((c.X - a.X) * s2 - (c.Y - a.Y) * s1) / denom;
            double u = ((c.X - a.X) * r2 - (c.Y - a.Y) * r1) / denom;

            if (t < 0 || t > 1 || u < 0 || u > 1) return false;

            ip = new XYZ(a.X + t * r1, a.Y + t * r2, 0);
            return true;
        }

        // ── Nearest drain groups ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the GroupIndex of the <paramref name="n"/> drain groups whose
        /// centroid is nearest to <paramref name="p"/> (2D distance), nearest first.
        /// </summary>
        public static List<int> FindNNearestGroups(XYZ p, List<DrainGroup> groups, int n)
        {
            var sorted = new SortedList<double, int>();
            foreach (var g in groups)
            {
                double d = Dist2D(p, g.Centroid);
                while (sorted.ContainsKey(d)) d += 1e-12; // avoid key collision
                sorted.Add(d, g.GroupIndex);
            }

            var result = new List<int>();
            int count = 0;
            foreach (var kv in sorted)
            {
                result.Add(kv.Value);
                if (++count >= n) break;
            }
            return result;
        }

        // ── Tessellation ──────────────────────────────────────────────────────────

        /// <summary>
        /// Tessellates a closed sketch-profile loop (lines kept as endpoints, curves
        /// tessellated) and flattens it to Z=0, de-duplicating adjacent points within
        /// snap tolerance.
        /// </summary>
        public static List<XYZ> TessellateLoop(CurveArray loop)
        {
            var pts = new List<XYZ>();
            foreach (Curve c in loop) AppendCurve(c, pts);
            return Flatten(pts);
        }

        /// <summary>
        /// Tessellates a closed solid-face edge loop (lines kept as endpoints, curves
        /// tessellated) and flattens it to Z=0, de-duplicating adjacent points within
        /// snap tolerance.
        /// </summary>
        public static List<XYZ> TessellateEdgeLoop(EdgeArray loop)
        {
            var pts = new List<XYZ>();
            foreach (Edge e in loop) AppendCurve(e.AsCurve(), pts);
            return Flatten(pts);
        }

        private static void AppendCurve(Curve c, List<XYZ> pts)
        {
            if (c is Line)
                pts.Add(c.GetEndPoint(0));
            else
            {
                IList<XYZ> tess = c.Tessellate();
                for (int i = 0; i < tess.Count - 1; i++) pts.Add(tess[i]);
            }
        }

        private static List<XYZ> Flatten(List<XYZ> pts)
        {
            const double snapTol = 1e-6;
            var flat = new List<XYZ>(pts.Count);
            XYZ prev = null;
            foreach (var p in pts)
            {
                var fp = new XYZ(p.X, p.Y, 0);
                if (prev != null && fp.DistanceTo(prev) < snapTol) continue;
                flat.Add(fp);
                prev = fp;
            }
            return flat;
        }

        // ── Polygon area ──────────────────────────────────────────────────────────

        /// <summary>Approximate planar area of a CurveArray loop, taken from its endpoint polygon (Shoelace formula).</summary>
        public static double ApproximateLoopArea(CurveArray loop)
        {
            var pts = new List<XYZ>();
            foreach (Curve c in loop) pts.Add(c.GetEndPoint(0));
            return ApproximatePolygonArea(Flatten(pts));
        }

        /// <summary>Shoelace-formula area of a flattened (Z=0) polygon point list.</summary>
        public static double ApproximatePolygonArea(List<XYZ> pts)
        {
            int n = pts.Count;
            if (n < 3) return 0;
            double area = 0;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                area += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(area) / 2.0;
        }
    }
}
