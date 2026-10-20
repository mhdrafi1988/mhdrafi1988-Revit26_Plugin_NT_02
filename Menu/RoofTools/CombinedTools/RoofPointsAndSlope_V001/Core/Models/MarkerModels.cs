// =======================================================
// File: MarkerModels.cs
// Location: Core/Models/
// Temporary markers shown in the active view (Revit's in-canvas graphics:
// nothing is saved in the model, nothing goes on the Undo list).
// =======================================================

using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models
{
    /// <summary>What a marker stands for; each kind has its own colour.</summary>
    public enum MarkerKind
    {
        /// <summary>Step 1 point (blue).</summary>
        OpeningPoint,

        /// <summary>Step 2 point (purple).</summary>
        Perpendicular,

        /// <summary>Step 3 point (green).</summary>
        MidQuarter,

        /// <summary>Step 4 point (orange).</summary>
        GapFill,

        /// <summary>Step 5 ridge point (red).</summary>
        Ridge,

        /// <summary>Outline of a highlighted opening (yellow).</summary>
        Opening
    }

    /// <summary>One marker: a plan position and its kind.</summary>
    public readonly record struct MarkerPoint(P2 Position, MarkerKind Kind);
}
