using System;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry
{
    /// <summary>Exact intersection tests used by the narrow phase of clash detection.</summary>
    public static class Intersect
    {
        private const double Epsilon = 1e-9;

        /// <summary>
        /// Möller–Trumbore test of a segment against a triangle. Triangle edges count as part of the
        /// triangle, so a segment through an edge shared by two triangles is reported by both; the
        /// caller merges such double hits.
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <param name="triangle">The triangle.</param>
        /// <param name="t">Where the segment crosses: 0 at its start, 1 at its end.</param>
        /// <param name="entering">True when the segment travels against the triangle's normal, that is, into the solid.</param>
        /// <returns>True when the segment crosses the triangle.</returns>
        public static bool SegmentTriangle(Segment3 segment, Triangle triangle, out double t, out bool entering)
        {
            t = 0;
            entering = false;

            Vec3 dir = segment.End - segment.Start;
            Vec3 edge1 = triangle.B - triangle.A;
            Vec3 edge2 = triangle.C - triangle.A;
            Vec3 p = dir.Cross(edge2);
            double det = edge1.Dot(p);

            // Scale the parallel test by the sizes involved so it works for small and large triangles alike.
            double scale = dir.Length * edge1.Length * edge2.Length;
            if (scale <= 0 || Math.Abs(det) <= 1e-12 * scale)
                return false;

            double inv = 1.0 / det;
            Vec3 s = segment.Start - triangle.A;
            double u = s.Dot(p) * inv;
            if (u < -Epsilon || u > 1 + Epsilon)
                return false;

            Vec3 q = s.Cross(edge1);
            double v = dir.Dot(q) * inv;
            if (v < -Epsilon || u + v > 1 + Epsilon)
                return false;

            double hit = edge2.Dot(q) * inv;
            if (hit < -Epsilon || hit > 1 + Epsilon)
                return false;

            t = Math.Min(1, Math.Max(0, hit));
            entering = dir.Dot(edge1.Cross(edge2)) < 0;
            return true;
        }
    }
}
