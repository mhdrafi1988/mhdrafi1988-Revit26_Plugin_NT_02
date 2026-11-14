// =======================================================
// File: FloorEntry.cs
// Location: Core/Models/
// One floor in the window's floor list: what was read from it. Each floor
// keeps its own opening ticks and counts (they live on its outline's
// OpeningModel objects), so switching floors never loses them.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Engine;

namespace Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Models
{
    /// <summary>A floor the window works on.</summary>
    public sealed class FloorEntry
    {
        /// <summary>The floor element.</summary>
        public ElementId Id { get; init; }

        /// <summary>"Floor name · Id 123".</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary>The floor's outline, openings and shape points (replaced by Re-read Floor).</summary>
        public FloorOutline Outline { get; set; }

        /// <summary>What the ridge step needs (replaced by Re-read Floor).</summary>
        public RidgeInput RidgeInput { get; set; }

        /// <inheritdoc/>
        public override string ToString() => Label;
    }
}
