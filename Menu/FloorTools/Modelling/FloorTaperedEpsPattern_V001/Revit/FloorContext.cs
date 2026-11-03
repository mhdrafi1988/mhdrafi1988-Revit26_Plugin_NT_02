using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>
    /// Snapshot of the picked floor and view, read in a valid API context so the modeless
    /// window never has to call the Revit API itself.
    /// </summary>
    public sealed class FloorContext
    {
        public FloorContext(Document document, ElementId viewId, ElementId floorId)
        {
            Document = document;
            ViewId = viewId;
            FloorId = floorId;
        }

        public Document Document { get; }
        public ElementId ViewId { get; }
        public ElementId FloorId { get; }

        public string ViewName { get; init; } = string.Empty;
        public string FloorName { get; init; } = string.Empty;
        public string LevelName { get; init; } = string.Empty;

        /// <summary>Elevation of the view's level relative to the internal origin (ft).</summary>
        public double LevelElevationFeet { get; init; }

        /// <summary>Revit's ShortCurveTolerance (ft).</summary>
        public double ShortCurveToleranceFeet { get; init; }

        /// <summary>Minimum segment length for the Core engine: max(1 mm, ShortCurveTolerance) in metres.</summary>
        public double MinSegmentLengthMetres { get; init; } = GeometryTolerance.MinSegmentLength;

        public int SketchEdgeCount { get; init; }

        public OutlineValidationResult Outline { get; init; } = new OutlineValidationResult();

        public bool IsValid => Outline.IsValid;

        public string GroupName => $"EPS2D_{FloorId.Value}_{ViewId.Value}";
    }
}
