namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Status of a single planned section row in the preview table, determined
    /// during the plan-build pass (before Run). Unlike FloorEdgeAroundSections_V004,
    /// there is no "NoEdgeFound" status here — a row only ever exists when at
    /// least one linked element matched a (floor, direction) pair.
    /// </summary>
    public enum PlannedSectionStatus
    {
        /// <summary>Cluster detected, within the per-(direction,category) cap — ready to create.</summary>
        Ready,

        /// <summary>
        /// Cluster detected but discarded because more than 2 clusters were found for the
        /// same (direction, category) group and this one had fewer elements. Shown in the
        /// table unchecked/disabled for transparency — never created.
        /// </summary>
        ClusterCapped
    }
}
