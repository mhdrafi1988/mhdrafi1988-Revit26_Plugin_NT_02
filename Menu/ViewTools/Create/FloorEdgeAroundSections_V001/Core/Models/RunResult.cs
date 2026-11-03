using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// Outcome of a single Run() pass — counts for the completion summary line
    /// plus the created view ids (for the optional "open views" step).
    /// DetectedCount/SuggestedCount reflect the plan-build pass (before Run);
    /// CreatedCount/SkippedCount/FailedCount reflect the Run itself.
    /// </summary>
    public class RunResult
    {
        /// <summary>Floors included in this plan-build pass.</summary>
        public int TotalFloorsCount { get; set; }

        /// <summary>Raw candidate count: one per edge found, across all floors, before proximity-merge.</summary>
        public int DetectedCount { get; set; }

        /// <summary>Candidates remaining after proximity-merge — what's shown as actionable rows.</summary>
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
