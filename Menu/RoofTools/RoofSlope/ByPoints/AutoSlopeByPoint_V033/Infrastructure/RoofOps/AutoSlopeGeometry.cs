// =======================================================
// File: AutoSlopeGeometry.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
// Moved from V030 Core/Engine (it uses the Revit API, so it belongs in
// Infrastructure). Changes:
//   - GetClosestLoopIndex measures in plan (XY): the point is projected at
//     each curve's own height, so a pick taken on the old (sloped) surface
//     is not pushed towards whichever loop happens to sit higher (D3).
//   - IsPointOnFace removed (unused in V030).
//   - 32.3.0: GetOuterBoundaryCorners for the corner Offset notes.
// =======================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps
{
    internal static class AutoSlopeGeometry
    {
        /// <summary>Every Arc on the top face's loops (outer boundary and openings).</summary>
        public static List<Arc> GetBoundaryArcs(Face topFace)
        {
            var arcs = new List<Arc>();
            if (topFace == null) return arcs;

            foreach (EdgeArray loop in topFace.EdgeLoops)
                foreach (Edge edge in loop)
                    if (edge.AsCurve() is Arc arc)
                        arcs.Add(arc);
            return arcs;
        }

        /// <summary>Every straight Line edge on the top face's loops — the roof's real edges.</summary>
        public static List<Line> GetBoundaryLines(Face topFace)
        {
            var lines = new List<Line>();
            if (topFace == null) return lines;

            foreach (EdgeArray loop in topFace.EdgeLoops)
                foreach (Edge edge in loop)
                    if (edge.AsCurve() is Line line)
                        lines.Add(line);
            return lines;
        }

        /// <summary>Each top-face loop (outer boundary and each opening) as its own curve list.</summary>
        public static List<List<Curve>> GetBoundaryLoops(Face topFace)
        {
            var loops = new List<List<Curve>>();
            if (topFace == null) return loops;

            foreach (EdgeArray loop in topFace.EdgeLoops)
            {
                var curves = new List<Curve>();
                foreach (Edge edge in loop)
                {
                    Curve c = edge.AsCurve();
                    if (c != null) curves.Add(c);
                }
                if (curves.Count > 0) loops.Add(curves);
            }
            return loops;
        }

        /// <summary>
        /// Corners of the roof's outer boundary (the top-face loop with the largest plan extent; openings
        /// are ignored). A corner is a loop vertex where the direction turns by more than
        /// <paramref name="minAngleDeg"/>, so points that only split a straight edge, and smooth
        /// line-to-arc joins, are left out. Returns an empty list when the face or its loops can't be read.
        /// </summary>
        public static List<XYZ> GetOuterBoundaryCorners(Face topFace, double minAngleDeg)
        {
            var corners = new List<XYZ>();
            if (topFace == null) return corners;

            List<Curve> outer = null;
            double bestExtent = -1;
            foreach (EdgeArray loop in topFace.EdgeLoops)
            {
                var curves = new List<Curve>();
                foreach (Edge edge in loop)
                {
                    Curve c = edge.AsCurveFollowingFace(topFace);
                    if (c != null) curves.Add(c);
                }
                if (curves.Count == 0) continue;

                var pts = curves.SelectMany(c => c.Tessellate()).ToList();
                double extent = (pts.Max(p => p.X) - pts.Min(p => p.X)) * (pts.Max(p => p.Y) - pts.Min(p => p.Y));
                if (extent > bestExtent)
                {
                    bestExtent = extent;
                    outer = curves;
                }
            }
            if (outer == null) return corners;

            double minAngleRad = minAngleDeg * Math.PI / 180.0;
            for (int i = 0; i < outer.Count; i++)
            {
                Curve prev = outer[(i - 1 + outer.Count) % outer.Count];
                Curve cur = outer[i];
                XYZ dirIn = FlatTangent(prev, 1.0);
                XYZ dirOut = FlatTangent(cur, 0.0);
                if (dirIn == null || dirOut == null || dirIn.AngleTo(dirOut) > minAngleRad)
                    corners.Add(cur.GetEndPoint(0));
            }
            return corners;
        }

        /// <summary>Unit tangent in plan at a normalized parameter, or null for a vertical or degenerate tangent.</summary>
        private static XYZ FlatTangent(Curve curve, double normalizedParam)
        {
            try
            {
                XYZ t = curve.ComputeDerivatives(normalizedParam, true).BasisX;
                var flat = new XYZ(t.X, t.Y, 0);
                return flat.GetLength() < 1e-9 ? null : flat.Normalize();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }

        /// <summary>Index of the loop closest to the point in plan, or -1.</summary>
        public static int GetClosestLoopIndex(XYZ point, List<List<Curve>> loops)
        {
            if (point == null || loops == null || loops.Count == 0) return -1;

            int bestIndex = -1;
            double bestDist = double.MaxValue;

            for (int i = 0; i < loops.Count; i++)
            {
                foreach (Curve curve in loops[i])
                {
                    // Bring the point to the curve's height so only plan distance counts.
                    XYZ flat = new XYZ(point.X, point.Y, curve.GetEndPoint(0).Z);
                    IntersectionResult proj = curve.Project(flat);
                    if (proj == null) continue;

                    XYZ q = proj.XYZPoint;
                    double dx = q.X - point.X, dy = q.Y - point.Y;
                    double dist = dx * dx + dy * dy;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestIndex = i;
                    }
                }
            }
            return bestIndex;
        }

        /// <summary>The roof face whose centre sits highest (unchanged from V030).</summary>
        public static Face GetTopFace(RoofBase roof)
        {
            if (roof == null) return null;

            var opt = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false };
            GeometryElement geom = roof.get_Geometry(opt);
            if (geom == null) return null;

            Face topFace = null;
            double maxZ = double.MinValue;

            foreach (GeometryObject obj in geom)
            {
                if (obj is not Solid solid || solid.Faces.Size == 0) continue;

                foreach (Face face in solid.Faces)
                {
                    BoundingBoxUV bb = face.GetBoundingBox();
                    if (bb == null) continue;

                    var mid = new UV((bb.Min.U + bb.Max.U) * 0.5, (bb.Min.V + bb.Max.V) * 0.5);
                    XYZ p = face.Evaluate(mid);
                    if (p == null) continue;

                    if (p.Z > maxZ)
                    {
                        maxZ = p.Z;
                        topFace = face;
                    }
                }
            }
            return topFace;
        }
    }
}
