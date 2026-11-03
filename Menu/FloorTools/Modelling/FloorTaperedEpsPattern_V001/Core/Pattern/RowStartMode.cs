namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Where each row starts and where its leftover (make-up) cut goes, in contour-rows mode.</summary>
    public enum RowStartMode
    {
        /// <summary>A — full boards from both hips inward, one filler in the middle of the row. Hips mirrored (V003).</summary>
        HipsWithMiddleFiller,

        /// <summary>B — no middle cut: the leftover is split equally between the two hip ends of the row.</summary>
        HipsSplitLeftover,

        /// <summary>C — no middle cut: full boards from the left hip, leftover at the right-hand hip (looking in from the edge).</summary>
        OneHip,

        /// <summary>D — no middle cut: the row starts at its exact middle and boards run out to both hips.</summary>
        FromMiddle,

        /// <summary>
        /// E — from the upper hip point: each row's boards start where the hip crosses the row's upper
        /// line, so the hip cut is a clean triangle (plus the optional hip strip width) and the most full
        /// boards fit between the hips. One filler in the middle.
        /// </summary>
        FromUpperHip
    }
}
