using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>Everything the preview, the metric tiles and the Revit writer need.</summary>
    public sealed class Pattern2DResult
    {
        public bool IsValid => Errors.Count == 0;
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        public Polygon2D Outline { get; init; } = Polygon2D.Empty;
        /// <summary>The single grid frame; null in "each edge" mode (one frame per slope face).</summary>
        public GridFrame? Frame { get; init; }
        public IReadOnlyList<SlopeFace> Faces { get; init; } = Array.Empty<SlopeFace>();
        public IReadOnlyList<BoardPiece> Boards { get; init; } = Array.Empty<BoardPiece>();
        public IReadOnlyList<ContourLoop> Contours { get; init; } = Array.Empty<ContourLoop>();
        public IReadOnlyList<PatternSegment> Segments { get; init; } = Array.Empty<PatternSegment>();
        public IReadOnlyList<PatternLabel> Labels { get; init; } = Array.Empty<PatternLabel>();

        public double MaxThicknessMm { get; init; }

        /// <summary>Contour pieces not drawn because a grid line lies exactly on them.</summary>
        public int ContoursOnGridLines { get; init; }

        /// <summary>Filled regions to draw (empty when hatching is off).</summary>
        public IReadOnlyList<HatchRegion> Hatches { get; init; } = Array.Empty<HatchRegion>();

        /// <summary>Legend for the hatching: slot, meaning, piece count.</summary>
        public IReadOnlyList<LegendEntry> Legend { get; init; } = Array.Empty<LegendEntry>();

        /// <summary>Number of colour slots used by the hatching.</summary>
        public int HatchSlotCount => Legend.Count == 0 ? 0 : Legend.Max(l => l.Slot) + 1;

        /// <summary>Cut pieces narrower (along the row) than the minimum piece size.</summary>
        public int SliverCount { get; init; }

        /// <summary>Per-face notes from the row layout, e.g. "Edge 1: D2 (board centred)".</summary>
        public IReadOnlyList<string> LayoutNotes { get; init; } = Array.Empty<string>();

        public int BoardCount => Boards.Count;
        public int CodeCount => Boards.Select(b => b.Code).Distinct().Count();

        public IEnumerable<PatternSegment> SegmentsOf(PatternLayer layer) => Segments.Where(s => s.Layer == layer);

        public LayerStats StatsOf(PatternLayer layer)
        {
            List<PatternSegment> list = SegmentsOf(layer).ToList();
            return new LayerStats(layer, list.Count, list.Sum(s => s.Segment.Length));
        }

        public IReadOnlyList<LayerStats> AllStats() =>
            Enum.GetValues(typeof(PatternLayer)).Cast<PatternLayer>().Select(StatsOf).ToList();

        public static Pattern2DResult Failed(IReadOnlyList<string> errors) => new Pattern2DResult { Errors = errors };
    }
}
