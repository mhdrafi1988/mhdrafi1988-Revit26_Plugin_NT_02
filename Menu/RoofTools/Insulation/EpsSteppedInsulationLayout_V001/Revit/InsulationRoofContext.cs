using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Core;

namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit
{
    /// <summary>
    /// Snapshot of the picked roof, read in a valid API context so the modeless window
    /// never calls the Revit API itself.
    /// </summary>
    public sealed class InsulationRoofContext
    {
        public InsulationRoofContext(Document document, ElementId viewId, ElementId roofId)
        {
            Document = document;
            ViewId = viewId;
            RoofId = roofId;
        }

        public Document Document { get; }
        public ElementId ViewId { get; }
        public ElementId RoofId { get; }

        public string ViewName { get; init; } = string.Empty;
        public string RoofName { get; init; } = string.Empty;
        public string LevelName { get; init; } = string.Empty;
        public ElementId LevelId { get; init; }

        /// <summary>Elevation of the roof's level (ft, internal units).</summary>
        public double LevelElevationFeet { get; init; }

        /// <summary>Roof's "Base Offset From Level" (ft).</summary>
        public double BaseOffsetFeet { get; init; }

        /// <summary>Outline vertices in plan (metres, CCW).</summary>
        public IReadOnlyList<Vec2> OutlineMetres { get; init; }

        /// <summary>Drain points from shape editing (lowest Z vertices).</summary>
        public IReadOnlyList<DrainPoint> Drains { get; init; }

        /// <summary>All shape editing vertex positions, for reference.</summary>
        public IReadOnlyList<(Vec2 Plan, double ElevationFeet)> ShapeVertices { get; init; }

        public bool IsValid => OutlineMetres != null && OutlineMetres.Count >= 3;

        public string Error { get; init; }
    }
}
