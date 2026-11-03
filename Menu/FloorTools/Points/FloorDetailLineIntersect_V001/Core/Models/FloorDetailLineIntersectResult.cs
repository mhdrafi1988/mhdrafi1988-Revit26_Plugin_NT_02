// =======================================================
// File: FloorDetailLineIntersectResult.cs
// Location: Core/Models/
// New in V012. Plain result object returned by
// FloorDetailLineIntersectEngine. No UI, no WPF, no ViewModel references.
// =======================================================

namespace Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Core.Models
{
    public class FloorDetailLineIntersectResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        public int IntersectionsFound { get; set; }
        public int LineToLineIntersectionsFound { get; set; }
        public int PointsPlaced { get; set; }
        public int SkippedCount { get; set; }
    }
}
