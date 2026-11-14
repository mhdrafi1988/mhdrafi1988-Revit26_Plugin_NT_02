// Ported unchanged from V003 (namespace updated only).
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Core.Models
{
    public enum VertexAction
    {
        KeepStart,
        KeepEnd,
        KeepMaxZ,
        /// <summary>
        /// Outer-loop only. Kept because it sits exactly N positions (loop-traversal
        /// order) to the left or right of a qualifying max-Z point. N is user-set
        /// (MainViewModel.NeighborOffset). Never applied on inner loops.
        /// </summary>
        KeepNeighbor,
        Remove,
        /// <summary>Not within tolerance of any straight segment — always left untouched.</summary>
        KeepUnmatched
    }

    /// <summary>
    /// Result of classifying one SlabShapeVertex against the floor's straight edges
    /// and applying the keep/remove rule.
    /// </summary>
    public class VertexDecision
    {
        public SlabShapeVertex Vertex { get; set; }
        public XYZ Position { get; set; }

        /// <summary>Elevation in Revit internal units (feet).</summary>
        public double ZFeet { get; set; }

        public string SegmentLabel { get; set; }
        public VertexAction Action { get; set; }
        public string Reason { get; set; }

        public bool WillRemove => Action == VertexAction.Remove;
    }
}
