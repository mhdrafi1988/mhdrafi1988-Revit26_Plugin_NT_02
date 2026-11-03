using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Outcome of a single Run() pass — counts for the completion summary line
    /// plus the created view ids (for the optional "open views" step).
    /// Copied verbatim from FloorEdgeAroundSections_V004.
    /// </summary>
    public class RunResult
    {
        /// <summary>Floors included in this plan-build pass.</summary>
        public int TotalFloorsCount { get; set; }

        /// <summary>Raw detected linked-element count, across all floors, before clustering.</summary>
        public int DetectedCount { get; set; }

        /// <summary>Cluster rows remaining after the per-(direction,category) cap — what's shown as actionable rows.</summary>
        public int SuggestedCount { get; set; }

        public int CreatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }

        /// <summary>ElementIds of successfully created ViewSection elements, in creation order.</summary>
        public List<ElementId> CreatedViewIds { get; set; } = new List<ElementId>();

        public string SummaryLine =>
            $"{CreatedCount} placed | {SkippedCount} skipped | {FailedCount} failed";
    }
}
