using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.UI.ViewModels
{
    /// <summary>One line of the preview list. Distances are paper mm.</summary>
    public sealed class PreviewRow
    {
        /// <summary>Tag label.</summary>
        public string Label { get; set; }

        /// <summary>Move to the right (negative = left), e.g. "+4.2", or "–".</summary>
        public string MoveX { get; set; }

        /// <summary>Move up (negative = down), e.g. "-6.0", or "–".</summary>
        public string MoveY { get; set; }

        /// <summary>Leader end move "X, Y", "–", or "no leader".</summary>
        public string EndMove { get; set; }

        /// <summary>Bend point move "X, Y", "–", or "no bend".</summary>
        public string BendMove { get; set; }

        /// <summary>True when the leader end stops short of its line (see the warning).</summary>
        public bool EndLimited { get; set; }

        /// <summary>True when the tag stays where it is.</summary>
        public bool Stays { get; set; }

        /// <summary>True when the tag would overlap another selected tag (estimated).</summary>
        public bool Overlaps { get; set; }

        /// <summary>What happened to each part (Align to detail lines only).</summary>
        public string Result { get; set; }
    }

    /// <summary>Options, live preview and result of the Align Spot Elevations window.</summary>
    public sealed class AlignViewModel : ObservableObject
    {
        private readonly IReadOnlyList<SpotItem> _items;
        private readonly AlignOptions _o;
        private SpotItem _reference;
        private string _summary = string.Empty;
        private string _warning = string.Empty;
        private bool _canApply;

        /// <summary>Creates the view model for the readable tags, starting from <paramref name="options"/>.</summary>
        public AlignViewModel(IReadOnlyList<SpotItem> items, IReadOnlyList<string> skipped, int scale, AlignOptions options)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            Skipped = skipped ?? new List<string>();
            Scale = Math.Max(1, scale);
            _o = (options ?? new AlignOptions()).Clone();
            if (_o.Mode == ArrangeMode.DistributeOnly && _o.Distribute == DistributeMode.None) _o.Distribute = DistributeMode.Even;
            _reference = items.FirstOrDefault();
            FitGapCommand = new RelayCommand(FitGap, () => SuggestedGapMm.HasValue);
            BendLine = new LinePickViewModel(SpotPart.LeaderShoulder, "Bend point line · optional",
                () => _o.ShoulderLineMove, m => _o.ShoulderLineMove = m, RequestPick, Refresh);
            EndLine = new LinePickViewModel(SpotPart.LeaderEnd, "End point line · optional",
                () => _o.EndLineMove, m => _o.EndLineMove = m, RequestPick, Refresh);
            TextLine = new LinePickViewModel(SpotPart.Text, "Text line · optional",
                () => _o.TextLineMove, m => _o.TextLineMove = m, RequestPick, Refresh);
            Refresh();
        }

        // ── Align to detail lines ───────────────────────────────────────────

        /// <summary>Detail line for the leader bend points.</summary>
        public LinePickViewModel BendLine { get; }

        /// <summary>Detail line for the leader end points.</summary>
        public LinePickViewModel EndLine { get; }

        /// <summary>Detail line for the text.</summary>
        public LinePickViewModel TextLine { get; }

        /// <summary>
        /// The part whose line the user asked to pick, or null. The window closes when this is set; the
        /// command then lets the user pick a detail line, calls <see cref="SetPickedLine"/> and shows the
        /// window again.
        /// </summary>
        public SpotPart? PendingPick { get; private set; }

        /// <summary>Raised when the user clicks Pick line…; the window closes itself.</summary>
        public event EventHandler PickRequested;

        private void RequestPick(SpotPart part)
        {
            PendingPick = part;
            PickRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Stores the line picked for <see cref="PendingPick"/> (null keeps the old one) and clears the request.</summary>
        public void SetPickedLine(DetailLine2D line)
        {
            SpotPart? part = PendingPick;
            PendingPick = null;
            if (line == null || !part.HasValue) return;
            LineFor(part.Value).Line = line;
        }

        /// <summary>Puts <paramref name="line"/> on the box for <paramref name="part"/> (e.g. a detail line selected before the command).</summary>
        public void SetLine(SpotPart part, DetailLine2D line) => LineFor(part).Line = line;

        private LinePickViewModel LineFor(SpotPart part)
            => part == SpotPart.Text ? TextLine : part == SpotPart.LeaderEnd ? EndLine : BendLine;

        /// <summary>Radio: Align to detail lines.</summary>
        public bool IsDetailLineMode { get => _o.Mode == ArrangeMode.AlignToDetailLines; set { if (value) SetMode(ArrangeMode.AlignToDetailLines); } }

        /// <summary>True when the Move checkboxes apply (every mode except Align to detail lines).</summary>
        public bool ShowsMoveParts => _o.Mode != ArrangeMode.AlignToDetailLines;

        /// <summary>Radio: skip parts whose move would land past an end of their line (default).</summary>
        public bool IsSkipPastEnd { get => !_o.ExtendLines; set { if (value) Set(() => _o.ExtendLines = false); } }

        /// <summary>Radio: treat the detail lines as endless.</summary>
        public bool IsExtendLines { get => _o.ExtendLines; set { if (value) Set(() => _o.ExtendLines = true); } }

        /// <summary>Sets a fixed gap that keeps the tags apart (tag height for a column, width for a row, + 1 mm).</summary>
        public IRelayCommand FitGapCommand { get; }

        /// <summary>The gap <see cref="FitGapCommand"/> would set, or null when no tag size is known.</summary>
        public double? SuggestedGapMm => AlignEngine.SuggestGap(_items, _o.Direction);

        /// <summary>Tooltip for the Fit button.</summary>
        public string FitGapTip => SuggestedGapMm.HasValue
            ? $"Set the gap to {SuggestedGapMm:0.#} mm: the {(_o.Direction == AlignDirection.Vertical ? "tallest" : "widest")} tag plus 1 mm (estimated from the text size)."
            : "Tag sizes are unknown for this selection.";

        private void FitGap()
        {
            double? gap = SuggestedGapMm;
            if (!gap.HasValue) return;
            _o.GapMm = gap.Value;
            _o.Distribute = DistributeMode.FixedGap;
            RaiseAll();
        }

        /// <summary>Tags that can be arranged.</summary>
        public IReadOnlyList<SpotItem> Items => _items;

        /// <summary>Skipped tags with reasons.</summary>
        public IReadOnlyList<string> Skipped { get; }

        /// <summary>True when something was skipped.</summary>
        public bool HasSkipped => Skipped.Count > 0;

        /// <summary>View scale (100 for 1:100).</summary>
        public int Scale { get; }

        /// <summary>Header text, e.g. "6 spot elevations · view 1:100".</summary>
        public string Header => $"{_items.Count} spot elevation(s) selected · view 1:{Scale}. Distances are paper mm. The measured point never moves.";

        /// <summary>Preview rows.</summary>
        public ObservableCollection<PreviewRow> Rows { get; } = new ObservableCollection<PreviewRow>();

        /// <summary>The current plan.</summary>
        public ArrangePlan Plan { get; private set; } = new ArrangePlan();

        /// <summary>A copy of the current options, for saving and applying.</summary>
        public AlignOptions Options
        {
            get
            {
                var c = _o.Clone();
                c.ReferenceId = _reference?.Id;
                c.ShoulderLine = BendLine?.Line;
                c.EndLine = EndLine?.Line;
                c.TextLine = TextLine?.Line;
                return c;
            }
        }

        // ── Mode ────────────────────────────────────────────────────────────

        /// <summary>Radio: Align to a line.</summary>
        public bool IsAlignMode { get => _o.Mode == ArrangeMode.AlignToLine; set { if (value) SetMode(ArrangeMode.AlignToLine); } }

        /// <summary>Radio: Distribute only.</summary>
        public bool IsDistributeMode { get => _o.Mode == ArrangeMode.DistributeOnly; set { if (value) SetMode(ArrangeMode.DistributeOnly); } }

        /// <summary>Radio: Offset from point.</summary>
        public bool IsOffsetMode { get => _o.Mode == ArrangeMode.OffsetFromPoint; set { if (value) SetMode(ArrangeMode.OffsetFromPoint); } }

        /// <summary>True when direction and spacing options apply.</summary>
        public bool UsesLine => _o.Mode == ArrangeMode.AlignToLine || _o.Mode == ArrangeMode.DistributeOnly;

        /// <summary>True when "Keep spacing" can be chosen (not in Distribute only).</summary>
        public bool CanKeepSpacing => _o.Mode == ArrangeMode.AlignToLine;

        private void SetMode(ArrangeMode mode)
        {
            if (_o.Mode == mode) return;
            _o.Mode = mode;
            if (mode == ArrangeMode.DistributeOnly && _o.Distribute == DistributeMode.None) _o.Distribute = DistributeMode.Even;
            RaiseAll();
        }

        // ── Parts to move ───────────────────────────────────────────────────

        /// <summary>Checkbox: move the tag text.</summary>
        public bool MoveText { get => _o.MoveText; set => Set(() => _o.MoveText = value); }

        /// <summary>Checkbox: move the leader end points.</summary>
        public bool MoveLeaderEnd { get => _o.MoveLeaderEnd; set => Set(() => _o.MoveLeaderEnd = value); }

        /// <summary>Checkbox: move the leader bend points (shoulders).</summary>
        public bool MoveLeaderShoulder { get => _o.MoveLeaderShoulder; set => Set(() => _o.MoveLeaderShoulder = value); }

        /// <summary>Label of the leader end checkbox, with how many tags have a leader.</summary>
        public string LeaderEndLabel => $"Leader end point  ({_items.Count(i => i.HasEnd)} of {_items.Count} have a leader)";

        /// <summary>Label of the bend point checkbox, with how many leaders have a bend.</summary>
        public string LeaderShoulderLabel => $"Leader bend point (shoulder)  ({_items.Count(i => i.HasShoulder)} of {_items.Count} have one)";

        /// <summary>Note under the checkboxes on how parts that are not ticked behave.</summary>
        public string FollowNote => "Parts that are not ticked follow, as in Revit: the text moves with the leader end, " +
                                    "and the end and text move with the bend point.";

        // ── Direction ───────────────────────────────────────────────────────

        /// <summary>Radio: one column.</summary>
        public bool IsVertical { get => _o.Direction == AlignDirection.Vertical; set { if (value) Set(() => _o.Direction = AlignDirection.Vertical); } }

        /// <summary>Radio: one row.</summary>
        public bool IsHorizontal { get => _o.Direction == AlignDirection.Horizontal; set { if (value) Set(() => _o.Direction = AlignDirection.Horizontal); } }

        // ── Target ──────────────────────────────────────────────────────────

        /// <summary>Radio: Average.</summary>
        public bool IsAverage { get => _o.Target == AlignTarget.Average; set { if (value) Set(() => _o.Target = AlignTarget.Average); } }

        /// <summary>Radio: Most common.</summary>
        public bool IsMostCommon { get => _o.Target == AlignTarget.MostCommon; set { if (value) Set(() => _o.Target = AlignTarget.MostCommon); } }

        /// <summary>Radio: Median.</summary>
        public bool IsMedian { get => _o.Target == AlignTarget.Median; set { if (value) Set(() => _o.Target = AlignTarget.Median); } }

        /// <summary>Radio: Min.</summary>
        public bool IsMin { get => _o.Target == AlignTarget.Min; set { if (value) Set(() => _o.Target = AlignTarget.Min); } }

        /// <summary>Radio: Max.</summary>
        public bool IsMax { get => _o.Target == AlignTarget.Max; set { if (value) Set(() => _o.Target = AlignTarget.Max); } }

        /// <summary>Radio: Reference tag.</summary>
        public bool IsReference { get => _o.Target == AlignTarget.Reference; set { if (value) Set(() => _o.Target = AlignTarget.Reference); } }

        /// <summary>Tolerance for "Most common", paper mm.</summary>
        public double ToleranceMm { get => _o.ToleranceMm; set => SetNumber(() => _o.ToleranceMm = Math.Max(0, value)); }

        /// <summary>The reference tag.</summary>
        public SpotItem Reference
        {
            get => _reference;
            set { if (SetProperty(ref _reference, value)) Refresh(); }
        }

        // ── Spacing ─────────────────────────────────────────────────────────

        /// <summary>Radio: keep spacing.</summary>
        public bool IsKeepSpacing { get => _o.Distribute == DistributeMode.None; set { if (value) Set(() => _o.Distribute = DistributeMode.None); } }

        /// <summary>Radio: even spacing.</summary>
        public bool IsEvenSpacing { get => _o.Distribute == DistributeMode.Even; set { if (value) Set(() => _o.Distribute = DistributeMode.Even); } }

        /// <summary>Radio: fixed gap.</summary>
        public bool IsFixedGap { get => _o.Distribute == DistributeMode.FixedGap; set { if (value) Set(() => _o.Distribute = DistributeMode.FixedGap); } }

        /// <summary>Gap for fixed spacing, paper mm.</summary>
        public double GapMm { get => _o.GapMm; set => SetNumber(() => _o.GapMm = Math.Max(0, value)); }

        // ── Offset ──────────────────────────────────────────────────────────

        /// <summary>Tag offset to the right of its point, paper mm.</summary>
        public double OffsetXMm { get => _o.OffsetXMm; set => SetNumber(() => _o.OffsetXMm = value); }

        /// <summary>Tag offset above its point, paper mm.</summary>
        public double OffsetYMm { get => _o.OffsetYMm; set => SetNumber(() => _o.OffsetYMm = value); }

        /// <summary>Leader end offset to the right of its point, paper mm.</summary>
        public double EndOffsetXMm { get => _o.EndOffsetXMm; set => SetNumber(() => _o.EndOffsetXMm = value); }

        /// <summary>Leader end offset above its point, paper mm.</summary>
        public double EndOffsetYMm { get => _o.EndOffsetYMm; set => SetNumber(() => _o.EndOffsetYMm = value); }

        /// <summary>Bend point offset to the right of its point, paper mm.</summary>
        public double ShoulderOffsetXMm { get => _o.ShoulderOffsetXMm; set => SetNumber(() => _o.ShoulderOffsetXMm = value); }

        /// <summary>Bend point offset above its point, paper mm.</summary>
        public double ShoulderOffsetYMm { get => _o.ShoulderOffsetYMm; set => SetNumber(() => _o.ShoulderOffsetYMm = value); }

        // ── Result ──────────────────────────────────────────────────────────

        /// <summary>One-line result under the options.</summary>
        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

        /// <summary>Warning line (overlapping tags), empty when none.</summary>
        public string Warning { get => _warning; private set { if (SetProperty(ref _warning, value)) OnPropertyChanged(nameof(HasWarning)); } }

        /// <summary>True when <see cref="Warning"/> has text.</summary>
        public bool HasWarning => !string.IsNullOrEmpty(_warning);

        /// <summary>True when at least one tag will move.</summary>
        public bool CanApply { get => _canApply; private set => SetProperty(ref _canApply, value); }

        private void Set(Action change)
        {
            change();
            RaiseAll();
        }

        /// <summary>
        /// Number boxes refresh the preview only. Raising PropertyChanged for the box being typed in would
        /// make WPF reformat it mid-typing ("1." becomes "1").
        /// </summary>
        private void SetNumber(Action change)
        {
            change();
            Refresh();
        }

        private void RaiseAll()
        {
            OnPropertyChanged(string.Empty); // every binding re-reads; the options are small
            BendLine?.RaiseAll(); EndLine?.RaiseAll(); TextLine?.RaiseAll();
            Refresh();
        }

        private void Refresh()
        {
            Rows.Clear();
            Plan = new ArrangePlan();
            Warning = string.Empty;
            if (_items.Count < 2) { Summary = "Select at least two spot elevations."; CanApply = false; return; }
            if (BendLine == null) return; // still constructing
            if (_o.Mode == ArrangeMode.AlignToDetailLines) { RefreshDetailLines(); return; }
            if (!_o.MovesAnything) { Summary = "Tick at least one part to move: text, leader end or bend point."; CanApply = false; return; }

            Plan = AlignEngine.Plan(_items, Options);
            foreach (PlannedMove m in Plan.Entries)
            {
                Rows.Add(new PreviewRow
                {
                    Label = m.Item.Label,
                    MoveX = Fmt(m.DeltaU),
                    MoveY = Fmt(m.DeltaV),
                    EndMove = m.Item.HasEnd ? Pair(m.EndDeltaU, m.EndDeltaV) + (m.EndLimited ? " *" : "") : "no leader",
                    BendMove = m.Item.HasShoulder ? Pair(m.ShoulderDeltaU, m.ShoulderDeltaV) : "no bend",
                    EndLimited = m.EndLimited,
                    Stays = !m.Moves,
                    Overlaps = m.Overlaps
                });
            }

            int n = Plan.MoveCount;
            var parts = new List<string>();
            if (_o.MoveText) parts.Add("text");
            if (_o.MoveLeaderEnd) parts.Add("leader ends");
            if (_o.MoveLeaderShoulder) parts.Add("bend points");
            string partsText = string.Join(", ", parts);
            string what = _o.Mode switch
            {
                ArrangeMode.OffsetFromPoint => "placed at their offsets from the measured points",
                ArrangeMode.DistributeOnly => _o.Direction == AlignDirection.Vertical ? "spaced down their column" : "spaced along their row",
                _ => _o.Direction == AlignDirection.Vertical ? "moved into one column" : "moved into one row"
            };
            string extra = string.Empty;
            if (Plan.Target != null && Plan.Target.FellBackToAverage) extra = " No tags share a position, so the average is used.";
            else if (Plan.Target != null && _o.Target == AlignTarget.MostCommon) extra = $" {Plan.Target.SharedCount} tags already share the line.";
            if (_o.Target == AlignTarget.Reference && Plan.Targets.Values.Any(t => t.ReferenceMissing))
                extra += " The reference spot lacks a chosen leader point, so the first spot that has it sets that line.";
            Summary = $"{n} of {_items.Count} spots change: {partsText} {what}.{extra}";

            var warn = new List<string>();
            if (Plan.MissingEndCount > 0) warn.Add($"{Plan.MissingEndCount} spot(s) have no leader; their end point stays.");
            if (Plan.MissingShoulderCount > 0) warn.Add($"{Plan.MissingShoulderCount} spot(s) have no bend point; theirs stays.");
            if (Plan.EndLimitedCount > 0)
                warn.Add($"{Plan.EndLimitedCount} leader end(s) (*) cannot reach the line: with a bend, Revit only lets the end " +
                         "slide along the bend-to-end segment. Also tick the bend point to move them further.");
            if (Plan.OverlapCount > 0)
                warn.Add($"{Plan.OverlapCount} tags would overlap (red rows, estimated from the text size). " +
                         "Choose even spacing or a fixed gap; Fit picks a gap that clears the tags.");
            Warning = string.Join(" ", warn);
            CanApply = n > 0;
        }

        /// <summary>Preview, summary and checks for Align to detail lines.</summary>
        private void RefreshDetailLines()
        {
            AlignOptions o = Options;
            foreach (LinePickViewModel box in new[] { BendLine, EndLine, TextLine })
            {
                int have = _items.Count(i => AlignEngine.Has(i, box.Part));
                box.SetCounts(have,
                    AlignEngine.CountReachable(_items, box.Part, box.Line, AlignDirection.Vertical, o.ExtendLines),
                    AlignEngine.CountReachable(_items, box.Part, box.Line, AlignDirection.Horizontal, o.ExtendLines));
            }
            if (!o.HasAnyDetailLine)
            {
                Summary = "Pick at least one detail line: for the bend points, the end points or the text.";
                CanApply = false;
                return;
            }

            Plan = AlignEngine.Plan(_items, o);
            foreach (PlannedMove m in Plan.Entries)
            {
                Rows.Add(new PreviewRow
                {
                    Label = m.Item.Label,
                    MoveX = Fmt(m.DeltaU),
                    MoveY = Fmt(m.DeltaV),
                    EndMove = m.Item.HasEnd ? Pair(m.EndDeltaU, m.EndDeltaV) + (m.EndLimited ? " *" : "") : "no leader",
                    BendMove = m.Item.HasShoulder ? Pair(m.ShoulderDeltaU, m.ShoulderDeltaV) : "no bend",
                    EndLimited = m.EndLimited,
                    Stays = !m.Moves,
                    Overlaps = m.Overlaps,
                    Result = m.LineResult
                });
            }

            var parts = new List<string>();
            if (o.ShoulderLine != null) parts.Add("bend points");
            if (o.EndLine != null) parts.Add("end points");
            if (o.TextLine != null) parts.Add("text");
            int n = Plan.MoveCount;
            Summary = n > 0
                ? $"{n} of {_items.Count} spots change: {string.Join(", ", parts)} moved onto their detail line."
                : "Nothing can move with these settings.";

            var warn = new List<string>();
            if (Plan.LineSkippedCount > 0) warn.Add($"{Plan.LineSkippedCount} spot(s) have a part skipped (see Result).");
            if (Plan.MissingEndCount > 0) warn.Add($"{Plan.MissingEndCount} spot(s) have no leader; their end point stays.");
            if (Plan.MissingShoulderCount > 0) warn.Add($"{Plan.MissingShoulderCount} spot(s) have no bend point; theirs stays.");
            if (Plan.EndLimitedCount > 0)
                warn.Add($"{Plan.EndLimitedCount} leader end(s) (*) cannot reach the line: with a bend, Revit only lets the end " +
                         "slide along the bend-to-end segment. Pick a bend point line too to move them further.");
            if (Plan.OverlapCount > 0)
                warn.Add($"{Plan.OverlapCount} tags would overlap (red rows, estimated from the text size).");
            Warning = string.Join(" ", warn);
            CanApply = n > 0;
        }

        private static string Pair(double u, double v)
            => Math.Abs(u) < 1e-6 && Math.Abs(v) < 1e-6 ? "–" : $"{Fmt(u)}, {Fmt(v)}";

        private static string Fmt(double mm) => Math.Abs(mm) < 1e-6 ? "–" : $"{(mm > 0 ? "+" : "")}{mm:0.0}";
    }
}
