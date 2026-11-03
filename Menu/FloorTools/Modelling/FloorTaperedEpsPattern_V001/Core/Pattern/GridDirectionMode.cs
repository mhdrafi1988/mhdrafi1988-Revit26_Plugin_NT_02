namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    public enum GridDirectionMode
    {
        /// <summary>Board length runs along the longest outline edge (tie → first edge CCW).</summary>
        LongestEdge,

        /// <summary>Board length runs along the project (internal) X axis.</summary>
        ProjectX,

        /// <summary>Board length runs parallel to the outline edge <c>Pattern2DSettings.AlignEdgeIndex</c>.</summary>
        SelectedEdge,

        /// <summary>
        /// Contour-row layout: on each slope face the boards run in rows between the thickness contours,
        /// parallel to the floor edge the face drains to; full boards from both hips inward, mirrored hip
        /// pieces, one filler in the middle of each row (see <c>RowLayoutBuilder</c>).
        /// </summary>
        EachEdge
    }
}
