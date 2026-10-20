// IslandRoofSplitter V002 — one picked detail curve, flattened to plan by the Infrastructure extractor.
#nullable enable
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.IslandRoofSplitter.V002.Core.Models
{
    public enum SegmentKind
    {
        /// <summary>Points = [start, end].</summary>
        Line,
        /// <summary>Points = [start, pointOnArc, end] — three-point circular arc.</summary>
        Arc,
        /// <summary>Points = Revit's own tessellation (splines, ellipses, anything not a line/arc).</summary>
        Polyline
    }

    public sealed class SourceSegment
    {
        /// <summary>ElementId.Value of the source DetailCurve.</summary>
        public long SourceId { get; init; }
        public SegmentKind Kind { get; init; }
        public IReadOnlyList<Pt2> Points { get; init; } = new List<Pt2>();
    }
}
