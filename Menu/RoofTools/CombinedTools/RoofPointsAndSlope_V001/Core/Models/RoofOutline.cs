// =======================================================
// File: RoofOutline.cs
// Location: Core/Models/
// What the tool knows about one roof, in plan: its boundary loops, its
// openings, every edge (one per sketch curve) and the shape points it
// already has. Read by Infrastructure/Revit/RoofReader; used by the
// planner without any Revit call, so the window can re-plan live.
// =======================================================

using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models
{
    /// <summary>One edge of the roof in plan: a boundary or opening curve.</summary>
    public sealed class RoofEdge
    {
        /// <summary>The edge geometry (line, arc or polyline for ellipses/splines).</summary>
        public LoopSegment2D Segment { get; init; }

        /// <summary>The loop the edge belongs to.</summary>
        public Loop2D Loop { get; init; }

        /// <summary>True for the outer boundary, false for an opening.</summary>
        public bool IsBoundary { get; init; }

        /// <summary>Opening number (1..n) for opening edges, 0 for boundary edges.</summary>
        public int OpeningIndex { get; init; }

        /// <summary>True for straight lines.</summary>
        public bool IsLine => Segment.IsStraight;
    }

    /// <summary>The plan geometry of one roof and its existing shape points.</summary>
    public sealed class RoofOutline
    {
        /// <summary>Outer boundary loops (one per roof island).</summary>
        public List<Loop2D> Boundaries { get; } = new List<Loop2D>();

        /// <summary>Openings, numbered north to south, then west to east.</summary>
        public List<OpeningModel> Openings { get; } = new List<OpeningModel>();

        /// <summary>Every boundary and opening edge.</summary>
        public List<RoofEdge> Edges { get; } = new List<RoofEdge>();

        /// <summary>Plan positions of the roof's shape points (empty when shape editing is off).</summary>
        public List<P2> ExistingVertices { get; set; } = new List<P2>();

        /// <summary>Number of openings Auto Slope By Drain V013's detection found (its drain report counts them).</summary>
        public int DetectedDrainCount { get; set; }

        /// <summary>Things worth telling the user (unclosed sketch loops, shape editing off …).</summary>
        public List<string> Notes { get; } = new List<string>();
    }
}
