using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Clipping of convex polygons and infinite lines by half-planes.</summary>
    public static class ConvexClipper
    {
        /// <summary>Sutherland–Hodgman clip of a polygon by one half-plane.</summary>
        public static Polygon2D Clip(Polygon2D polygon, HalfPlane halfPlane)
        {
            if (polygon.IsEmpty)
            {
                return Polygon2D.Empty;
            }

            if (halfPlane.IsDegenerate)
            {
                return halfPlane.Offset >= -GeometryTolerance.Epsilon ? polygon : Polygon2D.Empty;
            }

            var output = new List<Vec2>(polygon.Count + 2);
            IReadOnlyList<Vec2> v = polygon.Vertices;
            for (int i = 0; i < v.Count; i++)
            {
                Vec2 cur = v[i];
                Vec2 next = v[(i + 1) % v.Count];
                double dc = halfPlane.Evaluate(cur);
                double dn = halfPlane.Evaluate(next);
                bool curIn = dc >= -GeometryTolerance.Epsilon;
                bool nextIn = dn >= -GeometryTolerance.Epsilon;

                if (curIn)
                {
                    output.Add(cur);
                }

                if (curIn != nextIn)
                {
                    double t = dc / (dc - dn);
                    output.Add(cur + (next - cur) * t);
                }
            }

            return new Polygon2D(output).Cleaned(GeometryTolerance.Epsilon * 10);
        }

        public static Polygon2D ClipAll(Polygon2D polygon, IEnumerable<HalfPlane> halfPlanes)
        {
            Polygon2D result = polygon;
            foreach (HalfPlane hp in halfPlanes)
            {
                result = Clip(result, hp);
                if (result.IsEmpty)
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Cyrus–Beck clip of the infinite line origin + t·direction by a convex CCW polygon.
        /// Returns null when the line misses the polygon or only touches it at a point.
        /// </summary>
        public static Segment2D? ClipLine(Polygon2D convexCcw, Vec2 origin, Vec2 direction)
        {
            if (convexCcw.IsEmpty)
            {
                return null;
            }

            Vec2 dir = direction.Normalized();
            double tMin = double.NegativeInfinity;
            double tMax = double.PositiveInfinity;

            foreach (Segment2D edge in convexCcw.Edges)
            {
                Vec2 e = edge.End - edge.Start;
                if (e.Length < 1e-15)
                {
                    continue;
                }

                Vec2 inward = e.PerpLeft.Normalized();
                double d0 = inward.Dot(origin - edge.Start);
                double dd = inward.Dot(dir);

                if (Math.Abs(dd) < 1e-12)
                {
                    if (d0 < -GeometryTolerance.Epsilon)
                    {
                        return null;
                    }

                    continue;
                }

                double t = -d0 / dd;
                if (dd > 0)
                {
                    tMin = Math.Max(tMin, t);
                }
                else
                {
                    tMax = Math.Min(tMax, t);
                }
            }

            if (double.IsInfinity(tMin) || double.IsInfinity(tMax) || tMax - tMin <= GeometryTolerance.Epsilon)
            {
                return null;
            }

            return new Segment2D(origin + dir * tMin, origin + dir * tMax);
        }
    }
}
