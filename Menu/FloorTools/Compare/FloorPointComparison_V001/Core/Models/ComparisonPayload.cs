using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Core.Models
{
    public enum ComparisonMode
    {
        Recalculate,
        PlaceMarkers
    }

    /// <summary>Payload for FloorComparisonHandler — set by the ViewModel before raising the ExternalEvent.</summary>
    public class ComparisonPayload
    {
        public ComparisonMode Mode { get; set; }

        public ElementId FloorAId { get; set; }
        public ElementId FloorBId { get; set; }

        public double PositionToleranceMm { get; set; }
        public double ElevationToleranceMm { get; set; }

        public MarkerStyleGroup MissingMarkerGroup { get; set; }
        public MarkerStyleGroup MismatchMarkerGroup { get; set; }

        public Action<LogEntry> Log { get; set; }
        public Action<ComparisonResult> OnRecalculated { get; set; }
        public Action<int, int> OnMarkersPlaced { get; set; }
    }
}
