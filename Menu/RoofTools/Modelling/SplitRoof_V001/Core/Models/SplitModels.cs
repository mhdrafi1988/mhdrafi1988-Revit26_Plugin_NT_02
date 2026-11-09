// Split Roof V001 — Revit-free models for cut lines, result pieces and settings (units: Revit internal feet).
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.SplitRoof.V001.Core.Models
{
    /// <summary>A straight cut line in plan. The line is extended past its ends when cutting.</summary>
    public sealed record CutLine(string Id, Pt A, Pt B);

    /// <summary>One resulting roof piece: an outer loop and its interior loops, with slopes copied from the source roof.</summary>
    public sealed class SplitPiece
    {
        /// <summary>Outer boundary.</summary>
        public Loop Outer { get; init; } = new Loop(new List<Edge>());
        /// <summary>Interior loops.</summary>
        public IReadOnlyList<Loop> Holes { get; init; } = new List<Loop>();
        /// <summary>Plan area in square feet.</summary>
        public double AreaFeet { get; init; }
        /// <summary>True when at least one edge defines slope; a piece without one would be created flat.</summary>
        public bool HasSlope { get; init; }
    }

    /// <summary>Outcome of splitting a footprint.</summary>
    public sealed class SplitResult
    {
        /// <summary>The pieces, in a stable order (left-to-right, bottom-to-top).</summary>
        public IReadOnlyList<SplitPiece> Pieces { get; init; } = new List<SplitPiece>();
        /// <summary>Messages: cut lines that miss the roof, flat pieces, dropped slivers.</summary>
        public IReadOnlyList<string> Notes { get; init; } = new List<string>();
        /// <summary>True when the roof was divided into at least two pieces.</summary>
        public bool CanCreate => Pieces.Count >= 2;
    }

    /// <summary>User settings for the Split Roof window (versioned JSON).</summary>
    public sealed class SplitSettings
    {
        /// <summary>Settings file format version.</summary>
        public int Version { get; set; } = 1;
        /// <summary>Delete the original roof after the pieces are created (default: keep it).</summary>
        public bool DeleteOriginal { get; set; }
    }
}
