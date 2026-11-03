using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Models;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Services
{
    /// <summary>
    /// Clips raw Voronoi edges against the floor boundary polygon and collects
    /// all shape point locations according to the rules:
    ///   1. Voronoi vertices inside boundary
    ///   2. Edge-to-boundary intersections (clip points)
    ///   3. Edge-to-edge intersections inside boundary
    ///   4. Arc/curve tessellation points on boundary (already flattened by FloorBoundaryService)
    ///
    /// Fix C (Case 4): boundary-crossing parametric t-values are de-duplicated
    /// (within SnapTolerance, scaled by segment length) before the inside/outside
    /// interval walk, preventing false discards caused by a segment hitting a
    /// polygon corner vertex shared by two adjacent edges.
    ///
    /// Uses a Sutherland–Hodgman style segment-vs-polygon clip for edge clipping.
    /// No Revit transaction required.
    /// </summary>
    public class VoronoiClippingService
    {
        /// <summary>Points closer than this are considered the same (Revit internal units = feet).</summary>
        public double SnapTolerance { get; set; } = 1.0 / 304.8; // 1 mm in feet

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Clips all raw Voronoi edges against <paramref name="boundaryPolygon"/> and
        /// populates <see cref="VoronoiRidgeResult.ClippedEdges"/> and
        /// <see cref="VoronoiRidgeResult.ShapePoints"/>.
        /// </summary>
        public void ClipAndCollect(
            VoronoiRidgeResult result,
            List<XYZ> boundaryPolygon,
            List<DrainGroup> groups)
        {
            result.ClippedEdges.Clear();
            result.ShapePoints.Clear();
            result.ShapePointGroupMap.Clear();

            var collector = new List<(XYZ point, List<int> groupIndices, string note)>();

            // ── 1. Clip each raw edge ─────────────────────────────────────────────
            // Non-convex boundaries (notched/L-shaped floors) can produce multiple
            // entry/exit intersection pairs per edge. We handle this by sorting all
            // intersections by parametric position t along the segment, then emitting
            // one clipped sub-segment per entry→exit pair.
            foreach (var edge in result.RawVoronoiEdges)
            {
                var nearest = FindTwoNearestGroups(Midpoint(edge.Start, edge.End), groups);

                var segDir = new XYZ(edge.End.X - edge.Start.X, edge.End.Y - edge.Start.Y, 0);
                double segLen = segDir.GetLength();
                if (segLen < 1e-9) continue;

                // Collect all boundary intersections with their parametric t values
                var tHits = new List<double>();
                int n = boundaryPolygon.Count;
                for (int pi = 0; pi < n; pi++)
                {
                    var c = boundaryPolygon[pi];
                    var d = boundaryPolygon[(pi + 1) % n];
                    if (FloorGeometry2D.TrySegmentIntersectParametric(edge.Start, edge.End, c, d, out double t, out XYZ _))
                        tHits.Add(t);
                }

                // Deduplicate t values that are very close (corner double-hits)
                tHits.Sort();
                var tUnique = new List<double>();
                foreach (var t in tHits)
                    if (tUnique.Count == 0 || t - tUnique[tUnique.Count - 1] > SnapTolerance / segLen)
                        tUnique.Add(t);

                bool startInside = FloorGeometry2D.PointInPolygon(edge.Start, boundaryPolygon);

                // ── Event walk: sort crossings, toggle inside/outside, emit inside intervals ──
                var intervals = new List<(double tA, double tB)>();
                double tStart2 = 0.0;
                bool curInside = startInside;

                foreach (double tCross in tUnique)
                {
                    if (curInside)
                        intervals.Add((tStart2, tCross));
                    curInside = !curInside;
                    tStart2 = tCross;
                }
                if (curInside)
                    intervals.Add((tStart2, 1.0));

                // Emit one clipped edge per inside interval
                foreach (var (tA, tB) in intervals)
                {
                    if (tB - tA < SnapTolerance / segLen) continue; // degenerate interval

                    var ptA = new XYZ(edge.Start.X + tA * segDir.X,
                                      edge.Start.Y + tA * segDir.Y, 0);
                    var ptB = new XYZ(edge.Start.X + tB * segDir.X,
                                      edge.Start.Y + tB * segDir.Y, 0);

                    if (FloorGeometry2D.Dist2D(ptA, ptB) < SnapTolerance) continue;

                    result.ClippedEdges.Add((ptA, ptB));

                    // Clip points at boundary crossings become shape points
                    if (tA > 1e-9)
                        AddPoint(collector, ptA, nearest, "Boundary clip intersection", SnapTolerance);
                    if (tB < 1.0 - 1e-9)
                        AddPoint(collector, ptB, nearest, "Boundary clip intersection", SnapTolerance);
                }
            }

            // ── 2. Voronoi vertices inside boundary ───────────────────────────────
            foreach (var v in result.RawVoronoiVertices)
            {
                if (!FloorGeometry2D.PointInPolygon(v, boundaryPolygon)) continue;
                var nearestGroups = FindThreeNearestGroups(v, groups);
                AddPoint(collector, v, nearestGroups, "Voronoi vertex (circumcenter)", SnapTolerance);
            }

            // ── 3. Edge-to-edge intersections ─────────────────────────────────────
            for (int i = 0; i < result.ClippedEdges.Count; i++)
            {
                for (int j = i + 1; j < result.ClippedEdges.Count; j++)
                {
                    if (FloorGeometry2D.TrySegmentIntersect(
                        result.ClippedEdges[i].Start, result.ClippedEdges[i].End,
                        result.ClippedEdges[j].Start, result.ClippedEdges[j].End,
                        out XYZ ip))
                    {
                        if (FloorGeometry2D.PointInPolygon(ip, boundaryPolygon))
                        {
                            var g = FindTwoNearestGroups(ip, groups);
                            AddPoint(collector, ip, g, "Edge-to-edge intersection", SnapTolerance);
                        }
                    }
                }
            }

            // ── Finalise shape points ─────────────────────────────────────────────
            for (int i = 0; i < collector.Count; i++)
            {
                result.ShapePoints.Add(collector[i].point);
                result.ShapePointGroupMap[i] = collector[i].groupIndices;
            }
        }

        // ── Group proximity helpers ───────────────────────────────────────────────

        private static List<int> FindTwoNearestGroups(XYZ p, List<DrainGroup> groups)
        {
            return FloorGeometry2D.FindNNearestGroups(p, groups, 2);
        }

        private static List<int> FindThreeNearestGroups(XYZ p, List<DrainGroup> groups)
        {
            return FloorGeometry2D.FindNNearestGroups(p, groups, 3);
        }

        // ── De-duplicating point collector ────────────────────────────────────────

        private static void AddPoint(
            List<(XYZ point, List<int> groupIndices, string note)> collector,
            XYZ newPt,
            List<int> groupIndices,
            string note,
            double snapTol)
        {
            foreach (var existing in collector)
            {
                if (FloorGeometry2D.Dist2D(existing.point, newPt) < snapTol) return; // duplicate
            }
            collector.Add((newPt, groupIndices, note));
        }

        // ── Math utilities ────────────────────────────────────────────────────────

        private static XYZ Midpoint(XYZ a, XYZ b)
            => new XYZ((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0);
    }
}