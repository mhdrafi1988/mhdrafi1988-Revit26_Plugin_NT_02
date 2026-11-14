using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Models
{
    /// <summary>Output of FloorComparisonEngine.Compare — the full entry list plus rolled-up metrics.</summary>
    public class ComparisonResult
    {
        public List<PointComparisonEntry> Entries { get; set; } = new List<PointComparisonEntry>();
        public ComparisonMetrics Metrics { get; set; } = new ComparisonMetrics();

        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; }
    }
}
