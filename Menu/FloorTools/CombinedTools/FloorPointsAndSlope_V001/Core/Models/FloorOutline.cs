// =======================================================
// File: FloorOutline.cs
// Location: Core/Models/
// What the tool knows about one floor, in plan: its boundary loops, its
// openings, every edge (one per sketch curve) and the shape points it
// already has. Read by Infrastructure/Revit/FloorReader; used by the
// planner without any Revit call, so the window can re-plan live.
// =======================================================

using Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Geometry;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Core.Models
{
    /// <summary>One edge of the floor in plan: a boundary or opening curve.</summary>
    public sealed class FloorEdge
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

    /// <summary>The plan geometry of one floor and its existing shape points.</summary>
    public sealed class FloorOutline
    {
        /// <summary>Outer boundary loops (one per floor island).</summary>
        public List<Loop2D> Boundaries { get; } = new List<Loop2D>();

        /// <summary>Openings, numbered north to south, then west to east.</summary>
        public List<OpeningModel> Openings { get; } = new List<OpeningModel>();

        /// <summary>Every boundary and opening edge.</summary>
        public List<FloorEdge> Edges { get; } = new List<FloorEdge>();

        /// <summary>Plan positions of the floor's shape points (empty when shape editing is off).</summary>
        public List<P2> ExistingVertices { get; set; } = new List<P2>();

        /// <summary>Height difference between the highest and lowest shape point, in mm (0 = flat).</summary>
        public double ExistingSlopeMm { get; set; }

        /// <summary>Number of openings Floor Slope By Drain V001's detection found (its drain report counts them).</summary>
        public int DetectedDrainCount { get; set; }

        /// <summary>Things worth telling the user (unclosed sketch loops, shape editing off …).</summary>
        public List<string> Notes { get; } = new List<string>();
    }
}
