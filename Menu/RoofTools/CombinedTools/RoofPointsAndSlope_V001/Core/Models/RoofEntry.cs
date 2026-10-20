// =======================================================
// File: RoofEntry.cs
// Location: Core/Models/
// One roof in the window's roof list: what was read from it. Each roof
// keeps its own opening ticks and counts (they live on its outline's
// OpeningModel objects), so switching roofs never loses them.
// =======================================================

using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Engine;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models
{
    /// <summary>A roof the window works on.</summary>
    public sealed class RoofEntry
    {
        /// <summary>The roof element.</summary>
        public ElementId Id { get; init; }

        /// <summary>"Roof name · Id 123".</summary>
        public string Label { get; init; } = string.Empty;

        /// <summary>The roof's outline, openings and shape points (replaced by Re-read Roof).</summary>
        public RoofOutline Outline { get; set; }

        /// <summary>What the ridge step needs (replaced by Re-read Roof).</summary>
        public RidgeInput RidgeInput { get; set; }

        /// <inheritdoc/>
        public override string ToString() => Label;
    }
}
