using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V007.Helpers;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V007.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V007.ViewModels
{
    /// <summary>A choice in a combo box: value plus label.</summary>
    public sealed class Choice<T>
    {
        /// <summary>The value.</summary>
        public T Value { get; init; }

        /// <summary>Text shown.</summary>
        public string Label { get; init; }

        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>
    /// View model of the V007 window: V006 (scan, grid, Move Up / Move Down / Distribute, By priority
    /// with a user-editable priority order, presets, "Never move", a same-priority rule, optional
    /// cross-type checking) plus "Keep clear of other annotations" with a neighbour tolerance.
    /// </summary>
    public partial class AnnotationOverlapViewModel : ObservableObject
    {
        // V007 keeps its own file (AnnotationOverlapDetection\V007\settings.json) so that saving in V006
        // or V005 cannot drop the newer fields. On first run it starts from the V006 file, else the V005 one.
        private const string SettingsFolderV005 = "AnnotationOverlapDetection";
        private static readonly string SettingsFolderV006 = System.IO.Path.Combine("AnnotationOverlapDetection", "V006");
        private static readonly string SettingsFolder = System.IO.Path.Combine("AnnotationOverlapDetection", "V007");

        private readonly Document _doc;
        private readonly View _activeView;
        private readonly ElementId _viewId;
        private readonly ExternalEvent _zoomEvent;
        private readonly ZoomToElementEventHandler _zoomHandler;
        private readonly ExternalEvent _moveEvent;
        private readonly MoveAnnotationsEventHandler _moveHandler;

        /// <summary>Annotation types found in the view, with a checkbox each.</summary>
        public ObservableCollection<AnnotationFamily> AnnotationFamilies { get; } = new();

        /// <summary>Overlapping pairs shown in the results grid.</summary>
        public ObservableCollection<OverlapResult> OverlapResults { get; } = new();

        [ObservableProperty]
        private int totalScanned;

        [ObservableProperty]
        private int totalOverlaps;

        [ObservableProperty]
        private string breakdownByTypeText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsToleranceEnabled))]
        private bool isLoading;

        [ObservableProperty]
        private string errorMessage = string.Empty;

        [ObservableProperty]
        private bool hasError;

        /// <summary>Green result banner after a resolve run.</summary>
        [ObservableProperty]
        private string infoMessage = string.Empty;

        [ObservableProperty]
        private bool hasInfo;

        // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty]
        private double progress;

        [ObservableProperty]
        private string summaryText = string.Empty;

        /// <summary>Selected resolve mode.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsMoveUp), nameof(IsMoveDown), nameof(IsDistribute), nameof(IsByPriority))]
        private ResolveMode mode = ResolveMode.Distribute;

        /// <summary>V006: rows of the priority order, highest priority first.</summary>
        public ObservableCollection<PriorityRow> PriorityRows { get; } = new();

        /// <summary>V006: preset choices.</summary>
        public IReadOnlyList<Choice<PriorityPreset>> Presets { get; } = new[]
        {
            PriorityPreset.Architectural, PriorityPreset.NotesStay, PriorityPreset.TagsStay, PriorityPreset.Custom
        }.Select(p => new Choice<PriorityPreset> { Value = p, Label = AnnotationPriority.PresetLabel(p) }).ToList();

        /// <summary>V006: same-priority rule choices.</summary>
        public IReadOnlyList<Choice<TieRule>> TieRules { get; } = new[]
        {
            new Choice<TieRule> { Value = TieRule.Distribute, Label = "Distribute (both move, half each)" },
            new Choice<TieRule> { Value = TieRule.MoveUp, Label = "Move the upper one up" },
            new Choice<TieRule> { Value = TieRule.MoveDown, Label = "Move the lower one down" },
            new Choice<TieRule> { Value = TieRule.Skip, Label = "Skip the pair" }
        };

        /// <summary>V006: selected preset.</summary>
        [ObservableProperty]
        private Choice<PriorityPreset> selectedPreset;

        /// <summary>V006: selected same-priority rule.</summary>
        [ObservableProperty]
        private Choice<TieRule> selectedTie;

        /// <summary>V006: also compare annotations of different types (off = V005 behaviour).</summary>
        [ObservableProperty]
        private bool acrossTypes;

        private bool _applyingPreset;
        private bool _ready;

        /// <summary>Clear gap text box (mm).</summary>
        [ObservableProperty]
        private string gapText = "1.00";

        /// <summary>
        /// V007: keep moved annotations clear of every other annotation in the view, and undo a move
        /// that still lands on one. Off = V006 behaviour.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsToleranceEnabled))]
        private bool avoidOthers;

        /// <summary>V007: neighbour tolerance text box (mm).</summary>
        [ObservableProperty]
        private string toleranceText = "1.00";

        /// <summary>V007: maximum move text box (mm, 0 = no limit).</summary>
        [ObservableProperty]
        private string maxMoveText = "1000";

        /// <summary>True when the neighbour tolerance box can be edited.</summary>
        public bool IsToleranceEnabled => AvoidOthers && IsIdle;

        /// <summary>True when no scan or resolve is running.</summary>
        public bool IsIdle => !IsLoading;

        /// <summary>Radio button: Move Up.</summary>
        public bool IsMoveUp { get => Mode == ResolveMode.MoveUp; set { if (value) Mode = ResolveMode.MoveUp; } }

        /// <summary>Radio button: Move Down.</summary>
        public bool IsMoveDown { get => Mode == ResolveMode.MoveDown; set { if (value) Mode = ResolveMode.MoveDown; } }

        /// <summary>Radio button: Distribute.</summary>
        public bool IsDistribute { get => Mode == ResolveMode.Distribute; set { if (value) Mode = ResolveMode.Distribute; } }

        /// <summary>Radio button: By priority (V006).</summary>
        public bool IsByPriority { get => Mode == ResolveMode.ByPriority; set { if (value) Mode = ResolveMode.ByPriority; } }

        // Raw collected data, kept around so "Check Overlaps" doesn't re-scan the document
        private List<AnnotationData> _allAnnotations = new();

        // Status of each pair from the last resolve run, keyed by ResolveOutcome.PairKey.
        private Dictionary<(long, long), string> _lastStatus = new();

        /// <summary>Creates the view model and scans <paramref name="activeView"/> once.</summary>
        public AnnotationOverlapViewModel(Document doc, View activeView,
            ExternalEvent zoomEvent, ZoomToElementEventHandler zoomHandler,
            ExternalEvent moveEvent, MoveAnnotationsEventHandler moveHandler)
        {
            _doc = doc;
            _activeView = activeView;
            _viewId = activeView.Id;
            _zoomEvent = zoomEvent;
            _zoomHandler = zoomHandler;
            _moveEvent = moveEvent;
            _moveHandler = moveHandler;
            _moveHandler.Completed = OnResolveCompleted;

            string from = SettingsFileExists(SettingsFolder) ? SettingsFolder
                : SettingsFileExists(SettingsFolderV006) ? SettingsFolderV006
                : SettingsFolderV005;
            var settings = SettingsService<AnnotationOverlapSettings>.Load(from);
            Mode = Enum.IsDefined(typeof(ResolveMode), settings.Mode) ? (ResolveMode)settings.Mode : ResolveMode.Distribute;
            double gap = settings.GapMm >= 0 && settings.GapMm <= OverlapResolver.MaxGapMm ? settings.GapMm : 1.0;
            GapText = gap.ToString("F2", CultureInfo.CurrentCulture);

            // V007 neighbour clearance (older files: off, 1 mm).
            AvoidOthers = settings.AvoidOthers;
            double tol = settings.NeighbourToleranceMm >= 0 && settings.NeighbourToleranceMm <= OverlapResolver.MaxGapMm
                ? settings.NeighbourToleranceMm : 1.0;
            ToleranceText = tol.ToString("F2", CultureInfo.CurrentCulture);
            double maxMove = settings.MaxMoveMm >= 0 && settings.MaxMoveMm <= OverlapResolver.MaxMoveLimitMm
                ? settings.MaxMoveMm : AnnotationOverlapSettings.DefaultMaxMoveMm;
            MaxMoveText = maxMove.ToString("0.##", CultureInfo.CurrentCulture);

            // V006 priority settings (a V005 settings file has none: defaults are the recommended order).
            AcrossTypes = settings.AcrossTypes;
            SelectedTie = TieRules.FirstOrDefault(t => (int)t.Value == settings.Tie) ?? TieRules[0];
            var neverMove = new HashSet<AnnotationCategory>();
            foreach (string name in settings.NeverMove ?? new List<string>())
                if (Enum.TryParse(name, out AnnotationCategory c) && Enum.IsDefined(typeof(AnnotationCategory), c))
                    neverMove.Add(c);
            var preset = Enum.IsDefined(typeof(PriorityPreset), settings.Preset) ? (PriorityPreset)settings.Preset : PriorityPreset.Architectural;
            var order = preset == PriorityPreset.Custom
                ? AnnotationPriority.Normalize(settings.PriorityOrder)
                : AnnotationPriority.OrderFor(preset).ToList();
            SetOrder(order, neverMove);
            _applyingPreset = true;
            PriorityPreset shown = AnnotationPriority.MatchPreset(order);
            SelectedPreset = Presets.First(p => p.Value == shown);
            _applyingPreset = false;

            LoadAnnotationFamilies(doc);
            _ready = true;
        }

        private static bool SettingsFileExists(string folder)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return System.IO.File.Exists(System.IO.Path.Combine(appData, "Revit26_Plugin_Revised", folder, "settings.json"));
            }
            catch
            {
                return false;
            }
        }

        private void SetOrder(IEnumerable<AnnotationCategory> order, ISet<AnnotationCategory> neverMove)
        {
            PriorityRows.Clear();
            int rank = 1;
            foreach (var c in order)
                PriorityRows.Add(new PriorityRow { Category = c, Rank = rank++, NeverMove = neverMove.Contains(c) });
        }

        partial void OnSelectedPresetChanged(Choice<PriorityPreset> value)
        {
            if (_applyingPreset || value == null || value.Value == PriorityPreset.Custom)
                return;
            var keepFixed = new HashSet<AnnotationCategory>(PriorityRows.Where(r => r.NeverMove).Select(r => r.Category));
            SetOrder(AnnotationPriority.OrderFor(value.Value), keepFixed);
        }

        partial void OnAcrossTypesChanged(bool value)
        {
            if (!_ready || _allAnnotations.Count == 0)
                return;
            _lastStatus = new();
            HasInfo = false;
            RunCheck();
        }

        /// <summary>Moves a priority row one place up (higher priority).</summary>
        [RelayCommand]
        private void PriorityUp(PriorityRow row) => MoveRow(row, -1);

        /// <summary>Moves a priority row one place down (lower priority, moves sooner).</summary>
        [RelayCommand]
        private void PriorityDown(PriorityRow row) => MoveRow(row, +1);

        /// <summary>Restores the recommended architectural order (keeps "Never move" ticks).</summary>
        [RelayCommand]
        private void ResetPriority() =>
            SelectedPreset = Presets.First(p => p.Value == PriorityPreset.Architectural);

        private void MoveRow(PriorityRow row, int step)
        {
            if (row == null) return;
            int i = PriorityRows.IndexOf(row);
            int j = i + step;
            if (i < 0 || j < 0 || j >= PriorityRows.Count) return;
            PriorityRows.Move(i, j);
            for (int k = 0; k < PriorityRows.Count; k++)
                PriorityRows[k].Rank = k + 1;

            var match = AnnotationPriority.MatchPreset(PriorityRows.Select(r => r.Category));
            _applyingPreset = true;
            SelectedPreset = Presets.First(p => p.Value == match);
            _applyingPreset = false;
        }

        /// <summary>
        /// Step 2 + 3: scan the view once and populate the type-selector list.
        /// </summary>
        public void LoadAnnotationFamilies(Document doc)
        {
            _allAnnotations = OverlapDetectionHelper.GetAnnotationsFromView(_activeView, doc);
            RebuildFamilies();
        }

        /// <summary>Saves the remembered options (mode, gap). Never throws.</summary>
        public void SaveSettings()
        {
            var settings = new AnnotationOverlapSettings
            {
                Mode = (int)Mode,
                Preset = (int)(SelectedPreset?.Value ?? PriorityPreset.Architectural),
                PriorityOrder = PriorityRows.Select(r => r.Category.ToString()).ToList(),
                NeverMove = PriorityRows.Where(r => r.NeverMove).Select(r => r.Category.ToString()).ToList(),
                Tie = (int)(SelectedTie?.Value ?? TieRule.Distribute),
                AcrossTypes = AcrossTypes,
                AvoidOthers = AvoidOthers
            };
            if (TryParseGap(out double gap))
                settings.GapMm = gap;
            if (TryParseMm(ToleranceText, out double tol))
                settings.NeighbourToleranceMm = tol;
            if (TryParseMm(MaxMoveText, out double maxMove, OverlapResolver.MaxMoveLimitMm))
                settings.MaxMoveMm = maxMove;
            SettingsService<AnnotationOverlapSettings>.Save(SettingsFolder, settings);
        }

        private void RebuildFamilies()
        {
            // Keep the user's ticks across a re-scan; new types start ticked.
            var previous = AnnotationFamilies.ToDictionary(f => f.TypeName, f => f.IsSelected);
            var grouped = OverlapDetectionHelper.GroupByType(_allAnnotations);

            AnnotationFamilies.Clear();
            foreach (var kvp in grouped.OrderBy(g => g.Key))
            {
                AnnotationFamilies.Add(new AnnotationFamily
                {
                    TypeName = kvp.Key,
                    Count = kvp.Value.Count,
                    IsSelected = !previous.TryGetValue(kvp.Key, out bool sel) || sel
                });
            }
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var family in AnnotationFamilies)
                family.IsSelected = true;
        }

        [RelayCommand]
        private void ClearAll()
        {
            foreach (var family in AnnotationFamilies)
                family.IsSelected = false;
        }

        [RelayCommand]
        private void CheckOverlaps()
        {
            HasInfo = false;
            _lastStatus = new();
            RunCheck();
        }

        private bool RunCheck()
        {
            HasError = false;
            ErrorMessage = string.Empty;

            var selectedTypes = AnnotationFamilies
                .Where(f => f.IsSelected)
                .Select(f => f.TypeName)
                .ToHashSet();

            if (selectedTypes.Count == 0)
            {
                ShowError("Please select at least one type.");
                return false;
            }

            IsLoading = true;
            // Esc / Cancel with a Yes-No confirm during the (read-only) overlap scan.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.AnnotationOverlapDetectionV007, null);
            try
            {
                RunOverlapDetection(selectedTypes);
                UpdateSummaryCard();
                return true;
            }
            catch (OperationCanceledException)
            {
                // The scan changes nothing in the model.
                ShowError("Cancelled by user.");
                return false;
            }
            catch (Exception ex)
            {
                ShowError($"Overlap detection failed: {ex.Message}");
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Step 5: run detection against only the selected type groups.
        /// </summary>
        private void RunOverlapDetection(HashSet<string> selectedTypes)
        {
            var filtered = _allAnnotations
                .Where(a => selectedTypes.Contains(a.TypeName))
                .ToList();

            var results = OverlapDetectionHelper.DetectOverlaps(filtered, AcrossTypes);

            OverlapResults.Clear();
            foreach (var r in results)
            {
                if (_lastStatus.Count > 0)
                {
                    r.Status = _lastStatus.TryGetValue(ResolveOutcome.PairKey(r.ElementId1, r.ElementId2), out var s)
                        ? s
                        : "Not resolved";
                }
                OverlapResults.Add(r);
            }

            TotalScanned = filtered.Count;
        }

        /// <summary>
        /// Step 7: summary card - total scanned, total overlaps, breakdown by type.
        /// </summary>
        private void UpdateSummaryCard()
        {
            TotalOverlaps = OverlapResults.Count;

            var breakdown = OverlapResults
                .GroupBy(r => r.AnnotationType)
                .Select(g => $"{g.Count()} {g.Key}");

            BreakdownByTypeText = TotalOverlaps == 0
                ? "No overlaps found"
                : string.Join(", ", breakdown);

            SummaryText = $"{TotalScanned} annotations scanned · {TotalOverlaps} overlaps";
        }

        [RelayCommand]
        private void ZoomToElement(long elementIdValue)
        {
            _zoomHandler.ElementIdToZoom = new ElementId(elementIdValue);
            _zoomEvent.Raise();
        }

        /// <summary>Resolve the rows selected in the grid (CommandParameter = DataGrid.SelectedItems).</summary>
        [RelayCommand]
        private void ResolveSelected(IList selectedItems)
        {
            var rows = selectedItems?.OfType<OverlapResult>().ToList() ?? new List<OverlapResult>();
            if (rows.Count == 0)
            {
                ShowError("Select one or more rows in the grid first, or use Resolve All.");
                return;
            }
            StartResolve(rows);
        }

        /// <summary>Resolve every row in the grid.</summary>
        [RelayCommand]
        private void ResolveAll()
        {
            if (OverlapResults.Count == 0)
            {
                ShowError("There are no overlaps to resolve. Run Check Overlaps first.");
                return;
            }
            StartResolve(OverlapResults.ToList());
        }

        private void StartResolve(List<OverlapResult> rows)
        {
            HasError = false;
            HasInfo = false;

            if (!TryParseGap(out double gap))
            {
                ShowError($"Clear gap must be a number from 0 to {OverlapResolver.MaxGapMm:F0} mm.");
                return;
            }

            double tolerance = 0;
            if (AvoidOthers && !TryParseMm(ToleranceText, out tolerance))
            {
                ShowError($"Neighbour tolerance must be a number from 0 to {OverlapResolver.MaxGapMm:F0} mm.");
                return;
            }

            double maxMoveMm = 0;
            if (AvoidOthers && !TryParseMm(MaxMoveText, out maxMoveMm, OverlapResolver.MaxMoveLimitMm))
            {
                ShowError($"Maximum move must be a number from 0 (no limit) to {OverlapResolver.MaxMoveLimitMm:F0} mm.");
                return;
            }

            if (IsLoading)
                return;

            _moveHandler.Request = new ResolveRequest
            {
                Document = _doc,
                ViewId = _viewId,
                Pairs = rows.Select(r => (r.ElementId1, r.ElementId2)).ToList(),
                Mode = Mode,
                GapMm = gap,
                PriorityOrder = PriorityRows.Select(r => r.Category).ToList(),
                NeverMove = new HashSet<AnnotationCategory>(PriorityRows.Where(r => r.NeverMove).Select(r => r.Category)),
                Tie = SelectedTie?.Value ?? TieRule.Distribute,
                AvoidOthers = AvoidOthers,
                NeighbourToleranceMm = tolerance,
                MaxMoveMm = maxMoveMm
            };

            SaveSettings();
            IsLoading = true;
            SummaryText = $"Resolving {rows.Count} pair(s)…";

            ExternalEventRequest accepted = _moveEvent.Raise();
            if (accepted != ExternalEventRequest.Accepted)
            {
                _moveHandler.Request = null;
                IsLoading = false;
                SummaryText = string.Empty;
                ShowError($"Revit did not accept the request ({accepted}). Finish any running command and try again.");
            }
        }

        /// <summary>Called by the move handler on the Revit (UI) thread when a run finishes.</summary>
        private void OnResolveCompleted(ResolveOutcome outcome)
        {
            IsLoading = false;

            if (outcome.Rescan != null)
            {
                _allAnnotations = outcome.Rescan;
                RebuildFamilies();
            }

            _lastStatus = outcome.PairStatus;
            RunCheck();

            if (!string.IsNullOrEmpty(outcome.Error))
            {
                ShowError(outcome.Error);
                return;
            }

            int total = outcome.PairsResolved + outcome.PairsSkipped;
            string text = $"Resolved {outcome.PairsResolved} of {total} pair(s): moved {outcome.ElementsMoved} element(s).";
            if (outcome.PairsSkipped > 0)
                text += $" {outcome.PairsSkipped} pair(s) skipped or failed; see the Status column.";
            if (outcome.KeptInPlace.Count > 0)
                text += $" {outcome.KeptInPlace.Count} move(s) undone because they would land on another annotation.";
            if (outcome.TakenBack > 0)
                text += $" {outcome.TakenBack} annotation(s) left in place because their pair could not be separated.";
            if (outcome.ElementsMoved > 0)
                text += " One Undo in Revit reverts this run.";
            if (TotalOverlaps > 0)
                text += $" {TotalOverlaps} overlap(s) remain.";

            InfoMessage = text;
            HasInfo = true;
        }

        private bool TryParseGap(out double gap) => TryParseMm(GapText, out gap);

        /// <summary>
        /// Parses a mm value from 0 to <paramref name="max"/> (default <see cref="OverlapResolver.MaxGapMm"/>),
        /// in the current or the invariant culture.
        /// </summary>
        private static bool TryParseMm(string text, out double value, double max = OverlapResolver.MaxGapMm)
        {
            string s = (text ?? string.Empty).Trim();
            bool ok = double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                   || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return ok && !double.IsNaN(value) && value >= 0 && value <= max;
        }

        private void ShowError(string message)
        {
            ErrorMessage = message;
            HasError = true;
        }
    }
}
