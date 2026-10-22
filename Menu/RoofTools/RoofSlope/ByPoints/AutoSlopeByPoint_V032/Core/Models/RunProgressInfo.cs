// =======================================================
// File: RunProgressInfo.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models
// Changes vs V030: carries one monotonic OverallPercent (V030 P4: the bar
// jumped between indeterminate phases and sat at 100 % after the graph).
// =======================================================

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Core.Models
{
    public sealed class RunProgressInfo
    {
        /// <summary>Short label, e.g. "Building path graph".</summary>
        public string PhaseLabel { get; }

        /// <summary>0–100 across the whole run.</summary>
        public double OverallPercent { get; }

        /// <summary>0–100 inside the current phase, or null when not measured.</summary>
        public double? PercentWithinPhase { get; }

        /// <summary>Points handled so far in this phase, or null when not counted.</summary>
        public int? ItemsDone { get; }

        /// <summary>Total points in this phase, or null when not counted.</summary>
        public int? ItemsTotal { get; }

        public RunProgressInfo(string phaseLabel, double overallPercent, double? percentWithinPhase = null,
            int? itemsDone = null, int? itemsTotal = null)
        {
            ItemsDone = itemsDone;
            ItemsTotal = itemsTotal;
            PhaseLabel = phaseLabel;
            OverallPercent = overallPercent;
            PercentWithinPhase = percentWithinPhase;
        }
    }
}
