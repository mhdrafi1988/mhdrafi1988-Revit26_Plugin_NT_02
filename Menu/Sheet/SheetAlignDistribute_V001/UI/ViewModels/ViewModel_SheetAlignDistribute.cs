using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.UI.ViewModels
{
    /// <summary>One entry of a drop-down: the value and the words the user reads.</summary>
    /// <typeparam name="T">The value's type, normally one of the tool's enums.</typeparam>
    public sealed class AlignOption<T>
    {
        /// <summary>Creates an entry.</summary>
        public AlignOption(T value, string label)
        {
            Value = value;
            Label = label;
        }

        /// <summary>The value this entry stands for.</summary>
        public T Value { get; }

        /// <summary>What the drop-down shows.</summary>
        public string Label { get; }
    }

    /// <summary>
    /// Drives the Sheet Align &amp; Distribute window: holds the settings, re-plans whenever one of
    /// them changes, and hands the plan to the grid and the sheet preview.
    ///
    /// Every input below is mirrored from <see cref="AlignDistributeSettings"/> rather than bound to
    /// it directly, so that a change can trigger a re-plan and the saved file is only written on
    /// Apply and on close.
    /// </summary>
    public sealed partial class ViewModel_SheetAlignDistribute : ToolViewModelBase, IDisposable
    {
        /// <summary>Settings folder under %AppData%\Revit26_Plugin_Revised.</summary>
        private const string SettingsFolder = "SheetAlignDistribute";

        private readonly ISheetAlignSession _session;
        private readonly AlignDistributeSettings _settings;
        private readonly HashSet<long> _unticked = new();

        // Properties whose change asks for a fresh plan. Metrics and the plan itself are left out,
        // or writing them back would loop.
        private static readonly HashSet<string> ReplanTriggers = new()
        {
            nameof(MarginTopMm), nameof(MarginBottomMm), nameof(MarginLeftMm), nameof(MarginRightMm),
            nameof(MinGapHorizontalMm), nameof(MinGapVerticalMm),
            nameof(RowToleranceMm), nameof(ColumnToleranceMm), nameof(FootprintIncludesTitle),
            nameof(IncludeSchedules), nameof(AlignRows), nameof(AlignColumns),
            nameof(DistributeHorizontal), nameof(DistributeVertical),
            nameof(SnapToleranceMm), nameof(MaxMoveMm), nameof(MoveOverLimitAnyway),
            nameof(MovePinned), nameof(KeepInsideMargins), nameof(StopOnOverlap)
        };

        // Editing one of these means the user has taken the margins over, so Refresh and Apply
        // stop measuring them off the sheet and overwriting what was typed.
        private static readonly HashSet<string> CustomMarginTriggers = new()
        {
            nameof(MarginTopMm), nameof(MarginBottomMm), nameof(MarginLeftMm), nameof(MarginRightMm),
            nameof(MinGapHorizontalMm), nameof(MinGapVerticalMm)
        };

        private bool _loading;
        private SheetSnapshot _snapshot;

        /// <summary>Creates the view model for <paramref name="session"/>'s sheet.</summary>
        public ViewModel_SheetAlignDistribute(ISheetAlignSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _settings = SettingsService<AlignDistributeSettings>.Load(SettingsFolder) ?? new AlignDistributeSettings();
            _snapshot = session.Initial;

            _loading = true;
            LoadFromSettings();
            _loading = false;

            AddLog(LogLevel.Info, $"Sheet {_snapshot.SheetLabel}: {Describe(_snapshot)}.");
            if (_snapshot.FrameProblem != null)
                AddLog(LogLevel.Warning, _snapshot.FrameProblem);
            if (_snapshot.UnreadableCount > 0)
                AddLog(LogLevel.Warning, $"{_snapshot.UnreadableCount} element(s) had no readable outline and are left out.");

            AdoptDetected(announce: true);
            Replan();
        }

        // ── Sheet ──────────────────────────────────────────────────────────

        /// <summary>"A101 - Sheet Name" of the sheet the tool was opened on.</summary>
        public string SheetLabel => _snapshot?.SheetLabel ?? "";

        /// <summary>The current plan; the preview canvas is drawn from it.</summary>
        [ObservableProperty] private AlignPlan _plan;

        /// <summary>One line per element on the sheet.</summary>
        public ObservableCollection<AlignRowViewModel> Rows { get; } = new();

        /// <summary>The line the per-view clearance editor is pointed at.</summary>
        [ObservableProperty] private AlignRowViewModel _selectedRow;

        // ── Margins ────────────────────────────────────────────────────────

        /// <summary>Top margin inside the title block (mm).</summary>
        [ObservableProperty] private double _marginTopMm;

        /// <summary>Bottom margin inside the title block (mm).</summary>
        [ObservableProperty] private double _marginBottomMm;

        /// <summary>Left margin inside the title block (mm).</summary>
        [ObservableProperty] private double _marginLeftMm;

        /// <summary>Right margin inside the title block (mm).</summary>
        [ObservableProperty] private double _marginRightMm;

        /// <summary>What the sheet's own margins and gaps measure, as a label.</summary>
        [ObservableProperty] private string _detectedText = "";

        /// <summary>True once the user has typed a margin or gap of their own.</summary>
        public bool MarginsAreCustom => _settings.MarginSource == MarginSource.Custom;

        /// <summary>"Detected" or "Custom", for the badge next to the margin boxes.</summary>
        public string MarginSourceText => MarginsAreCustom ? "Custom" : "Detected";

        // ── Grid detection ─────────────────────────────────────────────────

        /// <summary>Row tolerance (mm).</summary>
        [ObservableProperty] private double _rowToleranceMm;

        /// <summary>Column tolerance (mm).</summary>
        [ObservableProperty] private double _columnToleranceMm;

        /// <summary>True to measure a viewport's footprint as its box plus its title.</summary>
        [ObservableProperty] private bool _footprintIncludesTitle;

        /// <summary>True to align schedules placed on the sheet as well as views.</summary>
        [ObservableProperty] private bool _includeSchedules;

        // ── Align and distribute ───────────────────────────────────────────

        /// <summary>The shared horizontal line each row snaps to.</summary>
        [ObservableProperty] private RowAlign _alignRows;

        /// <summary>The shared vertical line each column snaps to.</summary>
        [ObservableProperty] private ColumnAlign _alignColumns;

        /// <summary>What happens to the gaps across a row.</summary>
        [ObservableProperty] private DistributeMode _distributeHorizontal;

        /// <summary>What happens to the gaps between rows.</summary>
        [ObservableProperty] private DistributeMode _distributeVertical;

        /// <summary>Smallest gap between two views side by side (mm).</summary>
        [ObservableProperty] private double _minGapHorizontalMm;

        /// <summary>Smallest gap between two rows (mm).</summary>
        [ObservableProperty] private double _minGapVerticalMm;

        // ── Movement limits ────────────────────────────────────────────────

        /// <summary>A view already this close to its line is left untouched (mm).</summary>
        [ObservableProperty] private double _snapToleranceMm;

        /// <summary>A view is never moved further than this (mm). 0 turns the limit off.</summary>
        [ObservableProperty] private double _maxMoveMm;

        /// <summary>True to move a view past the limit anyway instead of reporting it.</summary>
        [ObservableProperty] private bool _moveOverLimitAnyway;

        /// <summary>Move pinned elements too; they are pinned again afterwards.</summary>
        [ObservableProperty] private bool _movePinned;

        /// <summary>Leave an element where it is when its aligned position would cross the margins.</summary>
        [ObservableProperty] private bool _keepInsideMargins;

        /// <summary>Apply nothing if any two elements would end up overlapping.</summary>
        [ObservableProperty] private bool _stopOnOverlap;

        // ── Per-view clearance ─────────────────────────────────────────────

        /// <summary>Clearance above the selected view (mm), or null for the sheet gap.</summary>
        [ObservableProperty] private double? _padTopMm;

        /// <summary>Clearance below the selected view (mm), or null for the sheet gap.</summary>
        [ObservableProperty] private double? _padBottomMm;

        /// <summary>Clearance left of the selected view (mm), or null for the sheet gap.</summary>
        [ObservableProperty] private double? _padLeftMm;

        /// <summary>Clearance right of the selected view (mm), or null for the sheet gap.</summary>
        [ObservableProperty] private double? _padRightMm;

        /// <summary>What the selected view currently has clear on each side, as a label.</summary>
        [ObservableProperty] private string _padDetectedText = "";

        /// <summary>Name of the view the clearance editor is pointed at, or a prompt when none is.</summary>
        public string PadTargetName => SelectedRow?.Name ?? "Pick a view in the grid below";

        /// <summary>True while a view is selected, so the clearance boxes are enabled.</summary>
        public bool HasPadTarget => SelectedRow != null;

        // ── Metrics ────────────────────────────────────────────────────────

        /// <summary>Elements found on the sheet.</summary>
        public int ElementCount => _snapshot?.Elements.Count ?? 0;

        /// <summary>"3 × 3" — the rows and columns read off the sheet.</summary>
        public string GridText => Plan == null || Plan.RowCount == 0 ? "–" : $"{Plan.RowCount} × {Plan.ColumnCount}";

        /// <summary>Elements that will be moved.</summary>
        public int MoveCount => Plan?.MoveCount ?? 0;

        /// <summary>Elements already within the snap tolerance of their line.</summary>
        public int InPlaceCount => Plan?.InPlaceCount ?? 0;

        /// <summary>Elements whose move is longer than the limit.</summary>
        public int OverLimitCount => Plan?.OverLimitCount ?? 0;

        /// <summary>The longest move the plan makes, as "5.7 mm".</summary>
        public string LargestMoveText => Plan == null || Plan.MoveCount == 0
            ? "–" : Plan.LargestMoveMm.ToString("0.0") + " mm";

        /// <summary>The line under the window: what Apply will do, or why it cannot.</summary>
        [ObservableProperty] private string _statusText = "";

        /// <summary>Caption of the Apply button, so it carries the count.</summary>
        public string ApplyCaption => MoveCount > 0 ? $"Apply ({MoveCount})" : "Apply";

        /// <summary>True while a Revit request is in flight.</summary>
        [ObservableProperty] private bool _isBusy;

        // ── Drop-down contents ─────────────────────────────────────────────

        /// <summary>Choices for the row alignment.</summary>
        public IReadOnlyList<AlignOption<RowAlign>> RowAlignOptions { get; } = new[]
        {
            new AlignOption<RowAlign>(RowAlign.Off, "Off — keep every Y"),
            new AlignOption<RowAlign>(RowAlign.LeastMovement, "Least movement"),
            new AlignOption<RowAlign>(RowAlign.Top, "Top edge"),
            new AlignOption<RowAlign>(RowAlign.Center, "Centre line"),
            new AlignOption<RowAlign>(RowAlign.Bottom, "Bottom edge — titles line up")
        };

        /// <summary>Choices for the column alignment.</summary>
        public IReadOnlyList<AlignOption<ColumnAlign>> ColumnAlignOptions { get; } = new[]
        {
            new AlignOption<ColumnAlign>(ColumnAlign.Off, "Off — keep every X"),
            new AlignOption<ColumnAlign>(ColumnAlign.LeastMovement, "Least movement"),
            new AlignOption<ColumnAlign>(ColumnAlign.Left, "Left edge"),
            new AlignOption<ColumnAlign>(ColumnAlign.Center, "Centre line"),
            new AlignOption<ColumnAlign>(ColumnAlign.Right, "Right edge")
        };

        /// <summary>Choices for either distribute axis.</summary>
        public IReadOnlyList<AlignOption<DistributeMode>> DistributeOptions { get; } = new[]
        {
            new AlignOption<DistributeMode>(DistributeMode.Off, "Off — keep the current gaps"),
            new AlignOption<DistributeMode>(DistributeMode.EvenGaps, "Even gaps"),
            new AlignOption<DistributeMode>(DistributeMode.FitMargins, "Fit the margins")
        };

        // ── Commands ───────────────────────────────────────────────────────

        /// <summary>Re-reads the sheet from Revit and re-plans.</summary>
        [RelayCommand]
        private void Refresh()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusText = "Reading the sheet…";

            if (!_session.Refresh(FootprintMode(), result => OnSessionDone(result, applied: false)))
            {
                IsBusy = false;
                AddLog(LogLevel.Error, "Revit would not take the request — finish what you are doing in Revit and press Refresh again.");
                Replan();
            }
        }

        /// <summary>Moves the elements the plan says to move, in one Undo step.</summary>
        [RelayCommand]
        private void Apply()
        {
            if (IsBusy) return;
            if (Plan == null || !Plan.CanApply)
            {
                AddLog(LogLevel.Warning, "Nothing to apply: " + (Plan?.Blocker ?? "every element is already in place."));
                return;
            }

            WriteToSettings();
            Save();

            IsBusy = true;
            StatusText = $"Moving {Plan.MoveCount} element(s)…";

            if (!_session.Apply(_settings, _unticked, Plan.Signature, result => OnSessionDone(result, applied: true)))
            {
                IsBusy = false;
                AddLog(LogLevel.Error, "Revit would not take the request — finish what you are doing in Revit and press Apply again.");
                Replan();
            }
        }

        /// <summary>Puts the margins and gaps back to what the sheet measures.</summary>
        [RelayCommand]
        private void RedetectMargins()
        {
            _settings.MarginSource = MarginSource.Detected;
            OnPropertyChanged(nameof(MarginsAreCustom));
            OnPropertyChanged(nameof(MarginSourceText));
            AdoptDetected(announce: true);
            Replan();
        }

        /// <summary>Fills the clearance boxes with what the selected view currently has clear.</summary>
        [RelayCommand]
        private void RedetectPad()
        {
            var clear = ClearSpaceFor(SelectedRow);
            if (clear == null)
            {
                AddLog(LogLevel.Warning, "Pick a view in the grid first.");
                return;
            }

            _loading = true;
            PadTopMm = clear.TopMm;
            PadBottomMm = clear.BottomMm;
            PadLeftMm = clear.LeftMm;
            PadRightMm = clear.RightMm;
            _loading = false;
            CommitPad();
        }

        /// <summary>Clears the selected view's own clearance, so it uses the sheet gap again.</summary>
        [RelayCommand]
        private void ClearPad()
        {
            _loading = true;
            PadTopMm = PadBottomMm = PadLeftMm = PadRightMm = null;
            _loading = false;
            CommitPad();
        }

        /// <summary>Gives every ticked view the clearance now in the boxes.</summary>
        [RelayCommand]
        private void ApplyPadToTicked()
        {
            var pad = PadFromBoxes();
            foreach (var row in Rows.Where(r => r.IsTicked && !string.IsNullOrEmpty(r.Name)))
                Store(row.Name, pad?.Clone());

            AddLog(LogLevel.Info, pad == null
                ? "Cleared the per-view clearance on every ticked view."
                : "Gave every ticked view the same clearance.");
            Replan();
        }

        /// <summary>Ticks every element that may be ticked.</summary>
        [RelayCommand]
        private void TickAll() => SetAllTicks(true);

        /// <summary>Unticks every element.</summary>
        [RelayCommand]
        private void UntickAll() => SetAllTicks(false);

        /// <summary>Puts every setting back to the tool's defaults, then re-detects the sheet's margins.</summary>
        [RelayCommand]
        private void ResetToDefaults()
        {
            var defaults = new AlignDistributeSettings();
            _loading = true;
            RowToleranceMm = defaults.RowToleranceMm;
            ColumnToleranceMm = defaults.ColumnToleranceMm;
            FootprintIncludesTitle = defaults.Footprint == Core.Models.FootprintMode.BoxAndTitle;
            IncludeSchedules = defaults.IncludeSchedules;
            AlignRows = defaults.AlignRows;
            AlignColumns = defaults.AlignColumns;
            DistributeHorizontal = defaults.DistributeHorizontal;
            DistributeVertical = defaults.DistributeVertical;
            MinGapHorizontalMm = defaults.MinGapHorizontalMm;
            MinGapVerticalMm = defaults.MinGapVerticalMm;
            SnapToleranceMm = defaults.SnapToleranceMm;
            MaxMoveMm = defaults.MaxMoveMm;
            MoveOverLimitAnyway = defaults.OverLimit == OverLimitMode.MoveAnyway;
            MovePinned = defaults.MovePinned;
            KeepInsideMargins = defaults.KeepInsideMargins;
            StopOnOverlap = defaults.StopOnOverlap;
            _loading = false;

            AddLog(LogLevel.Info, "Settings put back to the defaults.");
            _settings.MarginSource = MarginSource.Detected;
            OnPropertyChanged(nameof(MarginsAreCustom));
            OnPropertyChanged(nameof(MarginSourceText));
            AdoptDetected(announce: true);
            Replan();
        }

        /// <summary>Copies the plan to the clipboard as a plain-text table.</summary>
        [RelayCommand]
        private void CopyReport()
        {
            if (Plan == null) return;
            try
            {
                Clipboard.SetText(BuildReport());
                AddLog(LogLevel.Info, "Report copied to the clipboard.");
            }
            catch (Exception)
            {
                AddLog(LogLevel.Warning, "The clipboard was busy — try Copy report again.");
            }
        }

        // ── Planning ───────────────────────────────────────────────────────

        /// <summary>Re-plans from the current snapshot and settings, and refreshes the grid and metrics.</summary>
        private void Replan()
        {
            WriteToSettings();
            var plan = AlignPlanner.Build(_snapshot, _settings, _unticked);
            Plan = plan;
            RebuildRows(plan);
            UpdateDetectedText(plan);

            foreach (string note in plan.Notes)
                AddLog(LogLevel.Warning, note);

            StatusText = plan.Blocker != null
                ? plan.Blocker
                : plan.MoveCount > 0
                    ? $"Plan ready — {plan.MoveCount} element(s) to move, largest {plan.LargestMoveMm:0.0} mm."
                      + (plan.OverLimitCount > 0 ? $" {plan.OverLimitCount} over the limit, left alone." : "")
                    : "Nothing to move — every ticked element is already on its line.";

            OnPropertyChanged(nameof(ElementCount));
            OnPropertyChanged(nameof(GridText));
            OnPropertyChanged(nameof(MoveCount));
            OnPropertyChanged(nameof(InPlaceCount));
            OnPropertyChanged(nameof(OverLimitCount));
            OnPropertyChanged(nameof(LargestMoveText));
            OnPropertyChanged(nameof(ApplyCaption));
            OnPropertyChanged(nameof(SheetLabel));
        }

        private void RebuildRows(AlignPlan plan)
        {
            long? keep = SelectedRow?.Key;
            Rows.Clear();
            foreach (var move in plan.Moves)
                Rows.Add(new AlignRowViewModel(move, OnTickChanged));

            // Keep the clearance editor pointed at the same view across a re-plan.
            SelectedRow = keep.HasValue ? Rows.FirstOrDefault(r => r.Key == keep.Value) : null;
        }

        private void OnTickChanged(AlignRowViewModel row)
        {
            if (row.IsTicked) _unticked.Remove(row.Key);
            else _unticked.Add(row.Key);
            Replan();
        }

        private void SetAllTicks(bool ticked)
        {
            foreach (var row in Rows.Where(r => r.CanTick))
            {
                if (ticked) _unticked.Remove(row.Key);
                else _unticked.Add(row.Key);
            }
            Replan();
        }

        private void OnSessionDone(SessionResult result, bool applied)
        {
            IsBusy = false;

            if (result?.Snapshot != null)
                _snapshot = result.Snapshot;

            if (!string.IsNullOrEmpty(result?.Message))
                AddLog(result.Level, result.Message);

            // The sheet may have gained or lost elements, so drop ticks that no longer match anything.
            var live = new HashSet<long>(_snapshot?.Elements.Select(e => e.Key) ?? Enumerable.Empty<long>());
            _unticked.RemoveWhere(key => !live.Contains(key));

            AdoptDetected(announce: false);
            Replan();

            if (applied && result?.Applied == true)
                ShowApplied(result.Moved);
        }

        private void ShowApplied(int moved)
        {
            var text = new StringBuilder();
            text.Append(moved).Append(moved == 1 ? " element aligned on " : " elements aligned on ")
                .Append(_snapshot?.SheetLabel).Append('.');

            if (Plan != null)
            {
                if (Plan.InPlaceCount > 0)
                    text.Append(Environment.NewLine).Append(Environment.NewLine)
                        .Append(Plan.InPlaceCount).Append(Plan.InPlaceCount == 1 ? " element was" : " elements were")
                        .Append(" already in place.");
                if (Plan.OverLimitCount > 0)
                    text.Append(Environment.NewLine)
                        .Append(Plan.OverLimitCount).Append(Plan.OverLimitCount == 1 ? " element needed" : " elements needed")
                        .Append(" more than the ").Append(MaxMoveMm.ToString("0.#"))
                        .Append(" mm move limit and ").Append(Plan.OverLimitCount == 1 ? "was" : "were")
                        .Append(" left where ").Append(Plan.OverLimitCount == 1 ? "it is." : "they are.");
            }

            text.Append(Environment.NewLine).Append(Environment.NewLine)
                .Append("All moves are one Undo step — \"").Append(AlignApplier.TransactionName).Append("\".");

            MessageBox.Show(text.ToString(), ToolCatalog.SheetAlignDistribute.Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Detected values ────────────────────────────────────────────────

        /// <summary>
        /// Takes the sheet's own margins and gaps as the settings, which is what makes a run keep the
        /// layout's existing look. Only done while the margin source is Detected.
        /// </summary>
        private void AdoptDetected(bool announce)
        {
            if (_settings.MarginSource != MarginSource.Detected) return;

            WriteToSettings();
            var detected = AlignPlanner.Detect(_snapshot, _settings);
            if (!detected.HasValues) return;

            _loading = true;
            MarginTopMm = detected.MarginTopMm;
            MarginBottomMm = detected.MarginBottomMm;
            MarginLeftMm = detected.MarginLeftMm;
            MarginRightMm = detected.MarginRightMm;
            if (detected.GapHorizontalMm is double h) MinGapHorizontalMm = h;
            if (detected.GapVerticalMm is double v) MinGapVerticalMm = v;
            _loading = false;

            if (announce)
                AddLog(LogLevel.Info,
                    $"Margins read off the sheet: top {detected.MarginTopMm:0.#}, bottom {detected.MarginBottomMm:0.#}, " +
                    $"left {detected.MarginLeftMm:0.#}, right {detected.MarginRightMm:0.#} mm. " +
                    $"Gaps: horizontal {detected.GapHorizontalMm?.ToString("0.#") ?? "–"}, " +
                    $"vertical {detected.GapVerticalMm?.ToString("0.#") ?? "–"} mm.");
        }

        private void UpdateDetectedText(AlignPlan plan)
        {
            var detected = plan?.Detected;
            DetectedText = detected == null || !detected.HasValues
                ? "Nothing to measure on this sheet yet."
                : $"This sheet measures: top {detected.MarginTopMm:0.#} · bottom {detected.MarginBottomMm:0.#} · " +
                  $"left {detected.MarginLeftMm:0.#} · right {detected.MarginRightMm:0.#} mm, " +
                  $"gaps {detected.GapHorizontalMm?.ToString("0.#") ?? "–"} / {detected.GapVerticalMm?.ToString("0.#") ?? "–"} mm.";

            var clear = ClearSpaceFor(SelectedRow);
            PadDetectedText = clear == null
                ? ""
                : $"Clear now: top {clear.TopMm:0.#} · bottom {clear.BottomMm:0.#} · " +
                  $"left {clear.LeftMm:0.#} · right {clear.RightMm:0.#} mm.";
        }

        /// <summary>
        /// How much clear space the selected element currently has on each side: to its nearest
        /// neighbour on that side, or to the margin when nothing is in the way. This is what the
        /// clearance boxes are pre-filled with.
        /// </summary>
        private ViewPadding ClearSpaceFor(AlignRowViewModel row)
        {
            if (row?.Move?.Element == null || Plan?.Usable is not LayoutRect usable) return null;

            LayoutRect me = row.Move.Element.Footprint;
            double left = me.MinX - usable.MinX;
            double right = usable.MaxX - me.MaxX;
            double below = me.MinY - usable.MinY;
            double above = usable.MaxY - me.MaxY;

            foreach (var other in _snapshot.Elements)
            {
                if (other.Key == row.Key) continue;
                LayoutRect rect = other.Footprint;

                if (rect.VerticalOverlap(me) > 0)
                {
                    if (rect.MaxX <= me.MinX) left = Math.Min(left, me.MinX - rect.MaxX);
                    if (rect.MinX >= me.MaxX) right = Math.Min(right, rect.MinX - me.MaxX);
                }
                if (rect.HorizontalOverlap(me) > 0)
                {
                    if (rect.MaxY <= me.MinY) below = Math.Min(below, me.MinY - rect.MaxY);
                    if (rect.MinY >= me.MaxY) above = Math.Min(above, rect.MinY - me.MaxY);
                }
            }

            double Mm(double feet) => Units.ForDisplay(Units.FeetToMm(Math.Max(0, feet)));
            return new ViewPadding
            {
                TopMm = Mm(above), BottomMm = Mm(below), LeftMm = Mm(left), RightMm = Mm(right)
            };
        }

        // ── Per-view clearance plumbing ────────────────────────────────────

        partial void OnSelectedRowChanged(AlignRowViewModel value)
        {
            _loading = true;
            var pad = value?.Move?.Pad;
            PadTopMm = pad?.TopMm;
            PadBottomMm = pad?.BottomMm;
            PadLeftMm = pad?.LeftMm;
            PadRightMm = pad?.RightMm;
            _loading = false;

            OnPropertyChanged(nameof(PadTargetName));
            OnPropertyChanged(nameof(HasPadTarget));
            UpdateDetectedText(Plan);
        }

        private ViewPadding PadFromBoxes()
        {
            var pad = new ViewPadding
            {
                TopMm = PadTopMm, BottomMm = PadBottomMm, LeftMm = PadLeftMm, RightMm = PadRightMm
            };
            return pad.IsEmpty ? null : pad;
        }

        /// <summary>Saves what is in the clearance boxes against the selected view and re-plans.</summary>
        private void CommitPad()
        {
            if (SelectedRow == null || string.IsNullOrEmpty(SelectedRow.Name)) return;
            Store(SelectedRow.Name, PadFromBoxes());
            Replan();
        }

        private void Store(string viewName, ViewPadding pad)
        {
            _settings.Padding ??= new Dictionary<string, ViewPadding>();
            if (pad == null) _settings.Padding.Remove(viewName);
            else _settings.Padding[viewName] = pad;
        }

        // ── Settings ───────────────────────────────────────────────────────

        private Core.Models.FootprintMode FootprintMode() => FootprintIncludesTitle
            ? Core.Models.FootprintMode.BoxAndTitle
            : Core.Models.FootprintMode.BoxOnly;

        private void LoadFromSettings()
        {
            MarginTopMm = _settings.MarginTopMm;
            MarginBottomMm = _settings.MarginBottomMm;
            MarginLeftMm = _settings.MarginLeftMm;
            MarginRightMm = _settings.MarginRightMm;
            RowToleranceMm = _settings.RowToleranceMm;
            ColumnToleranceMm = _settings.ColumnToleranceMm;
            FootprintIncludesTitle = _settings.Footprint == Core.Models.FootprintMode.BoxAndTitle;
            IncludeSchedules = _settings.IncludeSchedules;
            AlignRows = _settings.AlignRows;
            AlignColumns = _settings.AlignColumns;
            DistributeHorizontal = _settings.DistributeHorizontal;
            DistributeVertical = _settings.DistributeVertical;
            MinGapHorizontalMm = _settings.MinGapHorizontalMm;
            MinGapVerticalMm = _settings.MinGapVerticalMm;
            SnapToleranceMm = _settings.SnapToleranceMm;
            MaxMoveMm = _settings.MaxMoveMm;
            MoveOverLimitAnyway = _settings.OverLimit == OverLimitMode.MoveAnyway;
            MovePinned = _settings.MovePinned;
            KeepInsideMargins = _settings.KeepInsideMargins;
            StopOnOverlap = _settings.StopOnOverlap;
        }

        private void WriteToSettings()
        {
            _settings.MarginTopMm = Math.Max(0, MarginTopMm);
            _settings.MarginBottomMm = Math.Max(0, MarginBottomMm);
            _settings.MarginLeftMm = Math.Max(0, MarginLeftMm);
            _settings.MarginRightMm = Math.Max(0, MarginRightMm);
            _settings.RowToleranceMm = Math.Max(0, RowToleranceMm);
            _settings.ColumnToleranceMm = Math.Max(0, ColumnToleranceMm);
            _settings.Footprint = FootprintMode();
            _settings.IncludeSchedules = IncludeSchedules;
            _settings.AlignRows = AlignRows;
            _settings.AlignColumns = AlignColumns;
            _settings.DistributeHorizontal = DistributeHorizontal;
            _settings.DistributeVertical = DistributeVertical;
            _settings.MinGapHorizontalMm = Math.Max(0, MinGapHorizontalMm);
            _settings.MinGapVerticalMm = Math.Max(0, MinGapVerticalMm);
            _settings.SnapToleranceMm = Math.Max(0, SnapToleranceMm);
            _settings.MaxMoveMm = Math.Max(0, MaxMoveMm);
            _settings.OverLimit = MoveOverLimitAnyway ? OverLimitMode.MoveAnyway : OverLimitMode.Report;
            _settings.MovePinned = MovePinned;
            _settings.KeepInsideMargins = KeepInsideMargins;
            _settings.StopOnOverlap = StopOnOverlap;
            _settings.Version = AlignDistributeSettings.CurrentVersion;
        }

        /// <summary>Writes the settings to disk. Called on Apply and when the window closes.</summary>
        public void Save()
        {
            WriteToSettings();
            SettingsService<AlignDistributeSettings>.Save(SettingsFolder, _settings);
        }

        /// <summary>Releases the Revit ExternalEvent behind the session. Called when the window closes.</summary>
        public void Dispose() => _session?.Dispose();

        /// <summary>Re-plans whenever one of the inputs changes, so the preview always matches the boxes.</summary>
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (_loading || e?.PropertyName == null) return;

            if (CustomMarginTriggers.Contains(e.PropertyName) && _settings.MarginSource != MarginSource.Custom)
            {
                _settings.MarginSource = MarginSource.Custom;
                OnPropertyChanged(nameof(MarginsAreCustom));
                OnPropertyChanged(nameof(MarginSourceText));
            }

            if (ReplanTriggers.Contains(e.PropertyName)) Replan();
        }

        // ── Report ─────────────────────────────────────────────────────────

        private string BuildReport()
        {
            var text = new StringBuilder();
            text.AppendLine($"Sheet Align & Distribute v{ToolCatalog.SheetAlignDistribute.Version}");
            text.AppendLine($"Sheet: {_snapshot?.SheetLabel}");
            text.AppendLine($"Grid: {GridText}   Margins (mm): top {MarginTopMm:0.#}, bottom {MarginBottomMm:0.#}, " +
                            $"left {MarginLeftMm:0.#}, right {MarginRightMm:0.#}");
            text.AppendLine($"Align: rows {AlignRows}, columns {AlignColumns}   " +
                            $"Distribute: H {DistributeHorizontal}, V {DistributeVertical}");
            text.AppendLine($"Snap {SnapToleranceMm:0.#} mm, max move {MaxMoveMm:0.#} mm");
            text.AppendLine();
            text.AppendLine("Detail\tView\tType\tW x H\tRow\tCol\tPad\tdX\tdY\tMove\tStatus\tNote");

            foreach (var row in Rows)
                text.AppendLine(string.Join("\t",
                    row.DetailNumber, row.Name, row.TypeName, row.SizeText, row.RowLabel, row.ColumnLabel,
                    row.PadLabel, row.DeltaXLabel, row.DeltaYLabel, row.DistanceLabel, row.StatusLabel,
                    row.Note ?? ""));

            text.AppendLine();
            text.AppendLine($"To move: {MoveCount}   In place: {InPlaceCount}   Over the limit: {OverLimitCount}   " +
                            $"Largest move: {LargestMoveText}");
            if (Plan?.Blocker != null) text.AppendLine("Blocked: " + Plan.Blocker);
            return text.ToString();
        }

        private static string Describe(SheetSnapshot snapshot)
        {
            int viewports = snapshot.Elements.Count(e => e.Kind == SheetElementKind.Viewport);
            int schedules = snapshot.Elements.Count(e => e.Kind == SheetElementKind.Schedule);
            var parts = new List<string>();
            if (viewports > 0) parts.Add($"{viewports} viewport(s)");
            if (schedules > 0) parts.Add($"{schedules} schedule(s)");
            return parts.Count == 0 ? "nothing placed on it" : string.Join(", ", parts);
        }
    }
}
