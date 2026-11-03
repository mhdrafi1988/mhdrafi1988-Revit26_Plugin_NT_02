using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using MIConvexHull;
using Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Models;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Services
{
    public class VoronoiComputationService
    {
        private class Vertex2D : IVertex
        {
            public double[] Position { get; }
            public Vertex2D(double x, double y) => Position = new[] { x, y };
        }

        /// <summary>
        /// Computes Voronoi ridge edges and vertices.
        ///
        /// • 2 groups              → single perpendicular bisector midline.
        /// • 3+ groups, collinear  → N-1 perpendicular bisectors (Cases 2 &amp; 3).
        /// • 3+ groups, all circumcenters co-located within 10 mm → convex-hull bisector rays
        ///                           (Case 5 – rectangle / symmetric polygon).
        ///   This is detected in TWO places:
        ///     a) After a successful MIConvexHull run (near-symmetric but not degenerate).
        ///     b) Inside the catch block when MIConvexHull throws on a perfectly co-circular
        ///        layout (e.g. a perfect rectangle). In that case we manually fan-triangulate
        ///        from vertex[0] to compute circumcenters and check co-location before
        ///        falling back to the collinear bisector path.
        /// • 3+ groups, normal     → full Voronoi; null-adjacency half-edges emitted
        ///                           as outward rays using boundary-edge perpendicular bisector
        ///                           (Cases 4 &amp; 5 general).
        ///
        /// Clipping to the floor boundary is handled downstream by VoronoiClippingService.
        /// </summary>
        public void Compute(List<DrainGroup> groups, VoronoiRidgeResult result)
        {
            if (groups == null || groups.Count < 2)
                throw new InvalidOperationException(
                    $"At least 2 drain groups are required. Found {groups?.Count ?? 0} group(s).");

            var vertices = new List<Vertex2D>(groups.Count);
            foreach (var g in groups)
                vertices.Add(new Vertex2D(g.Centroid.X, g.Centroid.Y));

            // ── Special case: exactly 2 groups ───────────────────────────────────
            if (vertices.Count == 2)
            {
                GenerateMidlineForTwoGroups(groups, result);
                return;
            }

            // ── Collinear groups (Cases 2 & 3) ───────────────────────────────────
            if (AreCollinear(vertices))
            {
                GenerateMidlinesForCollinearGroups(groups, result);
                return;
            }

            // ── Full Voronoi ──────────────────────────────────────────────────────
            // MIConvexHull throws ConvexHullGenerationException for perfectly co-circular
            // inputs (e.g. 4 drains on a rectangle). That is NOT the same as collinear —
            // the correct fallback is GenerateConvexHullBisectorRays, not the collinear path.
            // We detect that case inside the catch block via manual fan-triangulation.
            // NOTE: VoronoiMesh is a static class — var is required, no generic type decl.
            try
            {
                var voronoi = VoronoiMesh.Create<Vertex2D, DefaultTriangulationCell<Vertex2D>>(vertices);

                // Collect all circumcenters
                var circumcenters = new List<XYZ>(voronoi.Vertices.Count());
                foreach (var cell in voronoi.Vertices)
                {
                    var cc = ComputeCircumcenter(
                        cell.Vertices[0].Position,
                        cell.Vertices[1].Position,
                        cell.Vertices[2].Position);
                    circumcenters.Add(new XYZ(cc.x, cc.y, 0));
                }

                // ── Case 5: degenerate – all circumcenters are co-located ─────────
                // (rectangle, square, or any point set whose Delaunay triangulation
                //  produces only co-incident circumcenters)
                if (AllColocated(circumcenters, 10.0 / 304.8))
                {
                    GenerateConvexHullBisectorRays(groups, circumcenters[0], result);
                    return;
                }

                // ── Normal Voronoi: build edges ───────────────────────────────────
                result.RawVoronoiVertices.Clear();
                foreach (var cc in circumcenters)
                    result.RawVoronoiVertices.Add(cc);

                result.RawVoronoiEdges.Clear();
                var seen = new HashSet<string>();
                var cellList = voronoi.Vertices.ToList();

                for (int ci = 0; ci < cellList.Count; ci++)
                {
                    var cell = cellList[ci];
                    var ccAPt = circumcenters[ci];

                    for (int k = 0; k < cell.Adjacency.Length; k++)
                    {
                        var adjacentCell = cell.Adjacency[k];

                        // ── Null adjacency = convex-hull boundary half-edge → outward ray ──
                        if (adjacentCell == null)
                        {
                            string halfKey = $"half_{ci}_{k}";
                            if (!seen.Add(halfKey)) continue;

                            // Correct outward direction:
                            // The boundary edge is the edge of this Delaunay triangle that
                            // has no neighbour — i.e. the edge opposite to vertex k.
                            // Its two endpoints are at indices (k+1)%3 and (k+2)%3.
                            // The outward ray is the perpendicular bisector of that edge,
                            // oriented away from the opposite vertex (vOpp at index k).
                            var vA = cell.Vertices[(k + 1) % 3];
                            var vB = cell.Vertices[(k + 2) % 3];
                            var vOpp = cell.Vertices[k];

                            double ex = vB.Position[0] - vA.Position[0];
                            double ey = vB.Position[1] - vA.Position[1];
                            double eLen = Math.Sqrt(ex * ex + ey * ey);
                            if (eLen < 1e-9) continue;

                            var perp1 = new XYZ(-ey / eLen, ex / eLen, 0);
                            var perp2 = new XYZ(ey / eLen, -ex / eLen, 0);

                            // edgeMid of the boundary edge
                            double midX = (vA.Position[0] + vB.Position[0]) / 2.0;
                            double midY = (vA.Position[1] + vB.Position[1]) / 2.0;

                            // outward = perp whose dot with (vOpp - edgeMid) is NEGATIVE
                            double toOppX = vOpp.Position[0] - midX;
                            double toOppY = vOpp.Position[1] - midY;
                            var rayDir = (perp1.X * toOppX + perp1.Y * toOppY < 0)
                                ? perp1 : perp2;

                            const double large = 1000.0;
                            result.RawVoronoiEdges.Add((ccAPt, ccAPt + rayDir * large));
                            continue;
                        }

                        // ── Interior edge between two adjacent triangles ──────────
                        int adjIdx = cellList.IndexOf(adjacentCell);
                        if (adjIdx < 0) continue;

                        int idA = ci, idB = adjIdx;
                        string key = idA < idB ? $"{idA}_{idB}" : $"{idB}_{idA}";
                        if (!seen.Add(key)) continue;

                        var ccBPt = circumcenters[adjIdx];

                        // Skip zero-length interior edges
                        if (FloorGeometry2D.Dist2D(ccAPt, ccBPt) < 1.0 / 304.8) continue;

                        result.RawVoronoiEdges.Add((ccAPt, ccBPt));
                    }
                }
            }
            catch (ConvexHullGenerationException)
            {
                // MIConvexHull throws for perfectly co-circular layouts (e.g. a rectangle).
                // This is NOT the same as collinear — check using manual fan-triangulation
                // before falling back to the collinear bisector path.
                var manualCircumcenters = ComputeCircumcentersManually(vertices);
                if (manualCircumcenters != null && AllColocated(manualCircumcenters, 10.0 / 304.8))
                {
                    // Co-circular layout (rectangle / symmetric polygon) — use convex hull rays
                    GenerateConvexHullBisectorRays(groups, manualCircumcenters[0], result);
                    return;
                }

                // Truly near-collinear degenerate layout that slipped past AreCollinear
                GenerateMidlinesForCollinearGroups(groups, result);
            }
        }

        // ── Co-location check ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when every circumcenter in the list is within <paramref name="snapTol"/>
        /// of the first one — i.e. all Delaunay triangles share a single circumcenter.
        /// snapTol is set to 10 mm to absorb floating-point spread in near-symmetric layouts
        /// such as rectangles and squares.
        /// </summary>
        private static bool AllColocated(List<XYZ> circumcenters, double snapTol)
        {
            if (circumcenters.Count == 0) return false;
            var first = circumcenters[0];
            return circumcenters.All(cc => FloorGeometry2D.Dist2D(cc, first) < snapTol);
        }

        // ── Manual circumcenter fan-triangulation ─────────────────────────────────

        /// <summary>
        /// Fan-triangulates the point set from vertex[0] and computes the circumcenter
        /// of each triangle. Used inside the ConvexHullGenerationException catch block
        /// to distinguish a perfectly co-circular layout (rectangle → convex hull rays)
        /// from a truly near-collinear degenerate layout (→ collinear bisectors).
        ///
        /// Returns null if fewer than 3 vertices are present (cannot triangulate).
        /// </summary>
        private static List<XYZ> ComputeCircumcentersManually(List<Vertex2D> vertices)
        {
            if (vertices.Count < 3) return null;

            var result = new List<XYZ>();
            for (int i = 1; i + 1 < vertices.Count; i++)
            {
                var cc = ComputeCircumcenter(
                    vertices[0].Position,
                    vertices[i].Position,
                    vertices[i + 1].Position);
                result.Add(new XYZ(cc.x, cc.y, 0));
            }
            return result;
        }

        // ── Convex-hull bisector rays (Case 5 – co-circular / rectangle) ──────────

        /// <summary>
        /// For degenerate cases (e.g. rectangle) where all circumcenters collapse to a
        /// single point: emits one outward perpendicular-bisector ray per convex-hull edge.
        /// This produces the correct Voronoi diagram analytically.
        ///
        /// For a 4-drain rectangle the result is 4 rays from the centre — one per side.
        /// </summary>
        private static void GenerateConvexHullBisectorRays(
            List<DrainGroup> groups,
            XYZ sharedCenter,
            VoronoiRidgeResult result)
        {
            result.RawVoronoiEdges.Clear();
            result.RawVoronoiVertices.Clear();
            result.RawVoronoiVertices.Add(sharedCenter);

            // Build convex hull of the drain centroids (gift-wrapping)
            var hull = ConvexHull2D(groups.Select(g => g.Centroid).ToList());
            int n = hull.Count;
            const double large = 1000.0;

            // Overall centroid of all sites (to orient rays outward)
            double cx = groups.Average(g => g.Centroid.X);
            double cy = groups.Average(g => g.Centroid.Y);

            for (int i = 0; i < n; i++)
            {
                var A = hull[i];
                var B = hull[(i + 1) % n];

                // Perpendicular bisector direction of edge A→B
                var ab = new XYZ(B.X - A.X, B.Y - A.Y, 0);
                double abLen = ab.GetLength();
                if (abLen < 1e-9) continue;

                // Two perpendicular candidates (±90°)
                var perp1 = new XYZ(-ab.Y / abLen, ab.X / abLen, 0);
                var perp2 = new XYZ(ab.Y / abLen, -ab.X / abLen, 0);

                // Choose the one pointing away from the overall centroid
                var edgeMid = new XYZ((A.X + B.X) / 2, (A.Y + B.Y) / 2, 0);
                var toCentroid = new XYZ(cx - edgeMid.X, cy - edgeMid.Y, 0);
                var outward = (perp1.X * toCentroid.X + perp1.Y * toCentroid.Y < 0)
                    ? perp1 : perp2;

                result.RawVoronoiEdges.Add((sharedCenter, sharedCenter + outward * large));
            }
        }

        /// <summary>
        /// Gift-wrapping convex hull of a 2D point set. Returns hull vertices in CCW order.
        /// </summary>
        private static List<XYZ> ConvexHull2D(List<XYZ> pts)
        {
            int n = pts.Count;
            if (n < 3) return new List<XYZ>(pts);

            // Start with the leftmost point
            int start = 0;
            for (int i = 1; i < n; i++)
                if (pts[i].X < pts[start].X || (pts[i].X == pts[start].X && pts[i].Y < pts[start].Y))
                    start = i;

            var hull = new List<XYZ>();
            int current = start;
            do
            {
                hull.Add(pts[current]);
                int next = (current + 1) % n;
                for (int i = 0; i < n; i++)
                {
                    // Cross product: if pts[i] is more CCW than pts[next], update next
                    double cross = (pts[next].X - pts[current].X) * (pts[i].Y - pts[current].Y)
                                 - (pts[next].Y - pts[current].Y) * (pts[i].X - pts[current].X);
                    if (cross < 0) next = i;
                }
                current = next;
            }
            while (current != start);

            return hull;
        }

        // ── Collinear fallback (Cases 2 & 3) ─────────────────────────────────────

        /// <summary>
        /// Returns true when all vertices lie on a single line within a perpendicular
        /// distance of <c>collinearTolFeet</c> (default 10 mm).
        ///
        /// The raw cross product dx01*dy0i - dy01*dx0i has units of ft^2 and grows with
        /// the scale of the coordinates — using it directly with a fixed tiny epsilon
        /// fails at large Revit world coordinates (~300 ft from origin). Dividing by
        /// the baseline length converts the result to a perpendicular distance in feet.
        /// </summary>
        private static bool AreCollinear(List<Vertex2D> vertices,
                                         double collinearTolFeet = 10.0 / 304.8)
        {
            var p1 = vertices[0].Position;
            var p2 = vertices[1].Position;
            double dx01 = p2[0] - p1[0], dy01 = p2[1] - p1[1];
            double baseLen = Math.Sqrt(dx01 * dx01 + dy01 * dy01);
            if (baseLen < 1e-9) return true; // all points coincident

            for (int i = 2; i < vertices.Count; i++)
            {
                double dx0i = vertices[i].Position[0] - p1[0];
                double dy0i = vertices[i].Position[1] - p1[1];
                // Perpendicular distance from vertex i to the baseline (in feet)
                double perpDist = Math.Abs(dx01 * dy0i - dy01 * dx0i) / baseLen;
                if (perpDist > collinearTolFeet)
                    return false;
            }
            return true;
        }

        private static void GenerateMidlinesForCollinearGroups(List<DrainGroup> groups, VoronoiRidgeResult result)
        {
            result.RawVoronoiEdges.Clear();
            result.RawVoronoiVertices.Clear();

            var p0 = groups[0].Centroid;
            var pN = groups[groups.Count - 1].Centroid;
            var axisRaw = new XYZ(pN.X - p0.X, pN.Y - p0.Y, 0);
            double axisLen = axisRaw.GetLength();
            if (axisLen < 1e-9) return;
            var axisDir = axisRaw.Normalize();

            var sorted = groups
                .OrderBy(g => (g.Centroid.X - p0.X) * axisDir.X + (g.Centroid.Y - p0.Y) * axisDir.Y)
                .ToList();

            const double large = 1000.0;
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var A = sorted[i].Centroid;
                var B = sorted[i + 1].Centroid;
                var mid = new XYZ((A.X + B.X) / 2, (A.Y + B.Y) / 2, 0);
                var perp = new XYZ(B.Y - A.Y, A.X - B.X, 0);
                double perpLen = perp.GetLength();
                if (perpLen < 1e-9) continue;
                perp = perp.Normalize();
                result.RawVoronoiEdges.Add((mid + perp * large, mid - perp * large));
                result.RawVoronoiVertices.Add(mid);
            }
        }

        // ── 2-group midline ───────────────────────────────────────────────────────

        private static void GenerateMidlineForTwoGroups(List<DrainGroup> groups, VoronoiRidgeResult result)
        {
            var A = groups[0].Centroid;
            var B = groups[1].Centroid;
            var mid = new XYZ((A.X + B.X) / 2, (A.Y + B.Y) / 2, 0);
            var dir = new XYZ(B.Y - A.Y, A.X - B.X, 0);
            double len = dir.GetLength();
            if (len < 1e-9) throw new InvalidOperationException("Two groups have identical centroids.");
            dir = dir.Normalize();
            const double large = 1000.0;
            result.RawVoronoiEdges.Clear();
            result.RawVoronoiEdges.Add((mid + dir * large, mid - dir * large));
            result.RawVoronoiVertices.Clear();
            result.RawVoronoiVertices.Add(mid);
        }

        // ── Math utilities ────────────────────────────────────────────────────────

        /// <summary>
        /// Computes the circumcenter of triangle (a, b, c).
        /// Translates to local coordinates first to avoid catastrophic cancellation
        /// when vertices are far from the origin (e.g. large Revit world coordinates).
        /// </summary>
        private static (double x, double y) ComputeCircumcenter(double[] a, double[] b, double[] c)
        {
            // Translate to local coords: p1 = a-a = (0,0), p2 = b-a, p3 = c-a
            double p2x = b[0] - a[0], p2y = b[1] - a[1];
            double p3x = c[0] - a[0], p3y = c[1] - a[1];

            double D = 2 * (p2x * p3y - p2y * p3x);
            if (Math.Abs(D) < 1e-10)
            {
                // Degenerate (collinear) — return centroid in world coords
                return ((a[0] + b[0] + c[0]) / 3.0, (a[1] + b[1] + c[1]) / 3.0);
            }

            double p2sq = p2x * p2x + p2y * p2y;
            double p3sq = p3x * p3x + p3y * p3y;

            // Circumcenter in local coords
            double ux = (p2sq * p3y - p3sq * p2y) / D;
            double uy = (p3sq * p2x - p2sq * p3x) / D;

            // Translate back to world coords
            return (ux + a[0], uy + a[1]);
        }
    }
}