namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Models
{
    /// <summary>All metrics shown on the Metrics Card, computed by FloorComparisonEngine.</summary>
    public class ComparisonMetrics
    {
        public int TotalPointsA { get; set; }
        public int TotalPointsB { get; set; }

        public int MatchedCount { get; set; }
        public int MismatchedCount { get; set; }
        public int MissingCount { get; set; }
        public int DifferencesCount => MismatchedCount + MissingCount;

        /// <summary>Union of all distinct point positions across both floors.</summary>
        public int TotalUniquePoints => MatchedCount + MismatchedCount + MissingCount;

        public double MatchPercentage => TotalUniquePoints > 0
            ? (double)MatchedCount / TotalUniquePoints * 100.0
            : 0.0;

        public double HighestElevationA_mm { get; set; }
        public double HighestElevationB_mm { get; set; }
        public double LowestElevationA_mm { get; set; }
        public double LowestElevationB_mm { get; set; }
        public double AverageElevationA_mm { get; set; }
        public double AverageElevationB_mm { get; set; }

        public double ElevationRangeA_mm => HighestElevationA_mm - LowestElevationA_mm;
        public double ElevationRangeB_mm => HighestElevationB_mm - LowestElevationB_mm;

        public double StdDevElevationA_mm { get; set; }
        public double StdDevElevationB_mm { get; set; }

        /// <summary>Average of (ElevationB - ElevationA) over Matched-status pairs only.</summary>
        public double AverageElevationDiffMatched_mm { get; set; }
    }
}
