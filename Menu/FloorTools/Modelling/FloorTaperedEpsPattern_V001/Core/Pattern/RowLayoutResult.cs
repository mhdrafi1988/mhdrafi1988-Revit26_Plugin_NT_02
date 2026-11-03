using System;
using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Boards and grid lines of the contour-row layout (all slope faces).</summary>
    public sealed class RowLayoutResult
    {
        public IReadOnlyList<BoardPiece> Boards { get; init; } = Array.Empty<BoardPiece>();
        public IReadOnlyList<PatternSegment> GridLines { get; init; } = Array.Empty<PatternSegment>();

        /// <summary>Cut pieces narrower (along the row) than the minimum piece size.</summary>
        public int SliverCount { get; init; }

        /// <summary>Per-face notes, e.g. which D variant Auto chose.</summary>
        public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    }
}
