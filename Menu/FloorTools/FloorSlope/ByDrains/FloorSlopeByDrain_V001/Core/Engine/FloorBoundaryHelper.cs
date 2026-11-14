// File: FloorBoundaryHelper.cs
// Location: Core/Engine/
// Base: GetBoundaryArcs ported unchanged from AutoSlopeByDrain V004 (roof) (itself
// ported from AutoSlopeByPoint's AutoSlopeGeometry.GetBoundaryArcs).
//
// NEW (V005): GetBoundaryLines added, ported from AutoSlopeByPoint's
// AutoSlopeGeometry.GetBoundaryLines. This was missing in V004 because
// CurveIntersectionHelper.InsertIntersectionPoints (which needs it) was
// present as a file but never actually called — the curve-intersection
// feature is being wired in for the first time in this combined tool, per
// Rafi's confirmed decision.

using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Engine
{
    public static class FloorBoundaryHelper
    {
        /// <summary>
        /// Returns every Arc curve found on the top face's boundary loops —
        /// outer boundary AND inner loops (openings/drains with curved edges).
        /// Non-arc curves (Line, etc.) are skipped since only arcs need
        /// arc-length-aware (tangent-route) handling in DijkstraPathEngine.
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
        /// NEW (V005). Returns every straight Line edge found on the top face's
        /// boundary loops — outer boundary AND inner loops (openings). These are
        /// the floor's real physical edges. Used by
        /// CurveIntersectionHelper.InsertIntersectionPoints so only these (not
        /// arbitrary vertex-pair chords) are tested for arc intersections when
        /// deciding where a new shape point is actually required.
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
    }
}
