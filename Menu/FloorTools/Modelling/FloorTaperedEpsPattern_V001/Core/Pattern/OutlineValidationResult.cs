using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Validated outline (convex, CCW, collinear edges merged) or the reasons it was rejected.</summary>
    public sealed class OutlineValidationResult
    {
        public bool IsValid => Errors.Count == 0 && Outline != null;
        public Polygon2D? Outline { get; init; }
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        /// <summary>Short text for the Floor card badge, e.g. "Rectangle", "Convex 5-gon", "Concave".</summary>
        public string ShapeName { get; init; } = string.Empty;
    }
}
