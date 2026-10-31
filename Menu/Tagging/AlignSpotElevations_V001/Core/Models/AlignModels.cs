using System.Collections.Generic;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models
{
    /// <summary>Which way the spot elevation tags line up.</summary>
    public enum AlignDirection
    {
        /// <summary>Tags end up in one column (same position left-right on the sheet).</summary>
        Vertical,

        /// <summary>Tags end up in one row (same position up-down on the sheet).</summary>
        Horizontal
    }

    /// <summary>What the tool does to the selected tags.</summary>
    public enum ArrangeMode
    {
        /// <summary>Put the tags on one line (column or row), optionally spaced along it.</summary>
        AlignToLine,

        /// <summary>Only space the tags along the column or row; their other coordinate is kept.</summary>
        DistributeOnly,

        /// <summary>Place every tag the same distance from its own measured point.</summary>
        OffsetFromPoint
    }

    /// <summary>How the common line position is chosen (Align to line).</summary>
    public enum AlignTarget
    {
        /// <summary>Mean of the selected positions.</summary>
        Average,

        /// <summary>Position shared by the most tags (within the tolerance).</summary>
        MostCommon,

        /// <summary>Middle position when sorted.</summary>
        Median,

        /// <summary>Lowest position (left for vertical, bottom for horizontal).</summary>
        Min,

        /// <summary>Highest position (right for vertical, top for horizontal).</summary>
        Max,

        /// <summary>Position of one chosen reference tag.</summary>
        Reference
    }

    /// <summary>How tags are spaced along the column or row.</summary>
    public enum DistributeMode
    {
        /// <summary>Keep the spacing along the line as it is.</summary>
        None,

        /// <summary>First and last tag stay; the others are spaced evenly between them.</summary>
        Even,

        /// <summary>The first tag stays; the others follow at a fixed gap.</summary>
        FixedGap
    }

    /// <summary>
    /// One spot elevation tag. Positions are in paper millimetres in the view's own axes
    /// (U = right, V = up), so tolerances and gaps match the printed drawing at any view scale.
    /// </summary>
    public sealed class SpotItem
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>Text shown in lists, e.g. the elevation text.</summary>
        public string Label { get; set; }

        /// <summary>Tag position along the view's right direction (paper mm).</summary>
        public double U { get; set; }

        /// <summary>Tag position along the view's up direction (paper mm).</summary>
        public double V { get; set; }

        /// <summary>Measured point along the view's right direction (paper mm).</summary>
        public double PointU { get; set; }

        /// <summary>Measured point along the view's up direction (paper mm).</summary>
        public double PointV { get; set; }
    }

    /// <summary>
    /// Every option of the tool. Also the saved settings file (<see cref="ReferenceId"/> is not saved),
    /// so new fields need defaults that keep the old behaviour.
    /// </summary>
    public sealed class AlignOptions
    {
        /// <summary>Settings file format version.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Format version of the saved file.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>What the tool does.</summary>
        public ArrangeMode Mode { get; set; } = ArrangeMode.AlignToLine;

        /// <summary>Column (vertical) or row (horizontal).</summary>
        public AlignDirection Direction { get; set; } = AlignDirection.Vertical;

        /// <summary>How the line position is chosen.</summary>
        public AlignTarget Target { get; set; } = AlignTarget.Average;

        /// <summary>Positions closer than this count as the same, paper mm (Most common).</summary>
        public double ToleranceMm { get; set; } = 1.0;

        /// <summary>Spacing along the line.</summary>
        public DistributeMode Distribute { get; set; } = DistributeMode.None;

        /// <summary>Gap between tags for <see cref="DistributeMode.FixedGap"/>, paper mm.</summary>
        public double GapMm { get; set; } = 6.0;

        /// <summary>Tag offset to the right of its point, paper mm (Offset from point).</summary>
        public double OffsetXMm { get; set; } = 8.0;

        /// <summary>Tag offset above its point, paper mm (Offset from point).</summary>
        public double OffsetYMm { get; set; } = 3.0;

        /// <summary>Reference tag id for <see cref="AlignTarget.Reference"/>. Not saved.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public long? ReferenceId { get; set; }

        /// <summary>A copy of these options.</summary>
        public AlignOptions Clone() => (AlignOptions)MemberwiseClone();
    }

    /// <summary>The common line position the engine picked.</summary>
    public sealed class TargetResult
    {
        /// <summary>The common position (paper mm, along the aligned axis).</summary>
        public double Value { get; set; }

        /// <summary>True when "Most common" found no shared position and the average was used.</summary>
        public bool FellBackToAverage { get; set; }

        /// <summary>Number of tags that share the chosen position (Most common only).</summary>
        public int SharedCount { get; set; }
    }

    /// <summary>The planned move of one tag.</summary>
    public sealed class PlannedMove
    {
        /// <summary>The tag.</summary>
        public SpotItem Item { get; set; }

        /// <summary>Move to the right, paper mm (signed).</summary>
        public double DeltaU { get; set; }

        /// <summary>Move up, paper mm (signed).</summary>
        public double DeltaV { get; set; }

        /// <summary>True when the tag actually moves.</summary>
        public bool Moves => System.Math.Abs(DeltaU) > 1e-6 || System.Math.Abs(DeltaV) > 1e-6;
    }

    /// <summary>The full result of a plan: one entry per tag (including tags that stay) plus notes.</summary>
    public sealed class ArrangePlan
    {
        /// <summary>One entry per tag, in the order given.</summary>
        public List<PlannedMove> Entries { get; } = new List<PlannedMove>();

        /// <summary>The line target (Align to line only).</summary>
        public TargetResult Target { get; set; }

        /// <summary>Number of tags that would end up on top of another tag.</summary>
        public int StackedCount { get; set; }

        /// <summary>Number of tags that move.</summary>
        public int MoveCount
        {
            get { int n = 0; foreach (var e in Entries) if (e.Moves) n++; return n; }
        }
    }

    /// <summary>Result of reading the selection: usable tags plus the ones skipped, each with a reason.</summary>
    public sealed class SpotReadResult
    {
        /// <summary>Tags that can be moved.</summary>
        public List<SpotItem> Items { get; } = new List<SpotItem>();

        /// <summary>Skipped tags: "Element 123: reason".</summary>
        public List<string> Skipped { get; } = new List<string>();

        /// <summary>View scale (e.g. 100 for 1:100), used to turn feet into paper mm and back.</summary>
        public int Scale { get; set; } = 1;
    }
}
