using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Models;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Services
{
    /// <summary>
    /// Computes intersection points between clipped Voronoi ridge edges and interior
    /// floor void/opening loops.
    ///
    /// For each clipped ridge edge, the edge is extended in both directions out to the
    /// outer floor boundary, then intersected against every edge of every inner loop.
    /// Each intersection point is added to <see cref="VoronoiRidgeResult.ShapePoints"/>,
    /// de-duplicated against points already present.
    ///
    /// Pure 2D geometry — no Revit document access, no transaction required.
    /// </summary>
    public class InnerLoopIntersectionService
    {
        /// <summary>
        /// Tolerance used when de-duplicating a newly found intersection point against
        /// points already present in <see cref="VoronoiRidgeResult.ShapePoints"/>
        /// (Revit internal units — feet). Default: 5 mm.
        /// </summary>
        public double DedupTolerance { get; set; } = 5.0 / 304.8;

        /// <summary>
        /// Finds every intersection between <paramref name="result"/>'s clipped ridge
        /// edges (extended to the outer boundary) and its interior loops, adding each
        /// new point to <see cref="VoronoiRidgeResult.ShapePoints"/>.
        /// </summary>
        /// <param name="result">Pipeline result; <c>ClippedEdges</c> and <c>InnerLoops</c> must already be populated.</param>
        /// <param name="outerBoundary">The floor's outer boundary polygon (Z=0), used to bound the edge extension.</param>
        /// <returns>The number of new shape points added.</returns>
        public int ComputeInnerLoopIntersections(VoronoiRidgeResult result, List<XYZ> outerBoundary)
        {
            int added = 0;

            foreach (var edge in result.ClippedEdges)
            {
                // Direction vector of the ridge edge
                double dx = edge.End.X - edge.Start.X;
                double dy = edge.End.Y - edge.Start.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                double ux = dx / len, uy = dy / len;

                // Extend in both directions until the ray exits the outer boundary.
                // Use the diagonal of the BBox as a safe max extension.
                double bboxDiag = BBoxDiagonal(outerBoundary);
                XYZ extStart = new XYZ(edge.Start.X - ux * bboxDiag,
                                       edge.Start.Y - uy * bboxDiag, 0);
                XYZ extEnd = new XYZ(edge.End.X + ux * bboxDiag,
                                       edge.End.Y + uy * bboxDiag, 0);

                // Clip the extended ray to the outer boundary to get the true endpoints
                var clippedRay = ClipLineToBoundary(extStart, extEnd, outerBoundary);
                if (clippedRay == null) continue;

                XYZ rayA = clippedRay.Value.a;
                XYZ rayB = clippedRay.Value.b;

                // Intersect clipped ray with every edge of every inner loop
                foreach (var loop in result.InnerLoops)
                {
                    int n = loop.Count;
                    for (int i = 0; i < n; i++)
                    {
                        XYZ la = loop[i];
                        XYZ lb = loop[(i + 1) % n];

                        if (!SegmentIntersect2D(rayA, rayB, la, lb, out XYZ ip)) continue;

                        // De-duplicate against existing shape points
                        bool isDup = result.ShapePoints.Any(p =>
                            Math.Abs(p.X - ip.X) < DedupTolerance &&
                            Math.Abs(p.Y - ip.Y) < DedupTolerance);

                        if (!isDup)
                        {
                            result.ShapePoints.Add(ip);
                            added++;
                        }
                    }
                }
            }

            return added;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static double BBoxDiagonal(List<XYZ> pts)
        {
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double w = maxX - minX, h = maxY - minY;
            return Math.Sqrt(w * w + h * h);
        }

        /// <summary>
        /// Clips an infinite line (defined by two points) to a polygon boundary.
        /// Returns the two boundary-crossing points, or null if the line doesn't
        /// cross the boundary at least twice.
        /// </summary>
        private static (XYZ a, XYZ b)? ClipLineToBoundary(XYZ p1, XYZ p2, List<XYZ> boundary)
        {
            var hits = new List<XYZ>();
            int n = boundary.Count;
            for (int i = 0; i < n; i++)
            {
                XYZ ba = boundary[i];
                XYZ bb = boundary[(i + 1) % n];
                if (SegmentIntersect2D(p1, p2, ba, bb, out XYZ ip))
                    hits.Add(ip);
            }
            if (hits.Count < 2) return null;
            // Return the two extreme hits along the ray direction
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            hits.Sort((a, b) => ((a.X - p1.X) * dx + (a.Y - p1.Y) * dy)
                .CompareTo((b.X - p1.X) * dx + (b.Y - p1.Y) * dy));
            return (hits.First(), hits.Last());
        }

        /// <summary>
        /// Segment-segment intersection (2D). Returns true and the intersection point
        /// when segments p→q and r→s cross.
        ///
        /// NOTE: this intentionally uses a looser bounds tolerance (±1e-9 on the
        /// parametric t/u values) than <see cref="FloorGeometry2D.TrySegmentIntersect"/>'s
        /// strict [0,1] bounds. The rays this method tests are themselves the result of
        /// a boundary-clip computed with floating point, so a hit landing fractionally
        /// outside [0,1] at a shared boundary vertex needs to still register — the
        /// strict version is correct for the original, un-extended Voronoi edges used
        /// elsewhere in the pipeline, but would intermittently miss valid crossings here.
        /// </summary>
        private static bool SegmentIntersect2D(XYZ p, XYZ q, XYZ r, XYZ s, out XYZ ip)
        {
            ip = XYZ.Zero;
            double dx1 = q.X - p.X, dy1 = q.Y - p.Y;
            double dx2 = s.X - r.X, dy2 = s.Y - r.Y;
            double denom = dx1 * dy2 - dy1 * dx2;
            if (Math.Abs(denom) < 1e-12) return false; // parallel

            double t = ((r.X - p.X) * dy2 - (r.Y - p.Y) * dx2) / denom;
            double u = ((r.X - p.X) * dy1 - (r.Y - p.Y) * dx1) / denom;

            if (t < -1e-9 || t > 1 + 1e-9) return false; // outside segment PQ
            if (u < -1e-9 || u > 1 + 1e-9) return false; // outside segment RS

            ip = new XYZ(p.X + t * dx1, p.Y + t * dy1, 0);
            return true;
        }
        // ======== ADD THIS OVERLOAD to InnerLoopIntersectionService.cs ========

        /// <summary>
        /// Overload that processes only a subset of inner loops.
        /// Useful when the user wants to skip selected loops (cross‑over behaviour).
        /// </summary>
        /// <param name="result">Pipeline result (must have ClippedEdges and InnerLoops).</param>
        /// <param name="outerBoundary">Outer floor boundary.</param>
        /// <param name="loopsToProcess">The inner loops that ridges should terminate on.</param>
        /// <returns>Number of new shape points added.</returns>
        public int ComputeInnerLoopIntersections(
            VoronoiRidgeResult result,
            List<XYZ> outerBoundary,
            List<List<XYZ>> loopsToProcess)
        {
            // Backup the original full list, replace with the filtered one, run, then restore.
            var originalLoops = result.InnerLoops;
            result.InnerLoops = loopsToProcess;
            int added = ComputeInnerLoopIntersections(result, outerBoundary);
            result.InnerLoops = originalLoops;
            return added;
        }
    }
}
