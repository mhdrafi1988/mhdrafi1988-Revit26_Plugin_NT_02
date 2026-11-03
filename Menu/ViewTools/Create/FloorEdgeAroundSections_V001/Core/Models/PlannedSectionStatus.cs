namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// Status of a single planned section row in the preview table,
    /// determined during the plan-build pass (before Run).
    /// </summary>
    public enum PlannedSectionStatus
    {
        /// <summary>Edge found, ready to create.</summary>
        Ready,

        /// <summary>No boundary edge found close/parallel enough to this bounding-box side.</summary>
        NoEdgeFound,

        /// <summary>
        /// Edge found and named, but discarded by the proximity-merge pass because another
        /// row's edge midpoint was already kept within MergeDistanceMm. Shown in the table
        /// unchecked/disabled for transparency — never created.
        /// </summary>
        MergedOut
    }
}
