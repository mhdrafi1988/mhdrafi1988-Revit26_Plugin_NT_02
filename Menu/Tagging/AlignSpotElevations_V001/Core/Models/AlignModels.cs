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
        OffsetFromPoint,

        /// <summary>Slide the bend points, leader ends and / or text vertically or horizontally onto a picked
        /// detail line (one optional line per part).</summary>
        AlignToDetailLines
    }

    /// <summary>How a part slides onto its detail line.</summary>
    public enum LineMove
    {
        /// <summary>Vertical or horizontal, whichever reaches the line (the shorter move when both do).</summary>
        Auto,

        /// <summary>Up or down: the part's X stays.</summary>
        Vertical,

        /// <summary>Left or right: the part's Y stays.</summary>
        Horizontal
    }

    /// <summary>
    /// A straight detail line in paper mm, in the view's axes (U = right, V = up), from A to B.
    /// </summary>
    public sealed class DetailLine2D
    {
        /// <summary>Element id value of the detail line (0 when not from Revit).</summary>
        public long Id { get; set; }

        /// <summary>Start, along the view's right direction (paper mm).</summary>
        public double AU { get; set; }

        /// <summary>Start, along the view's up direction (paper mm).</summary>
        public double AV { get; set; }

        /// <summary>End, along the view's right direction (paper mm).</summary>
        public double BU { get; set; }

        /// <summary>End, along the view's up direction (paper mm).</summary>
        public double BV { get; set; }

        /// <summary>Length on paper, mm.</summary>
        public double Length => System.Math.Sqrt((BU - AU) * (BU - AU) + (BV - AV) * (BV - AV));

        /// <summary>A line closer than this to vertical / horizontal (as a share of its length) counts as exactly so.</summary>
        public const double StraightTolerance = 1e-6;

        /// <summary>True when the line runs straight up and down, so a vertical move can never reach it.</summary>
        public bool IsVertical => System.Math.Abs(BU - AU) <= StraightTolerance * System.Math.Max(Length, 1e-9);

        /// <summary>True when the line runs straight left to right, so a horizontal move can never reach it.</summary>
        public bool IsHorizontal => System.Math.Abs(BV - AV) <= StraightTolerance * System.Math.Max(Length, 1e-9);

        /// <summary>Short description, e.g. "sloped, 40° · 142 mm".</summary>
        public string Describe()
        {
            string shape;
            if (IsVertical) shape = "vertical";
            else if (IsHorizontal) shape = "horizontal";
            else
            {
                double deg = System.Math.Atan2(BV - AV, BU - AU) * 180 / System.Math.PI;
                if (deg < 0) deg += 180;
                shape = $"sloped, {deg:0}°";
            }
            return $"{shape} · {Length:0} mm";
        }
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

    /// <summary>A part of a spot elevation that the tool can move.</summary>
    public enum SpotPart
    {
        /// <summary>The tag text.</summary>
        Text,

        /// <summary>The leader end point (where the leader meets the text).</summary>
        LeaderEnd,

        /// <summary>The leader bend point (shoulder).</summary>
        LeaderShoulder
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

        /// <summary>True when the spot has a leader, so <see cref="EndU"/> / <see cref="EndV"/> are known.</summary>
        public bool HasEnd { get; set; }

        /// <summary>Leader end point along the view's right direction (paper mm).</summary>
        public double EndU { get; set; }

        /// <summary>Leader end point along the view's up direction (paper mm).</summary>
        public double EndV { get; set; }

        /// <summary>True when the leader has a bend point (shoulder).</summary>
        public bool HasShoulder { get; set; }

        /// <summary>Leader bend point along the view's right direction (paper mm).</summary>
        public double ShoulderU { get; set; }

        /// <summary>Leader bend point along the view's up direction (paper mm).</summary>
        public double ShoulderV { get; set; }

        /// <summary>Estimated tag text width (paper mm); 0 when unknown.</summary>
        public double WidthMm { get; set; }

        /// <summary>Estimated tag text height (paper mm); 0 when unknown.</summary>
        public double HeightMm { get; set; }
    }

    /// <summary>
    /// Every option of the tool. Also the saved settings file (<see cref="ReferenceId"/> is not saved),
    /// so new fields need defaults that keep the old behaviour.
    /// </summary>
    public sealed class AlignOptions
    {
        /// <summary>Settings file format version.</summary>
        public const int CurrentVersion = 3;

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

        /// <summary>Move the tag text (on by default, the only choice before version 2).</summary>
        public bool MoveText { get; set; } = true;

        /// <summary>Move the leader end points. Spots without a leader keep theirs.</summary>
        public bool MoveLeaderEnd { get; set; }

        /// <summary>Move the leader bend points (shoulders). Leaders without a bend keep theirs.</summary>
        public bool MoveLeaderShoulder { get; set; }

        /// <summary>Leader end offset to the right of its point, paper mm (Offset from point).</summary>
        public double EndOffsetXMm { get; set; } = 8.0;

        /// <summary>Leader end offset above its point, paper mm (Offset from point).</summary>
        public double EndOffsetYMm { get; set; } = 3.0;

        /// <summary>Bend point offset to the right of its point, paper mm (Offset from point).</summary>
        public double ShoulderOffsetXMm { get; set; } = 4.0;

        /// <summary>Bend point offset above its point, paper mm (Offset from point).</summary>
        public double ShoulderOffsetYMm { get; set; } = 3.0;

        /// <summary>How the text slides onto its detail line (Align to detail lines). Version 3.</summary>
        public LineMove TextLineMove { get; set; } = LineMove.Auto;

        /// <summary>How the leader ends slide onto their detail line (Align to detail lines). Version 3.</summary>
        public LineMove EndLineMove { get; set; } = LineMove.Auto;

        /// <summary>How the bend points slide onto their detail line (Align to detail lines). Version 3.</summary>
        public LineMove ShoulderLineMove { get; set; } = LineMove.Auto;

        /// <summary>Treat the detail lines as endless. Off (default): a part whose move would land past
        /// an end of its line is skipped. Version 3.</summary>
        public bool ExtendLines { get; set; }

        /// <summary>Detail line for the text (Align to detail lines), or null. Not saved.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DetailLine2D TextLine { get; set; }

        /// <summary>Detail line for the leader ends (Align to detail lines), or null. Not saved.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DetailLine2D EndLine { get; set; }

        /// <summary>Detail line for the bend points (Align to detail lines), or null. Not saved.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DetailLine2D ShoulderLine { get; set; }

        /// <summary>True when at least one detail line is picked.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasAnyDetailLine => TextLine != null || EndLine != null || ShoulderLine != null;

        /// <summary>True when at least one part is chosen.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool MovesAnything => MoveText || MoveLeaderEnd || MoveLeaderShoulder;

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

        /// <summary>True when the reference tag lacks this part, so the first tag that has it was used.</summary>
        public bool ReferenceMissing { get; set; }
    }

    /// <summary>The planned move of one tag.</summary>
    public sealed class PlannedMove
    {
        /// <summary>The tag.</summary>
        public SpotItem Item { get; set; }

        /// <summary>Text move to the right, paper mm (signed). When text is not chosen this is the estimated
        /// move Revit makes when the leader end moves.</summary>
        public double DeltaU { get; set; }

        /// <summary>Text move up, paper mm (signed). See <see cref="DeltaU"/>.</summary>
        public double DeltaV { get; set; }

        /// <summary>Leader end move to the right, paper mm. When the end is not chosen this is the
        /// estimated move Revit makes when the bend point moves.</summary>
        public double EndDeltaU { get; set; }

        /// <summary>Leader end move up, paper mm. See <see cref="EndDeltaU"/>.</summary>
        public double EndDeltaV { get; set; }

        /// <summary>Bend point move to the right, paper mm.</summary>
        public double ShoulderDeltaU { get; set; }

        /// <summary>Bend point move up, paper mm.</summary>
        public double ShoulderDeltaV { get; set; }

        /// <summary>True when the text is set explicitly (text chosen and it moves).</summary>
        public bool SetsText { get; set; }

        /// <summary>True when the leader end is set explicitly (end chosen, the spot has one and it moves).</summary>
        public bool SetsEnd { get; set; }

        /// <summary>True when the bend point is set explicitly.</summary>
        public bool SetsShoulder { get; set; }

        /// <summary>True when the leader end cannot reach its line: with a bend, Revit only lets the end
        /// slide along the bend-to-end segment. The end deltas are then the reachable move.</summary>
        public bool EndLimited { get; set; }

        /// <summary>True when this tag's box would overlap another selected tag after the move.</summary>
        public bool Overlaps { get; set; }

        /// <summary>What happened to each part (Align to detail lines), e.g.
        /// "bend: moved vertically · end: on line". Empty in the other modes.</summary>
        public string LineResult { get; set; } = string.Empty;

        /// <summary>True when a part of this spot could not reach its detail line and was skipped.</summary>
        public bool LineSkipped { get; set; }

        /// <summary>True when the tag actually moves.</summary>
        public bool Moves => SetsText || SetsEnd || SetsShoulder;
    }

    /// <summary>The full result of a plan: one entry per tag (including tags that stay) plus notes.</summary>
    public sealed class ArrangePlan
    {
        /// <summary>One entry per tag, in the order given.</summary>
        public List<PlannedMove> Entries { get; } = new List<PlannedMove>();

        /// <summary>The line target of the first chosen part (Align to line only).</summary>
        public TargetResult Target { get; set; }

        /// <summary>The line target of each chosen part (Align to line only).</summary>
        public Dictionary<SpotPart, TargetResult> Targets { get; } = new Dictionary<SpotPart, TargetResult>();

        /// <summary>Tags without a leader while the leader end was chosen (their end stays).</summary>
        public int MissingEndCount { get; set; }

        /// <summary>Tags without a bend point while the bend point was chosen (theirs stays).</summary>
        public int MissingShoulderCount { get; set; }

        /// <summary>Leader ends that cannot reach their line because of the bend (see <see cref="PlannedMove.EndLimited"/>).</summary>
        public int EndLimitedCount { get; set; }

        /// <summary>Number of tags whose box would overlap another selected tag after the move.</summary>
        public int OverlapCount { get; set; }

        /// <summary>Spots with at least one part that could not reach its detail line (Align to detail lines).</summary>
        public int LineSkippedCount { get; set; }

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
