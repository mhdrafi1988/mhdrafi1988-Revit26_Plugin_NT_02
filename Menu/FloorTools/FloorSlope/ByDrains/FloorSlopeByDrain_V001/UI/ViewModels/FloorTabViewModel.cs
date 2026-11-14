// =======================================================
// File: FloorTabViewModel.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByDrain.V001.UI.ViewModels
// One tab per picked floor: the detected-drains grid, its size filter and
// selection commands, and (after Run) that floor's status and results.
// Changes vs V012:
//   - SelectedDrainsCount counts every checked drain. V012 counted only the
//     drains visible under the size filter, while Run used every checked
//     drain (GetSelectedDrains), so the count and the run could disagree.
//     HiddenSelectedCount says how many checked drains the filter hides.
//   - Status (Ready / No drains / Running / Done / Rolled back / Not started
//     / Skipped / Failed) for the pill on the tab header.
//   - Results for the side panel: Longest 10 and Skipped rows, same columns
//     as the Excel "Vertex Data" sheet.
//   - Timing uses decimal seconds.
//   - One DrainDetectionService for the filter (V012 created one per row).
// =======================================================

using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Core.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.FloorSlopeByDrain.V001.UI.ViewModels
{
    public enum FloorTabStatus
    {
        Ready,
        NoDrains,
        Running,
        Done,
        RolledBack,
        NotStarted,
        Skipped,
        Failed
    }

    public partial class FloorTabViewModel : ObservableObject
    {
        private const int ResultRowCount = 10;
        private readonly DrainDetectionService _detection = new DrainDetectionService();

        public FloorData FloorData { get; }
        public ElementId FloorId => FloorData.Floor.Id;
        public long FloorIdValue => FloorData.Floor.Id.Value;
        public string FloorName => FloorData.Floor.Name;
        public int TabNumber { get; }

        /// <summary>Tab header text, e.g. "Floor 1 — Generic 300mm".</summary>
        public string TabHeader { get; }

        public string FloorSubtitle { get; }

        public ObservableCollection<DrainItem> AllDrains { get; } = new ObservableCollection<DrainItem>();
        public ObservableCollection<string> SizeFilters { get; } = new ObservableCollection<string>();
        public ICollectionView FilteredDrainsView { get; }

        /// <summary>
        /// Groups the user expanded or collapsed by hand, by group name. A group
        /// not in here starts expanded only when it is the first group in the
        /// current view. Cleared when the size filter changes.
        /// </summary>
        public Dictionary<string, bool> GroupExpandedOverride { get; } = new Dictionary<string, bool>();

        [ObservableProperty]
        private string selectedSizeFilter = "All";

        partial void OnSelectedSizeFilterChanged(string value)
        {
            GroupExpandedOverride.Clear();
            FilteredDrainsView.Refresh();
            UpdateSelectedCount();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectionNote))]
        private int selectedDrainsCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectionNote))]
        private int hiddenSelectedCount;

        public int TotalDetectedCount { get; }

        public bool HasNoDrainsSelected => SelectedDrainsCount == 0;

        public string SelectionNote => SelectedDrainsCount == 0
            ? "No drains checked on this floor, so it will be skipped. Tick at least one drain to include it."
            : $"{SelectedDrainsCount} of {TotalDetectedCount} drains checked · Floor Id {FloorIdValue}"
              + (HiddenSelectedCount > 0 ? $" · {HiddenSelectedCount} checked drain(s) hidden by the size filter will still run" : string.Empty);

        /// <summary>One-line default-selection summary, or null when the floor has no circle drains.</summary>
        public string DefaultSelectionSummary { get; }

        // ── Status (tab pill) ──────────────────────────────────────────────
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [NotifyPropertyChangedFor(nameof(DisplayStatus))]
        [NotifyPropertyChangedFor(nameof(ResultsEmptyText))]
        private FloorTabStatus status = FloorTabStatus.Ready;

        /// <summary>Status, with "Ready" shown as "No drains" when nothing is checked.</summary>
        public FloorTabStatus DisplayStatus =>
            Status == FloorTabStatus.Ready && SelectedDrainsCount == 0 ? FloorTabStatus.NoDrains : Status;

        public string StatusText => DisplayStatus switch
        {
            FloorTabStatus.NoDrains => "No drains",
            FloorTabStatus.Running => "Running",
            FloorTabStatus.Done => "Done",
            FloorTabStatus.RolledBack => "Rolled back",
            FloorTabStatus.NotStarted => "Not started",
            FloorTabStatus.Skipped => "Skipped",
            FloorTabStatus.Failed => "Failed",
            _ => "Ready"
        };

        // ── Last result (banner + side panel) ──────────────────────────────
        [ObservableProperty] private bool hasRunResult;
        [ObservableProperty] private string bannerText = string.Empty;
        [ObservableProperty] private string timingDisplay = string.Empty;

        public AutoSlopeDrainResult LastResult { get; private set; }

        public ObservableCollection<DrainResultRow> LongestRows { get; } = new ObservableCollection<DrainResultRow>();
        public ObservableCollection<DrainResultRow> SkippedRows { get; } = new ObservableCollection<DrainResultRow>();

        [ObservableProperty] private int skippedTotal;

        public bool HasResults => LastResult?.Success == true;

        public string ResultsEmptyText => DisplayStatus switch
        {
            FloorTabStatus.Running => "Running… the table fills when this floor finishes.",
            FloorTabStatus.NotStarted => "This floor has not run yet in this batch.",
            FloorTabStatus.RolledBack => "This floor was rolled back, so there is nothing to show.",
            FloorTabStatus.Failed => "This floor's run failed, so there is nothing to show.",
            FloorTabStatus.Skipped => "This floor was skipped (no drains checked).",
            FloorTabStatus.NoDrains => "No drains are checked on this floor, so it will not run.",
            FloorTabStatus.Done => "No rows.",
            _ => "Run Floor Slope to see this floor's results."
        };

        public FloorTabViewModel(FloorData floorData, int tabNumber)
        {
            FloorData = floorData;
            TabNumber = tabNumber;
            string shortName = (floorData.Floor.Name ?? "Floor").Replace("Basic Floor – ", "").Replace("Basic Floor - ", "");
            TabHeader = $"Floor {tabNumber} — {shortName}";
            FloorSubtitle = $"Floor: {floorData.Floor.Name} (Id {floorData.Floor.Id.Value})";

            foreach (var drain in floorData.DetectedDrains)
            {
                AllDrains.Add(drain);
                drain.PropertyChanged += OnDrainPropertyChanged;
            }

            foreach (var category in _detection.GenerateSizeCategories(floorData.DetectedDrains))
                SizeFilters.Add(category);

            FilteredDrainsView = CollectionViewSource.GetDefaultView(AllDrains);
            FilteredDrainsView.Filter = FilterDrainItem;
            FilteredDrainsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DrainItem.ShapeGroup)));
            FilteredDrainsView.SortDescriptions.Add(new SortDescription(nameof(DrainItem.ShapeGroupOrder), ListSortDirection.Ascending));
            FilteredDrainsView.SortDescriptions.Add(new SortDescription(nameof(DrainItem.SizeSortKey), ListSortDirection.Ascending));

            // After FilteredDrainsView exists: setting IsSelected raises UpdateSelectedCount.
            DefaultSelectionSummary = ApplyDefaultCircleSelection();
            TotalDetectedCount = floorData.DetectedDrains.Count;
            UpdateSelectedCount();
        }

        /// <summary>Selects circles with a diameter of 90–110 mm; other circles start unchecked.</summary>
        private string ApplyDefaultCircleSelection()
        {
            const double minMm = 90.0;
            const double maxMm = 110.0;

            var circles = AllDrains.Where(d => d.ShapeGroup == "Circle" && d.Diameter.HasValue).ToList();
            if (circles.Count == 0) return null;

            int kept = 0;
            foreach (var circle in circles)
            {
                bool keep = circle.Diameter.Value >= minMm && circle.Diameter.Value <= maxMm;
                circle.IsSelected = keep;
                if (keep) kept++;
            }
            return $"Default selection: {kept} circle(s) with ⌀ 90–110 mm (of {circles.Count} circle(s) detected).";
        }

        private void OnDrainPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DrainItem.IsSelected)) return;
            UpdateSelectedCount();
        }

        /// <summary>Raised whenever the checked set changes, so the parent can re-evaluate Run.</summary>
        public event System.Action SelectionChanged;

        private bool PassesFilter(DrainItem drain) =>
            SelectedSizeFilter == "All" || _detection.FilterDrainsBySize(new List<DrainItem> { drain }, SelectedSizeFilter).Any();

        private bool FilterDrainItem(object obj) => obj is DrainItem drain && PassesFilter(drain);

        private void ForVisible(System.Action<DrainItem> action)
        {
            foreach (var d in FilteredDrainsView.Cast<DrainItem>().ToList()) action(d);
            FilteredDrainsView.Refresh();
            UpdateSelectedCount();
        }

        [RelayCommand] private void SelectAll() => ForVisible(d => d.IsSelected = true);
        [RelayCommand] private void SelectNone() => ForVisible(d => d.IsSelected = false);
        [RelayCommand] private void InvertSelection() => ForVisible(d => d.IsSelected = !d.IsSelected);

        [RelayCommand]
        private void SelectAllInGroup(object groupName)
        {
            if (groupName is string name) ForVisible(d => { if (d.ShapeGroup == name) d.IsSelected = true; });
        }

        [RelayCommand]
        private void SelectNoneInGroup(object groupName)
        {
            if (groupName is string name) ForVisible(d => { if (d.ShapeGroup == name) d.IsSelected = false; });
        }

        private void UpdateSelectedCount()
        {
            SelectedDrainsCount = AllDrains.Count(d => d.IsSelected);
            HiddenSelectedCount = AllDrains.Count(d => d.IsSelected && !PassesFilter(d));
            OnPropertyChanged(nameof(HasNoDrainsSelected));
            OnPropertyChanged(nameof(DisplayStatus));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ResultsEmptyText));
            SelectionChanged?.Invoke();
        }

        /// <summary>Every checked drain, including any the size filter hides (the count above says how many).</summary>
        public List<DrainItem> GetSelectedDrains() => AllDrains.Where(d => d.IsSelected).ToList();

        // ── Run lifecycle ──────────────────────────────────────────────────

        /// <summary>Called when a batch starts: runnable tabs wait, the rest are skipped.</summary>
        public void PrepareForRun(bool willRun)
        {
            ClearResults();
            Status = willRun ? FloorTabStatus.NotStarted : FloorTabStatus.Skipped;
            BannerText = willRun ? string.Empty : "Skipped · no drains were checked on this floor";
            HasRunResult = !willRun;
        }

        public void MarkRunning()
        {
            Status = FloorTabStatus.Running;
            BannerText = string.Empty;
            HasRunResult = false;
        }

        /// <summary>Called when a batch ends: floors that never started say so.</summary>
        public void FinishBatch()
        {
            if (Status == FloorTabStatus.NotStarted)
            {
                BannerText = "Not started · the run was cancelled before this floor";
                HasRunResult = true;
            }
            else if (Status == FloorTabStatus.Running)
            {
                Status = FloorTabStatus.RolledBack;
                BannerText = "Rolled back · this floor is exactly as it was before Run";
                HasRunResult = true;
            }
        }

        public void ApplyRunResult(AutoSlopeDrainResult result)
        {
            LastResult = result;
            HasRunResult = true;
            TimingDisplay = result.StartedAt == default
                ? string.Empty
                : $"{result.StartedAt:HH:mm:ss} → {result.EndedAt:HH:mm:ss} · {result.DurationSeconds:0.0} s";

            if (result.Success)
            {
                Status = FloorTabStatus.Done;
                BannerText = $"Done · {result.VerticesProcessed:N0} processed, {result.VerticesSkipped:N0} skipped · " +
                             $"longest {result.LongestPath_m:0.00} m · highest {result.HighestElevation_mm:0} mm";
                FillResults(result);
            }
            else
            {
                string timing = TimingDisplay;
                ClearResults();
                TimingDisplay = timing;
                Status = result.WasCancelled ? FloorTabStatus.RolledBack : FloorTabStatus.Failed;
                BannerText = result.WasCancelled
                    ? "Rolled back · cancelled while this floor was running; it is exactly as it was before Run"
                    : $"Failed · {result.ErrorMessage} · rolled back";
            }
        }

        private void FillResults(AutoSlopeDrainResult r)
        {
            LongestRows.Clear();
            SkippedRows.Clear();

            int n = 1;
            foreach (var v in r.Vertices.Where(v => v.WasProcessed).OrderByDescending(v => v.PathLengthMeters).Take(ResultRowCount))
                LongestRows.Add(DrainResultRow.FromProcessed(v, n++, r.SlopePercent));

            var skipped = r.Vertices.Where(v => !v.WasProcessed && !v.IsDrain)
                .OrderBy(v => v.SkipReason == "Beyond distance" ? 1 : 0)
                .ThenBy(v => v.PathLengthMeters)
                .ToList();
            SkippedTotal = skipped.Count;
            n = 1;
            foreach (var v in skipped.Take(ResultRowCount))
                SkippedRows.Add(DrainResultRow.FromSkipped(v, n++));

            OnPropertyChanged(nameof(HasResults));
        }

        private void ClearResults()
        {
            LastResult = null;
            LongestRows.Clear();
            SkippedRows.Clear();
            SkippedTotal = 0;
            TimingDisplay = string.Empty;
            OnPropertyChanged(nameof(HasResults));
        }
    }
}
