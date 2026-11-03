using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Engine
{
    public static class AutoSlopeGeometry
    {
        /// <summary>
        /// Returns every Arc curve found on the top face's boundary loops —
        /// outer boundary AND inner loops (openings/circular cut-outs).
        /// Non-arc curves (Line, etc.) are skipped since only arcs need
        /// arc-length-aware handling.
        /// </summary>
        public static List<Arc> GetBoundaryArcs(Face topFace)
        {
            var arcs = new List<Arc>();
            if (topFace == null) return arcs;

            foreach (EdgeArray loop in topFace.EdgeLoops)
            {
                foreach (Edge edge in loop)
                {
                    if (edge.AsCurve() is Arc arc)
                        arcs.Add(arc);
                }
            }
            return arcs;
        }
        /// <summary>
        /// Returns every straight Line edge found on the top face's boundary loops —
        /// outer boundary AND inner loops (openings). These are the floor's real
        /// physical edges. Used by CurveIntersectionHelper.InsertIntersectionPoints
        /// so only these (not arbitrary vertex-pair chords) are tested for arc
        /// intersections when deciding where a new shape point is actually required.
        /// </summary>
        public static List<Line> GetBoundaryLines(Face topFace)
        {
            var lines = new List<Line>();
            if (topFace == null) return lines;

            foreach (EdgeArray loop in topFace.EdgeLoops)
            {
                foreach (Edge edge in loop)
                {
                    if (edge.AsCurve() is Line line)
                        lines.Add(line);
                }
            }
            return lines;
        }

        public static Face GetTopFace(Floor floor)
        {
            if (floor == null) return null;

            Options opt = new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false
            };

            GeometryElement geom = floor.get_Geometry(opt);
            if (geom == null) return null;

            Face topFace = null;
            double maxZ = double.MinValue;

            foreach (GeometryObject obj in geom)
            {
                if (obj is not Solid solid || solid.Faces.Size == 0)
                    continue;

                foreach (Face face in solid.Faces)
                {
                    BoundingBoxUV bb = face.GetBoundingBox();
                    if (bb == null) continue;

                    UV mid = new UV(
                        (bb.Min.U + bb.Max.U) * 0.5,
                        (bb.Min.V + bb.Max.V) * 0.5);

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

        public static bool IsPointOnFace(XYZ point, Face face)
        {
            if (point == null || face == null) return false;
            IntersectionResult proj = face.Project(point);
            if (proj == null) return false;
            UV uv = proj.UVPoint;

            try
            {
                return face.IsInside(uv);
            }
            catch
            {
                BoundingBoxUV bb = face.GetBoundingBox();
                if (bb == null) return false;
                return uv.U >= bb.Min.U && uv.U <= bb.Max.U &&
                       uv.V >= bb.Min.V && uv.V <= bb.Max.V;
            }
        }
    }
}