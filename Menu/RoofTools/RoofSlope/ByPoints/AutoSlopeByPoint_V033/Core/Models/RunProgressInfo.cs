// =======================================================
// File: RunProgressInfo.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
// Changes vs V030: carries one monotonic OverallPercent (V030 P4: the bar
// jumped between indeterminate phases and sat at 100 % after the graph).
// =======================================================

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models
{
    public sealed class RunProgressInfo
    {
        /// <summary>Short label, e.g. "Building path graph".</summary>
        public string PhaseLabel { get; }

        /// <summary>0–100 across the whole run.</summary>
        public double OverallPercent { get; }

        /// <summary>0–100 inside the current phase, or null when not measured.</summary>
        public double? PercentWithinPhase { get; }

        public RunProgressInfo(string phaseLabel, double overallPercent, double? percentWithinPhase = null)
        {
            PhaseLabel = phaseLabel;
            OverallPercent = overallPercent;
            PercentWithinPhase = percentWithinPhase;
        }
    }
}
