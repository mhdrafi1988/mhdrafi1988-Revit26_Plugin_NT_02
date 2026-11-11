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
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Helpers;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V005.ViewModels
{
    /// <summary>
    /// View model of the V005 window: V004's scan and results grid, plus Resolve Overlaps
    /// (Move Up / Move Down / Distribute) through <see cref="MoveAnnotationsEventHandler"/>.
    /// </summary>
    public partial class AnnotationOverlapViewModel : ObservableObject
    {
        private const string SettingsFolder = "AnnotationOverlapDetection";

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
        [NotifyPropertyChangedFor(nameof(IsMoveUp), nameof(IsMoveDown), nameof(IsDistribute))]
        private ResolveMode mode = ResolveMode.Distribute;

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

            var settings = SettingsService<AnnotationOverlapSettings>.Load(SettingsFolder);
            Mode = Enum.IsDefined(typeof(ResolveMode), settings.Mode) ? (ResolveMode)settings.Mode : ResolveMode.Distribute;
            double gap = settings.GapMm >= 0 && settings.GapMm <= OverlapResolver.MaxGapMm ? settings.GapMm : 1.0;
            GapText = gap.ToString("F2", CultureInfo.CurrentCulture);

            LoadAnnotationFamilies(doc);
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
            var settings = new AnnotationOverlapSettings { Mode = (int)Mode };
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

            IsLoading = true;
            // Esc / Cancel with a Yes-No confirm during the (read-only) overlap scan.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.AnnotationOverlapDetectionV005, null);
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

            var grouped = OverlapDetectionHelper.GroupByType(filtered);
            var results = OverlapDetectionHelper.DetectOverlaps(grouped);

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

            if (IsLoading)
                return;

            _moveHandler.Request = new ResolveRequest
            {
                Document = _doc,
                ViewId = _viewId,
                Pairs = rows.Select(r => (r.ElementId1, r.ElementId2)).ToList(),
                Mode = Mode,
                GapMm = gap
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
            if (outcome.ElementsMoved > 0)
                text += " One Undo in Revit reverts this run.";
            if (TotalOverlaps > 0)
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
