// =======================================================
// File: CurveIntersectionHelper.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
// Moved from V030 Core/Engine (Revit API). Logic unchanged:
//   - which shape points sit on a boundary/opening Arc
//   - insert real shape points where a straight roof edge meets an Arc
//   - arc length between two parameters, tangent candidates around an Arc
// =======================================================

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
{
    /// <summary>An Arc a vertex lies on, and its parameter on that Arc.</summary>
    internal sealed class VertexCurveInfo
    {
        public Arc Curve;
        public double Parameter;
    }

    internal static class CurveIntersectionHelper
    {
        /// <summary>Every arc each position lies on (a vertex at a shared endpoint is on both).</summary>
        public static Dictionary<int, List<VertexCurveInfo>> MapVerticesOnCurves(
            IReadOnlyList<XYZ> positions, List<Arc> arcs, double toleranceFt)
        {
            var map = new Dictionary<int, List<VertexCurveInfo>>();
            if (arcs == null || arcs.Count == 0) return map;

            for (int i = 0; i < positions.Count; i++)
            {
                XYZ p = positions[i];
                foreach (Arc arc in arcs)
                {
                    IntersectionResult proj = arc.Project(p);
                    if (proj == null) continue;
                    if (proj.XYZPoint.DistanceTo(p) > toleranceFt) continue;

                    if (!map.TryGetValue(i, out var list))
                        map[i] = list = new List<VertexCurveInfo>();
                    list.Add(new VertexCurveInfo { Curve = arc, Parameter = proj.Parameter });
                }
            }
            return map;
        }

        /// <summary>
        /// True length between two parameters on the same arc. Very short spans
        /// fall back to the chord: Curve.MakeBound throws below Revit's
        /// ShortCurveTolerance (e.g. two tangent solutions clamped to one endpoint).
        /// </summary>
        public static double ArcLengthBetween(Arc arc, double paramA, double paramB)
        {
            double lo = Math.Min(paramA, paramB);
            double hi = Math.Max(paramA, paramB);

            if (arc.Radius * (hi - lo) < 0.01) // ~3 mm
                return arc.Evaluate(paramA, false).DistanceTo(arc.Evaluate(paramB, false));

            Curve sub = arc.Clone();
            sub.MakeBound(lo, hi);
            return sub.Length;
        }

        /// <summary>
        /// External tangent point(s) from a point to an arc, clamped to the arc's
        /// range. A point inside/on the circle falls back to its nearest point on
        /// the arc (which is the point itself when it lies on the arc).
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

            double offPlane = (externalPoint - center).DotProduct(normal);
            XYZ projected = externalPoint - normal * offPlane;
            XYZ d = projected - center;
            double dist = d.GetLength();

            if (dist <= radius + 1e-9)
            {
                IntersectionResult proj = arc.Project(externalPoint);
                if (proj == null) return results;
                results.Add(new VertexCurveInfo { Curve = arc, Parameter = Clamp(proj.Parameter, startParam, endParam) });
                return results;
            }

            double theta0 = Math.Atan2(d.DotProduct(yDir), d.DotProduct(xDir));
            double alpha = Math.Acos(radius / dist);

            foreach (double raw in new[] { theta0 + alpha, theta0 - alpha })
            {
                double theta = NormalizeAngleToRange(raw, startParam, endParam);
                results.Add(new VertexCurveInfo { Curve = arc, Parameter = Clamp(theta, startParam, endParam) });
            }
            return results;
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        private static double NormalizeAngleToRange(double angle, double lo, double hi)
        {
            while (angle < lo - 1e-9) angle += 2 * Math.PI;
            while (angle > hi + 1e-9) angle -= 2 * Math.PI;
            return angle;
        }

        /// <summary>
        /// Tests the roof's real straight edges against every arc and adds a shape
        /// point at each entry/exit. Must run inside an open Transaction.
        /// Returns the number of points inserted.
        /// </summary>
        public static int InsertIntersectionPoints(
            SlabShapeEditor editor,
            List<Line> boundaryLines,
            IReadOnlyList<XYZ> vertexPositions,
            List<Arc> arcs,
            double toleranceFt)
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
                    try { result = line.Intersect(arc, out xsects); }
                    catch { continue; }

                    if (result != SetComparisonResult.Overlap || xsects == null) continue;

                    foreach (IntersectionResult ir in xsects)
                    {
                        XYZ pt = ir.XYZPoint;
                        if (pt == null) continue;
                        if (pt.DistanceTo(a) <= toleranceFt || pt.DistanceTo(b) <= toleranceFt) continue;
                        if (IsNear(pt, toInsert, toleranceFt)) continue;
                        if (IsNear(pt, vertexPositions, toleranceFt)) continue;
                        toInsert.Add(pt);
                    }
                }
            }

            foreach (XYZ pt in toInsert)
                editor.AddPoint(pt);

            return toInsert.Count;
        }

        private static bool IsNear(XYZ pt, IReadOnlyList<XYZ> list, double tol)
        {
            foreach (XYZ q in list)
                if (q.DistanceTo(pt) <= tol) return true;
            return false;
        }
    }
}
