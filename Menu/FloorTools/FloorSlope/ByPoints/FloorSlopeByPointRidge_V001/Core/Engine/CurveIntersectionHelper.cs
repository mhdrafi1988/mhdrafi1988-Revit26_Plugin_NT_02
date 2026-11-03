// =======================================================
// File: CurveIntersectionHelper.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001
// Purpose:
//   - Detect which SlabShapeVertex points already sit on a boundary/opening Arc.
//   - Detect where a straight line between two vertices enters/exits an Arc
//     (partial overlap case) and insert real SlabShapeVertex points there.
//   - Provide arc-length lookup for two points known to lie on the same Arc.
// =======================================================

using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Core.Engine
{
    /// <summary>Maps a vertex position to the Arc it lies on (if any) and its parameter on that Arc.</summary>
    public class VertexCurveInfo
    {
        public Arc Curve;
        public double Parameter;
    }

    public static class CurveIntersectionHelper
    {
        /// <summary>
        /// For each position, checks whether it lies on any of the given arcs
        /// (within tolerance). Returns every matching arc per vertex, not just the
        /// first — a vertex sitting at a shared endpoint between two adjoining arcs
        /// is genuinely "on" both of them, and the same-arc bypass in
        /// DijkstraPathEngine needs to see whichever one the OTHER point in a pair
        /// actually shares, not an arbitrary single pick.
        /// </summary>
        public static Dictionary<int, List<VertexCurveInfo>> MapVerticesOnCurves(
            List<XYZ> positions, List<Arc> arcs, double toleranceFt)
        {
            var map = new Dictionary<int, List<VertexCurveInfo>>();

            for (int i = 0; i < positions.Count; i++)
            {
                XYZ p = positions[i];
                foreach (Arc arc in arcs)
                {
                    IntersectionResult proj = arc.Project(p);
                    if (proj == null) continue;

                    if (proj.XYZPoint.DistanceTo(p) <= toleranceFt)
                    {
                        if (!map.TryGetValue(i, out var list))
                        {
                            list = new List<VertexCurveInfo>();
                            map[i] = list;
                        }
                        list.Add(new VertexCurveInfo { Curve = arc, Parameter = proj.Parameter });
                    }
                }
            }
            return map;
        }

        /// <summary>
        /// Exact distance between two parameters on the SAME arc (not the chord).
        /// Guards against near-zero spans: Curve.MakeBound throws "New parameterization
        /// made the curve length too small for Revit's tolerance (ShortCurveTolerance)"
        /// when the bound span is extremely short — most commonly when two tangent
        /// solutions from GetTangentCandidates both clamp to the same arc endpoint,
        /// giving paramA ≈ paramB. Below that length, fall back to the direct
        /// point-to-point distance instead of trying to bind an ultra-short curve.
        /// </summary>
        public static double ArcLengthBetween(Arc arc, double paramA, double paramB)
        {
            double lo = paramA < paramB ? paramA : paramB;
            double hi = paramA < paramB ? paramB : paramA;

            double approxLen = arc.Radius * (hi - lo);
            if (approxLen < 0.01) // ~3 mm — safely above Revit's short curve tolerance
            {
                XYZ pA = arc.Evaluate(paramA, false);
                XYZ pB = arc.Evaluate(paramB, false);
                return pA.DistanceTo(pB);
            }

            Curve sub = arc.Clone();
            sub.MakeBound(lo, hi);
            return sub.Length;
        }

        /// <summary>
        /// Computes the external tangent point(s) from a point to an arc, clamped to the
        /// arc's bounded parameter range (endpoint used if the true tangent falls outside it).
        /// If the point is inside/on the arc's circle (no true external tangent exists,
        /// e.g. the point already sits on the arc), falls back to the nearest point on the
        /// arc — this naturally collapses to the point's own position for on-arc points,
        /// so no special-casing is needed there.
        /// Returns up to 2 candidates (the two classic external-tangent solutions); caller
        /// should try both and keep whichever produces the shorter valid path.
        /// </summary>
        public static List<VertexCurveInfo> GetTangentCandidates(XYZ externalPoint, Arc arc)
        {
            var results = new List<VertexCurveInfo>();

            XYZ center = arc.Center;
            XYZ xDir = arc.XDirection;
            XYZ yDir = arc.YDirection;
            XYZ normal = xDir.CrossProduct(yDir).Normalize();
            double radius = arc.Radius;

            double startParam = arc.GetEndParameter(0);
            double endParam = arc.GetEndParameter(1);

            // Project the external point onto the arc's plane.
            XYZ toPoint = externalPoint - center;
            double offPlane = toPoint.DotProduct(normal);
            XYZ projected = externalPoint - normal * offPlane;

            XYZ d = projected - center;
            double dist = d.GetLength();

            if (dist <= radius + 1e-9)
            {
                // Inside or on the circle: no true external tangent. Fall back to the
                // nearest point on the arc (collapses to the point itself when it's on-arc).
                IntersectionResult proj = arc.Project(externalPoint);
                if (proj == null) return results;

                double clamped = ClampToRange(proj.Parameter, startParam, endParam);
                results.Add(new VertexCurveInfo { Curve = arc, Parameter = clamped });
                return results;
            }

            double theta0 = Math.Atan2(d.DotProduct(yDir), d.DotProduct(xDir));
            double alpha = Math.Acos(radius / dist);

            double[] rawThetas = { theta0 + alpha, theta0 - alpha };

            foreach (double raw in rawThetas)
            {
                double theta = NormalizeAngleToRange(raw, startParam, endParam);
                double clamped = ClampToRange(theta, startParam, endParam);
                results.Add(new VertexCurveInfo { Curve = arc, Parameter = clamped });
            }

            return results;
        }

        private static double ClampToRange(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        /// <summary>
        /// Shifts angle by whole 2π turns so it falls inside [lo, hi] when possible,
        /// before clamping. Handles Revit's Arc raw parameter being an angle in radians.
        /// </summary>
        private static double NormalizeAngleToRange(double angle, double lo, double hi)
        {
            while (angle < lo - 1e-9) angle += 2 * Math.PI;
            while (angle > hi + 1e-9) angle -= 2 * Math.PI;
            return angle;
        }

        /// <summary>
        /// Checks every REAL boundary/opening edge (the face's own straight Line
        /// segments, from Face.EdgeLoops) for intersection with any boundary arc,
        /// and inserts new real SlabShapeVertex points at the entry/exit locations
        /// found. Must be called inside an active Transaction. Returns the number
        /// of points inserted.
        ///
        /// PREVIOUS BEHAVIOUR (removed): tested every vertex pair within
        /// edgeThresholdFt — an O(n²) scan that reused the Dijkstra pathfinding
        /// threshold to decide which pairs to check. That inserted a point for any
        /// two vertices merely "close enough" to be pathfinding candidates,
        /// including diagonal chords that are not real floor edges at all — hence
        /// far more shape-editor points than the geometry actually needed.
        /// This version only tests the floor's actual physical edges, so a point
        /// is inserted only where a real edge truly crosses an arc.
        /// </summary>
        public static int InsertIntersectionPoints(
            SlabShapeEditor editor,
            List<Line> boundaryLines,
            List<XYZ> vertexPositions,
            List<Arc> arcs,
            double toleranceFt,
            System.Action<string> log = null)
        {
            if (arcs == null || arcs.Count == 0 || boundaryLines == null || boundaryLines.Count == 0)
                return 0;

            var toInsert = new List<XYZ>();

            foreach (Line line in boundaryLines)
            {
                XYZ a = line.GetEndPoint(0);
                XYZ b = line.GetEndPoint(1);

                foreach (Arc arc in arcs)
                {
                    SetComparisonResult result;
                    IntersectionResultArray xsects;
                    try
                    {
                        result = line.Intersect(arc, out xsects);
                    }
                    catch
                    {
                        continue;
                    }

                    if (result != SetComparisonResult.Overlap || xsects == null) continue;

                    foreach (IntersectionResult ir in xsects)
                    {
                        XYZ pt = ir.XYZPoint;
                        if (pt == null) continue;

                        // Skip if it basically coincides with this edge's own endpoints.
                        if (pt.DistanceTo(a) <= toleranceFt || pt.DistanceTo(b) <= toleranceFt)
                            continue;

                        bool alreadyQueued = false;
                        foreach (XYZ existing in toInsert)
                        {
                            if (existing.DistanceTo(pt) <= toleranceFt) { alreadyQueued = true; break; }
                        }
                        if (alreadyQueued) continue;

                        bool alreadyVertex = false;
                        foreach (XYZ v in vertexPositions)
                        {
                            if (v.DistanceTo(pt) <= toleranceFt) { alreadyVertex = true; break; }
                        }
                        if (alreadyVertex) continue;

                        toInsert.Add(pt);
                    }
                }
            }

            foreach (XYZ pt in toInsert)
            {
                editor.AddPoint(pt);
            }

            log?.Invoke($"Curve intersection check: inserted {toInsert.Count} new point(s) on arc boundaries/openings (real edges only).");
            return toInsert.Count;
        }
    }
}
