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
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.ViewModels
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
    /// View model of the V006 window: V005's scan, grid and Resolve Overlaps (Move Up / Move Down /
    /// Distribute), plus By priority with a user-editable priority order, presets, "Never move",
    /// a same-priority rule, and optional cross-type checking.
    /// </summary>
    public partial class AnnotationOverlapViewModel : ObservableObject
    {
        // V006 keeps its own file (AnnotationOverlapDetection\V006\settings.json) so that saving in V005
        // cannot drop the priority fields. On first run it starts from the V005 file (same folder root).
        private const string SettingsFolderV005 = "AnnotationOverlapDetection";
        private static readonly string SettingsFolder = System.IO.Path.Combine("AnnotationOverlapDetection", "V006");

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
        [NotifyPropertyChangedFor(nameof(IsIdle))]
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
            new Choice<TieRule> { Value = TieRule.MoveUp, Label = "Move the upper (or right-hand) one" },
            new Choice<TieRule> { Value = TieRule.MoveDown, Label = "Move the lower (or left-hand) one" },
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

        // ── 6.1.0: every former hard-coded value is a user option (defaults = 6.0.0 behaviour) ──

        private const double DefaultMinOverlapMm = 0.0;
        private const double DefaultZoomMarginPercent = 10.0;
        private const double MaxOptionMm = 1000.0;

        /// <summary>Minimum overlap (mm) text box: overlaps not deeper than this on both axes are ignored.</summary>
        [ObservableProperty]
        private string minOverlapText = "0.00";

        /// <summary>Alignment tolerance (mm) text box, for the "Aligned" column.</summary>
        [ObservableProperty]
        private string alignToleranceText = "0.10";

        /// <summary>Zoom margin (%) text box.</summary>
        [ObservableProperty]
        private string zoomMarginText = "10";

        /// <summary>Move left / right instead of up / down.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVertical), nameof(UpLabel), nameof(DownLabel), nameof(UpHint), nameof(DownHint))]
        private bool horizontal;

        /// <summary>Radio button: Vertical (up / down).</summary>
        public bool IsVertical { get => !Horizontal; set { if (value) Horizontal = false; } }

        /// <summary>Label of the first mode: "Move Up" or "Move Right".</summary>
        public string UpLabel => Horizontal ? "Move Right" : "Move Up";

        /// <summary>Label of the second mode: "Move Down" or "Move Left".</summary>
        public string DownLabel => Horizontal ? "Move Left" : "Move Down";

        /// <summary>Hint under the first mode.</summary>
        public string UpHint => Horizontal ? "The right-hand element moves right until the pair is clear." : "The upper element moves up until the pair is clear.";

        /// <summary>Hint under the second mode.</summary>
        public string DownHint => Horizontal ? "The left-hand element moves left until the pair is clear." : "The lower element moves down until the pair is clear.";

        /// <summary>Dimension handling choices.</summary>
        public IReadOnlyList<Choice<DimensionHandling>> DimensionOptions { get; } = new[]
        {
            new Choice<DimensionHandling> { Value = DimensionHandling.WholeThenText, Label = "Move the whole dimension (else its text)" },
            new Choice<DimensionHandling> { Value = DimensionHandling.TextOnly, Label = "Move only the dimension text" },
            new Choice<DimensionHandling> { Value = DimensionHandling.Never, Label = "Never move dimensions" }
        };

        /// <summary>Selected dimension handling.</summary>
        [ObservableProperty]
        private Choice<DimensionHandling> selectedDimension;

        /// <summary>Re-check overlaps after each resolve (off: the grid keeps its rows and shows statuses).</summary>
        [ObservableProperty]
        private bool recheckAfterResolve = true;

        /// <summary>Select the moved elements in Revit after a resolve.</summary>
        [ObservableProperty]
        private bool selectMoved;

        /// <summary>Select the element when zooming to it from the grid.</summary>
        [ObservableProperty]
        private bool selectOnZoom = true;

        /// <summary>Show the Options card.</summary>
        [ObservableProperty]
        private bool optionsExpanded;

        /// <summary>Puts every option of the Options card back to its default (the 6.0.0 behaviour).</summary>
        [RelayCommand]
        private void RestoreDefaults()
        {
            ApplyOptions(new AnnotationOverlapSettings());
            HasError = false;
            InfoMessage = "Options restored to their defaults. Press Check Overlaps to apply them to the grid.";
            HasInfo = true;
        }

        private void ApplyOptions(AnnotationOverlapSettings s)
        {
            MinOverlapText = Clamp(s.MinOverlapMm, 0, MaxOptionMm, DefaultMinOverlapMm).ToString("F2", CultureInfo.CurrentCulture);
            AlignToleranceText = Clamp(s.AlignToleranceMm, 0, MaxOptionMm, OverlapDetectionHelper.DefaultAlignmentToleranceMm).ToString("F2", CultureInfo.CurrentCulture);
            ZoomMarginText = Clamp(s.ZoomMarginPercent, 0, 500, DefaultZoomMarginPercent).ToString("0.##", CultureInfo.CurrentCulture);
            Horizontal = s.Horizontal;
            SelectedDimension = DimensionOptions.FirstOrDefault(d => (int)d.Value == s.Dimensions) ?? DimensionOptions[0];
            RecheckAfterResolve = s.RecheckAfterResolve;
            SelectMoved = s.SelectMoved;
            SelectOnZoom = s.SelectOnZoom;
        }

        private static double Clamp(double v, double min, double max, double fallback) =>
            double.IsNaN(v) || v < min || v > max ? fallback : v;

        private static bool TryParseNumber(string text, double min, double max, out double value)
        {
            string s = (text ?? string.Empty).Trim();
            bool ok = double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                   || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return ok && !double.IsNaN(value) && value >= min && value <= max;
        }

        // Detection values in use, validated in RunCheck.
        private double _minOverlapMm = DefaultMinOverlapMm;
        private double _alignToleranceMm = OverlapDetectionHelper.DefaultAlignmentToleranceMm;

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

            var settings = SettingsFileExists(SettingsFolder)
                ? SettingsService<AnnotationOverlapSettings>.Load(SettingsFolder)
                : SettingsService<AnnotationOverlapSettings>.Load(SettingsFolderV005);
            Mode = Enum.IsDefined(typeof(ResolveMode), settings.Mode) ? (ResolveMode)settings.Mode : ResolveMode.Distribute;
            double gap = settings.GapMm >= 0 && settings.GapMm <= OverlapResolver.MaxGapMm ? settings.GapMm : 1.0;
            GapText = gap.ToString("F2", CultureInfo.CurrentCulture);

            // V006 priority settings (a V005 settings file has none: defaults are the recommended order).
            AcrossTypes = settings.AcrossTypes;
            ApplyOptions(settings);
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
                Horizontal = Horizontal,
                Dimensions = (int)(SelectedDimension?.Value ?? DimensionHandling.WholeThenText),
                RecheckAfterResolve = RecheckAfterResolve,
                SelectMoved = SelectMoved,
                SelectOnZoom = SelectOnZoom
            };
            if (TryParseNumber(MinOverlapText, 0, MaxOptionMm, out double minOverlap)) settings.MinOverlapMm = minOverlap;
            if (TryParseNumber(AlignToleranceText, 0, MaxOptionMm, out double align)) settings.AlignToleranceMm = align;
            if (TryParseNumber(ZoomMarginText, 0, 500, out double margin)) settings.ZoomMarginPercent = margin;
            if (TryParseGap(out double gap))
                settings.GapMm = gap;
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

            if (!TryParseNumber(MinOverlapText, 0, MaxOptionMm, out double minOverlap))
            {
                ShowError($"Minimum overlap must be a number from 0 to {MaxOptionMm:F0} mm (Options).");
                return false;
            }
            if (!TryParseNumber(AlignToleranceText, 0, MaxOptionMm, out double align))
            {
                ShowError($"Alignment tolerance must be a number from 0 to {MaxOptionMm:F0} mm (Options).");
                return false;
            }
            _minOverlapMm = minOverlap;
            _alignToleranceMm = align;

            IsLoading = true;
            // Esc / Cancel with a Yes-No confirm during the (read-only) overlap scan.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.AnnotationOverlapDetectionV006, null);
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

            var results = OverlapDetectionHelper.DetectOverlaps(filtered, AcrossTypes, _minOverlapMm, _alignToleranceMm);

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
            if (!TryParseNumber(ZoomMarginText, 0, 500, out double margin))
            {
                ShowError("Zoom margin must be a number from 0 to 500 % (Options).");
                return;
            }
            _zoomHandler.ElementIdToZoom = new ElementId(elementIdValue);
            _zoomHandler.PaddingRatio = margin / 100.0;
            _zoomHandler.SelectOnZoom = SelectOnZoom;
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
                Horizontal = Horizontal,
                Dimensions = SelectedDimension?.Value ?? DimensionHandling.WholeThenText,
                SelectMoved = SelectMoved
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
            if (RecheckAfterResolve)
            {
                RunCheck();
            }
            else
            {
                // Keep the rows as they were and show each pair's result in the Status column.
                var rows = OverlapResults.ToList();
                OverlapResults.Clear();
                foreach (var r in rows)
                {
                    r.Status = _lastStatus.TryGetValue(ResolveOutcome.PairKey(r.ElementId1, r.ElementId2), out var st) ? st : "Not resolved";
                    OverlapResults.Add(r);
                }
            }

            if (!string.IsNullOrEmpty(outcome.Error))
            {
                ShowError(outcome.Error);
                return;
            }

            int total = outcome.PairsResolved + outcome.PairsSkipped;
            string text = $"Resolved {outcome.PairsResolved} of {total} pair(s): moved {outcome.ElementsMoved} element(s).";
            if (outcome.PairsSkipped > 0)
                text += $" {outcome.PairsSkipped} pair(s) skipped or failed; see the Status column.";
            if (outcome.ElementsMoved > 0)
                text += " One Undo in Revit reverts this run.";
            if (!RecheckAfterResolve)
                text += " The grid was not re-checked (Options); press Check Overlaps to refresh it.";
            else if (TotalOverlaps > 0)
                text += $" {TotalOverlaps} overlap(s) remain.";

            InfoMessage = text;
            HasInfo = true;
        }

        private bool TryParseGap(out double gap)
        {
            string s = (GapText ?? string.Empty).Trim();
            bool ok = double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out gap)
                   || double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out gap);
            return ok && !double.IsNaN(gap) && gap >= 0 && gap <= OverlapResolver.MaxGapMm;
        }

        private void ShowError(string message)
        {
            ErrorMessage = message;
            HasError = true;
        }
    }
}
