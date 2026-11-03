// Ported unchanged from V003 (namespace updated only).
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Core.Models
{
    /// <summary>
    /// One straight sketch edge from the floor footprint boundary (outer loop or an
    /// inner/opening loop). Only Line curves become segments — Arc/Spline/etc. are
    /// reported as skipped by EdgeVertexReducerService and never touched.
    /// </summary>
    public class FloorSegment
    {
        /// <summary>"Outer", "Inner 1", "Inner 2", ... — which boundary loop this belongs to.</summary>
        public string LoopLabel { get; set; }

        /// <summary>0-based position of this segment within its loop, in sketch order.</summary>
        public int SegmentIndex { get; set; }

        public Line Line { get; set; }

        /// <summary>Segment start, flattened to Z = 0 (XY only — used for horizontal matching).</summary>
        public XYZ StartXY { get; set; }

        /// <summary>Segment end, flattened to Z = 0.</summary>
        public XYZ EndXY { get; set; }

        public double LengthXY { get; set; }

        public string Label => $"{LoopLabel} / seg {SegmentIndex + 1}";
    }
}
