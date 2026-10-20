// Test stand-in for the tool's OpeningModel: only the members the planner reads.
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models
{
    /// <summary>Minimal opening for tests (the real class needs Revit).</summary>
    public class OpeningModel
    {
        /// <summary>Opening number.</summary>
        public int Index { get; init; }

        /// <summary>Opening outline.</summary>
        public Loop2D Loop { get; init; }

        /// <summary>Step 1 ticked.</summary>
        public bool AddInnerPoints { get; set; }

        /// <summary>Step 1 point count.</summary>
        public int PointCount { get; set; }

        /// <summary>Step 2 ticked.</summary>
        public bool AddPerpendicular { get; set; }
    }
}
