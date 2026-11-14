// Merge Roofs V001 — Revit-free footprint model used by the analysis/union code (units: Revit internal feet).
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.MergeRoofs.V001.Core.Models
{
    /// <summary>A 2D point in plan, in feet.</summary>
    public readonly record struct Pt(double X, double Y);

    /// <summary>One footprint edge. <see cref="ArcMid"/> non-null means an arc through A, ArcMid, B.</summary>
    public sealed class Edge
    {
        /// <summary>Start point.</summary>
        public Pt A { get; init; }
        /// <summary>End point.</summary>
        public Pt B { get; init; }
        /// <summary>A point on the arc between A and B; null for a straight edge.</summary>
        public Pt? ArcMid { get; init; }
        /// <summary>True when the edge defines roof slope.</summary>
        public bool DefinesSlope { get; init; }
        /// <summary>Slope angle in radians (Revit's slope angle value) when <see cref="DefinesSlope"/>.</summary>
        public double SlopeAngle { get; init; }
    }

    /// <summary>A closed loop of edges.</summary>
    public sealed class Loop
    {
        /// <summary>Creates a loop.</summary>
        public Loop(IReadOnlyList<Edge> edges) { Edges = edges; }
        /// <summary>The loop's edges, in order.</summary>
        public IReadOnlyList<Edge> Edges { get; }
    }

    /// <summary>A roof footprint: one outer loop and any interior loops (holes).</summary>
    public sealed class RoofFootprint
    {
        /// <summary>Creates a footprint.</summary>
        public RoofFootprint(string id, Loop outer, IReadOnlyList<Loop> holes) { Id = id; Outer = outer; Holes = holes; }
        /// <summary>Identifier shown in messages (the roof's element id).</summary>
        public string Id { get; }
        /// <summary>Outer boundary.</summary>
        public Loop Outer { get; }
        /// <summary>Interior loops.</summary>
        public IReadOnlyList<Loop> Holes { get; }
    }

    /// <summary>Kind of problem found between footprints.</summary>
    public enum IssueKind { Overlap, Intersection, Gap, NotJoined }

    /// <summary>One problem with its plan location.</summary>
    public sealed record Issue(IssueKind Kind, Pt Location, string Message);

    /// <summary>A connecting line drawn across a snapped gap.</summary>
    public sealed record GapLink(Pt A, Pt B);

    /// <summary>Outcome of analysing and merging footprints.</summary>
    public sealed class MergeResult
    {
        /// <summary>Outer loops of the merged outline (one when the roofs are joined).</summary>
        public IReadOnlyList<Loop> Outer { get; init; } = new List<Loop>();
        /// <summary>Interior loops of the merged outline.</summary>
        public IReadOnlyList<Loop> Holes { get; init; } = new List<Loop>();
        /// <summary>Problems found.</summary>
        public IReadOnlyList<Issue> Issues { get; init; } = new List<Issue>();
        /// <summary>Connecting lines across snapped gaps.</summary>
        public IReadOnlyList<GapLink> GapLinks { get; init; } = new List<GapLink>();
        /// <summary>True when exactly one outer loop exists and nothing is <see cref="IssueKind.NotJoined"/>.</summary>
        public bool CanCreate { get; init; }
    }
}
