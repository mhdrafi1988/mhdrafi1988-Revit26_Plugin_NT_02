// File: AutoSlopeDrainViewModel.cs
// Location: UI/ViewModels/
// Base: AutoSlopeByDrain V004's AutoSlopeDrainViewModel (Select All/None/
// Invert, size filter + DataGrid sorting via ICollectionView, Run wiring
// through AutoSlopeDrainEventManager).
//
// CHANGES (V005), per Rafi's confirmed decisions on 2026-07-21:
//   REWRITTEN using CommunityToolkit.Mvvm's ObservableObject +
//     [ObservableProperty] + [RelayCommand] source-generator attributes,
//     instead of the hand-rolled INotifyPropertyChanged + plain
//     RelayCommand pattern both V004 and ByPoint V018 actually used. This
//     is an intentional deviation from both prior tools' pattern, flagged
//     explicitly per Rafi's request â€” requires a confirmed
//     CommunityToolkit.Mvvm PackageReference in the .csproj (Rafi is
//     wiring the .csproj himself; not included here).
//   ADDED   ThresholdMeters (Max Path Distance) input, distinct from
//     ConnectionThresholdMeters (Max Edge Distance) â€” ported from ByPoint.
//   ADDED   InsertCurveIntersectionPoints toggle (off by default).
//   ADDED   VerifyElevationsAfterCommit toggle (OFF by default).
//   CHANGED export: Excel-only (ExportToExcel replaces ExportToCsv),
//     single exported file path instead of Detailed+Summary pair.
//   ADDED   settings persistence via SettingsService â€” loads on
//     construction, saves after a successful Run and can be called on
//     window close from the code-behind.
//   ADDED   CanRun / RunDisabledReason surfaced so the View can show an
//     inline hint ("Select at least one drain to run") when the Run
//     button is disabled specifically because 0 drains are selected â€”
//     confirmed UX decision, not a silent disable with no explanation.
//   ADDED   TotalDetectedCount / SelectedCount / ArcsCalculated metric
//     bindings for the new UI metric cards.

using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.AutoSlopeByDrain.V007.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByDrain.V007.Core.Services;
using Revit26_Plugin_Revised.AutoSlopeByDrain.V007.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.AutoSlopeByDrain.V007.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.AutoSlopeByDrain.V007.UI.ViewModels
{
    public partial class AutoSlopeDrainViewModel : ObservableObject
    {
        // Must be public (not private) â€” [ObservableProperty] generates a public
        // State property, and a public property cannot expose a less-accessible
        // type. The enum is still only meant for internal use within this
        // ViewModel; nothing outside binds to it directly.
        // NEW: Cancelled added, per Rafi's confirmed Cancel decision.
        public enum RunState { Ready, Running, Done, Cancelled }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusMessage))]
        [NotifyPropertyChangedFor(nameof(LongestPathDisplay))]
        [NotifyPropertyChangedFor(nameof(HighestElevationDisplay))]
        [NotifyCanExecuteChangedFor(nameof(RunAutoSlopeCommand))]
        private RunState state = RunState.Ready;

        private bool IsRunning => State == RunState.Running;
        private bool IsComplete => State == RunState.Done;

        /// <summary>NEW. Done OR Cancelled â€” used only to gate display of real numbers; NOT used for Run-button re-enablement (see IsComplete), so cancelling never permanently locks the Run button.</summary>
        private bool IsFinished => State == RunState.Done || State == RunState.Cancelled;

        public string StatusMessage => State switch
        {
            RunState.Running => "Processing...",
            RunState.Done => "Completed",
            RunState.Cancelled => "Cancelled",
            _ => "Ready"
        };

        // â”€â”€ Roof / drains â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public UIDocument UIDoc { get; }
        public UIApplication App { get; }
        private readonly RoofData _roofData;
        public ElementId RoofId => _roofData.Roof.Id;

        public ObservableCollection<DrainItem> AllDrains { get; } = new ObservableCollection<DrainItem>();
        public ObservableCollection<string> SizeFilters { get; } = new ObservableCollection<string>();
        public ICollectionView FilteredDrainsView { get; }

        [ObservableProperty]
        private string roofSubtitle;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunAutoSlopeCommand))]
        private string selectedSizeFilter = "All";

        partial void OnSelectedSizeFilterChanged(string value)
        {
            FilteredDrainsView.Refresh();
            UpdateSelectedCount();
        }

        [ObservableProperty]
        private int selectedDrainsCount;

        /// <summary>
        /// NEW (V005). True only when the Run button is disabled specifically
        /// because 0 drains are selected â€” the View shows an inline hint in
        /// this case rather than a silent disable with no explanation, per
        /// Rafi's confirmed UX decision.
        /// </summary>
        public bool ShowNoDrainsSelectedHint => !IsRunning && !IsComplete && SelectedDrainsCount == 0;

        // â”€â”€ Slope inputs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public List<string> SlopeOptions { get; } = new List<string> { "1.0", "1.5", "2.0", "2.5", "3.0" };

        [ObservableProperty]
        private string slopeInput = AppConstants.DefaultSlopePercent.ToString("0.0");

        [ObservableProperty]
        private string connectionThresholdInput = AppConstants.DefaultConnectionThresholdMeters.ToString();

        /// <summary>NEW (V005): Max Path Distance input, ported from ByPoint. Distinct from ConnectionThresholdInput.</summary>
        [ObservableProperty]
        private string thresholdInput = AppConstants.DefaultThresholdMeters.ToString();

        [ObservableProperty]
        private string pathSampleCountInput = AppConstants.DefaultPathSampleCount.ToString();

        /// <summary>NEW (V005), ported from ByPoint. Opt-in, off by default.</summary>
        [ObservableProperty]
        private bool insertCurveIntersectionPoints = false;

        /// <summary>NEW (V005). Opt-in, OFF by default per Rafi's confirmed decision.</summary>
        [ObservableProperty]
        private bool verifyElevationsAfterCommit = false;

        // â”€â”€ Circle Markers â€” ported from AutoSlopeByPoint â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>Style/config for circles placed on selected drain openings.</summary>
        public CircleMarkerGroup DrainMarkerGroup { get; } = new CircleMarkerGroup
        {
            GroupLabel = "Drain",
            ColorName = "Blue",
            RadiusMm = 250
        };

        /// <summary>Style/config for circles placed on vertices tied at max elevation.</summary>
        public CircleMarkerGroup HighestPointMarkerGroup { get; } = new CircleMarkerGroup
        {
            GroupLabel = "Highest Point",
            ColorName = "Red",
            RadiusMm = 250
        };

        /// <summary>Named colors offered in each group's Color dropdown.</summary>
        public IReadOnlyList<string> ColorPalette { get; } = NamedColorHelper.PaletteNames;

        /// <summary>
        /// Project's existing Line Style options (OST_Lines subcategories),
        /// populated in the constructor via FilteredElementCollector. The tool
        /// never creates new line styles â€” user picks from what already exists.
        /// </summary>
        public ObservableCollection<LineStyleOption> LineStyleOptions { get; } = new ObservableCollection<LineStyleOption>();

        // â”€â”€ Export â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        private string exportFolderPath;

        // â”€â”€ Log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();

        // â”€â”€ Results / metrics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LongestPathDisplay))]
        private double longestPath_m;
        public string LongestPathDisplay => IsFinished ? LongestPath_m.ToString("F2") : "N/A";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HighestElevationDisplay))]
        private double highestElevation_mm;
        public string HighestElevationDisplay => IsFinished ? HighestElevation_mm.ToString("F0") : "N/A";

        [ObservableProperty]
        private int runDuration_sec;

        // â”€â”€ Progress / Cancel â€” NEW, per Rafi's confirmed decisions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        private bool isProgressVisible;

        [ObservableProperty]
        private double progressPercent;

        [ObservableProperty]
        private bool progressIsIndeterminate = true;

        [ObservableProperty]
        private string progressPhaseText = "";

        private CancellationTokenSource _runCts;

        [ObservableProperty]
        private int finalDrainCount;

        /// <summary>NEW (V005): drains DrainDetectionService found, before selection.</summary>
        [ObservableProperty]
        private int totalDetectedCount;

        /// <summary>NEW (V005): drains checked in the grid at Run time (== FinalDrainCount, kept as a separate binding for clarity in the UI).</summary>
        [ObservableProperty]
        private int selectedCount;

        /// <summary>NEW (V005): boundary/opening arcs found. 0 unless InsertCurveIntersectionPoints was on.</summary>
        [ObservableProperty]
        private int arcsCalculated;

        /// <summary>Circles placed on selected drain openings this run.</summary>
        [ObservableProperty]
        private int drainCirclesPlaced;

        /// <summary>Circles placed on vertices tied at max elevation this run.</summary>
        [ObservableProperty]
        private int highestCirclesPlaced;

        private AutoSlopeDrainResult _lastResult;

        // â”€â”€ Constructor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public AutoSlopeDrainViewModel(UIDocument uidoc, UIApplication app, RoofData roofData)
        {
            UIDoc = uidoc;
            App = app;
            _roofData = roofData;

            RoofSubtitle = $"Revit 2026 Â· Roof: {roofData.Roof.Name} (Id {roofData.Roof.Id.Value})";

            var detectionService = new DrainDetectionService();

            foreach (var drain in roofData.DetectedDrains)
            {
                AllDrains.Add(drain);
                drain.PropertyChanged += OnDrainPropertyChanged;
            }

            foreach (var category in detectionService.GenerateSizeCategories(roofData.DetectedDrains))
                SizeFilters.Add(category);

            FilteredDrainsView = CollectionViewSource.GetDefaultView(AllDrains);
            FilteredDrainsView.Filter = FilterDrainItem;

            // â”€â”€ Grouped/sorted drain grid â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Groups by ShapeGroup (Circle / Rectangle / Other), group order fixed
            // via ShapeGroupOrder (not detection order), and within each group
            // sorted by opening size (bounding-box area) ascending.
            FilteredDrainsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DrainItem.ShapeGroup)));
            FilteredDrainsView.SortDescriptions.Add(new SortDescription(nameof(DrainItem.ShapeGroupOrder), ListSortDirection.Ascending));
            FilteredDrainsView.SortDescriptions.Add(new SortDescription(nameof(DrainItem.SizeSortKey), ListSortDirection.Ascending));

            TotalDetectedCount = roofData.DetectedDrains.Count;

            LoadLineStyleOptions();

            // â”€â”€ NEW (V005): load persisted settings, falling back to AppConstants
            // defaults for anything not yet saved (first run, or a fresh machine).
            var settings = SettingsService.Load(msg => AddLog(new LogEntry(LogLevel.Warning, msg)));

            SlopeInput = settings.SlopePercent.ToString("0.0");
            ConnectionThresholdInput = settings.ConnectionThresholdMeters.ToString("0");
            ThresholdInput = settings.ThresholdMeters.ToString("0");
            PathSampleCountInput = settings.PathSampleCount.ToString();
            InsertCurveIntersectionPoints = settings.InsertCurveIntersectionPoints;
            VerifyElevationsAfterCommit = settings.VerifyElevationsAfterCommit;
            SelectedSizeFilter = string.IsNullOrEmpty(settings.SelectedSizeFilter) ? "All" : settings.SelectedSizeFilter;

            ApplyMarkerSettings(DrainMarkerGroup, settings.DrainMarkerGroup);
            ApplyMarkerSettings(HighestPointMarkerGroup, settings.HighestPointMarkerGroup);

            ExportFolderPath = !string.IsNullOrWhiteSpace(settings.ExportFolderPath) && Directory.Exists(settings.ExportFolderPath)
                ? settings.ExportFolderPath
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    AppConstants.DefaultExportFolder);

            if (!Directory.Exists(ExportFolderPath))
                Directory.CreateDirectory(ExportFolderPath);

            AddLog(new LogEntry(LogLevel.Info, $"Detected {roofData.DetectedDrains.Count} drain opening(s)."));
            AddLog(new LogEntry(LogLevel.Info, $"Default export folder: {ExportFolderPath}"));

            UpdateSelectedCount();

            AutoSlopeDrainEventManager.Init();
        }

        // â”€â”€ LoadLineStyleOptions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// Populates LineStyleOptions from the project's existing OST_Lines
        /// subcategories (GraphicsStyle elements) â€” ported from AutoSlopeByPoint.
        /// The tool never creates new line styles.
        /// </summary>
        private void LoadLineStyleOptions()
        {
            LineStyleOptions.Clear();

            Document doc = UIDoc?.Document;
            if (doc == null) return;

            Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (linesCategory?.SubCategories == null) return;

            foreach (Category sub in linesCategory.SubCategories)
            {
                if (sub?.GetGraphicsStyle(GraphicsStyleType.Projection) is GraphicsStyle gs)
                {
                    LineStyleOptions.Add(new LineStyleOption { Id = gs.Id, Name = sub.Name });
                }
            }

            var defaultOption = LineStyleOptions.FirstOrDefault(o => o.Name == "Thin Lines")
                                 ?? LineStyleOptions.FirstOrDefault();

            if (defaultOption != null)
            {
                foreach (var group in new[] { DrainMarkerGroup, HighestPointMarkerGroup })
                {
                    if (group.LineStyleId == null)
                    {
                        group.LineStyleId = defaultOption.Id;
                        group.LineStyleName = defaultOption.Name;
                    }
                }
            }
            else
            {
                AddLog(new LogEntry(LogLevel.Warning,
                    "Circle Markers: no Line Styles found in this project (OST_Lines has no subcategories)."));
            }
        }

        /// <summary>Applies a saved CircleMarkerGroupSettings onto a live CircleMarkerGroup. Must run AFTER LoadLineStyleOptions.</summary>
        private void ApplyMarkerSettings(CircleMarkerGroup group, CircleMarkerGroupSettings saved)
        {
            if (group == null || saved == null) return;

            group.IsEnabled = saved.IsEnabled;
            group.ColorName = string.IsNullOrWhiteSpace(saved.ColorName) ? group.ColorName : saved.ColorName;
            group.RadiusMm = saved.RadiusMm > 0 ? saved.RadiusMm : group.RadiusMm;

            if (!string.IsNullOrWhiteSpace(saved.LineStyleName))
            {
                var match = LineStyleOptions.FirstOrDefault(o => o.Name == saved.LineStyleName);
                if (match != null)
                {
                    group.LineStyleId = match.Id;
                    group.LineStyleName = match.Name;
                }
            }
        }

        private static CircleMarkerGroupSettings ToMarkerSettings(CircleMarkerGroup group) => new CircleMarkerGroupSettings
        {
            IsEnabled = group.IsEnabled,
            LineStyleName = group.LineStyleName,
            ColorName = group.ColorName,
            RadiusMm = group.RadiusMm
        };

        // â”€â”€ Filtering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void OnDrainPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DrainItem.IsSelected)) return;
            UpdateSelectedCount();
            RunAutoSlopeCommand.NotifyCanExecuteChanged();
        }

        private bool FilterDrainItem(object obj)
        {
            if (!(obj is DrainItem drain)) return false;
            if (SelectedSizeFilter == "All") return true;

            var svc = new DrainDetectionService();
            return svc.FilterDrainsBySize(new List<DrainItem> { drain }, SelectedSizeFilter).Any();
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var d in FilteredDrainsView.Cast<DrainItem>()) d.IsSelected = true;
            FilteredDrainsView.Refresh();
            UpdateSelectedCount();
            AddLog(new LogEntry(LogLevel.Info, $"Selected all {FilteredDrainsView.Cast<DrainItem>().Count()} visible drain(s)."));
            RunAutoSlopeCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SelectNone()
        {
            foreach (var d in FilteredDrainsView.Cast<DrainItem>()) d.IsSelected = false;
            FilteredDrainsView.Refresh();
            UpdateSelectedCount();
            AddLog(new LogEntry(LogLevel.Info, "Deselected all visible drains."));
            RunAutoSlopeCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void InvertSelection()
        {
            foreach (var d in FilteredDrainsView.Cast<DrainItem>()) d.IsSelected = !d.IsSelected;
            FilteredDrainsView.Refresh();
            UpdateSelectedCount();
            AddLog(new LogEntry(LogLevel.Info, $"Selection inverted: {AllDrains.Count(d => d.IsSelected)} selected."));
            RunAutoSlopeCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Checks every visible drain in one shape group ("Circle"/"Rectangle"/"Other") â€” bound to each DataGrid group header's "All" button.</summary>
        [RelayCommand]
        private void SelectAllInGroup(object groupName)
        {
            if (!(groupName is string name)) return;
            foreach (var d in FilteredDrainsView.Cast<DrainItem>().Where(d => d.ShapeGroup == name))
                d.IsSelected = true;
            FilteredDrainsView.Refresh();
            UpdateSelectedCount();
            RunAutoSlopeCommand.NotifyCanExecuteChanged();
        }

        /// <summary>Unchecks every visible drain in one shape group â€” bound to each DataGrid group header's "None" button.</summary>
        [RelayCommand]
        private void SelectNoneInGroup(object groupName)
        {
            if (!(groupName is string name)) return;
            foreach (var d in FilteredDrainsView.Cast<DrainItem>().Where(d => d.ShapeGroup == name))
                d.IsSelected = false;
            FilteredDrainsView.Refresh();
            UpdateSelectedCount();
            RunAutoSlopeCommand.NotifyCanExecuteChanged();
        }

        private void UpdateSelectedCount()
        {
            SelectedDrainsCount = FilteredDrainsView.Cast<DrainItem>().Count(d => d.IsSelected);
            OnPropertyChanged(nameof(ShowNoDrainsSelectedHint));
        }

        // â”€â”€ Run â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private bool CanRunAutoSlope() => !IsRunning && !IsComplete && AllDrains.Any(d => d.IsSelected);

        /// <summary>Raised after RunAutoSlope completes or bails out early (true =
        /// success), so callers outside this ViewModel (e.g. the Combined Roof
        /// Tools "Run All" orchestrator) can await completion without polling.</summary>
        public event Action<bool> RunCompleted;

        [RelayCommand(CanExecute = nameof(CanRunAutoSlope))]
        private void RunAutoSlope()
        {
            if (IsRunning || IsComplete) { RunCompleted?.Invoke(false); return; }

            if (!double.TryParse(SlopeInput, out double slopePercent) || slopePercent <= 0)
            {
                AddLog(new LogEntry(LogLevel.Error, "Please enter a valid positive slope percentage."));
                RunCompleted?.Invoke(false);
                return;
            }
            if (!double.TryParse(ConnectionThresholdInput, out double connectionThresholdM) || connectionThresholdM <= 0)
            {
                AddLog(new LogEntry(LogLevel.Error, "Please enter a valid positive Max Edge Distance (m)."));
                RunCompleted?.Invoke(false);
                return;
            }
            if (!double.TryParse(ThresholdInput, out double thresholdM) || thresholdM <= 0)
            {
                AddLog(new LogEntry(LogLevel.Error, "Please enter a valid positive Max Path Distance (m)."));
                RunCompleted?.Invoke(false);
                return;
            }
            if (!int.TryParse(PathSampleCountInput, out int pathSamples) || pathSamples < 2)
            {
                AddLog(new LogEntry(LogLevel.Error, "Path Samples must be a whole number of 2 or more."));
                RunCompleted?.Invoke(false);
                return;
            }

            // UPDATED (V005, second round): pass the checked DrainItem objects
            // directly, per Rafi's confirmed decision â€” no more lightweight
            // signatures. The Engine reuses these exact objects (including their
            // LoopCurves) rather than re-detecting and position-matching.
            var selectedDrainItems = AllDrains.Where(d => d.IsSelected).ToList();

            if (selectedDrainItems.Count == 0)
            {
                // Defensive backstop â€” CanRunAutoSlope already prevents this in
                // normal use via the disabled Run button + inline hint.
                AddLog(new LogEntry(LogLevel.Warning, "No drains selected for slope application."));
                RunCompleted?.Invoke(false);
                return;
            }

            State = RunState.Running;
            LogEntries.Clear();

            // NEW: fresh CancellationTokenSource per Run click. The bar starts
            // indeterminate ("Startingâ€¦") since nothing has reported yet.
            _runCts = new CancellationTokenSource();
            IsProgressVisible = true;
            ProgressPercent = 0;
            ProgressIsIndeterminate = true;
            ProgressPhaseText = "Startingâ€¦";

            AddLog(new LogEntry(LogLevel.Info, "Starting AutoSlope By Drain..."));

            if (!Directory.Exists(ExportFolderPath))
            {
                try { Directory.CreateDirectory(ExportFolderPath); }
                catch (Exception ex)
                {
                    AddLog(new LogEntry(LogLevel.Warning, $"Failed to create export directory: {ex.Message}"));
                }
            }

            AutoSlopeDrainHandler.Payload = new AutoSlopeDrainPayload
            {
                RoofId = RoofId,
                SelectedDrains = selectedDrainItems,
                TotalDetectedCount = TotalDetectedCount,
                SlopePercent = slopePercent,
                ConnectionThresholdMeters = connectionThresholdM,
                ThresholdMeters = thresholdM,
                PathSampleCount = pathSamples,
                InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,
                VerifyElevationsAfterCommit = VerifyElevationsAfterCommit,
                DrainMarkerGroup = DrainMarkerGroup,
                HighestPointMarkerGroup = HighestPointMarkerGroup,
                ProjectTitle = UIDoc?.Document?.Title ?? "Unknown Project",
                Log = AddLog,
                ExportConfig = new ExportConfig
                {
                    ExportPath = ExportFolderPath,
                    ExportToExcel = true
                },
                CancelToken = _runCts.Token,
                // NEW: called synchronously on the SAME thread as this run
                // (Revit's main thread), never via BeginInvoke â€” updates the
                // properties and then immediately forces WPF to repaint + process a
                // queued Cancel click.
                Progress = info =>
                {
                    ProgressPhaseText = info.PhaseLabel;

                    if (info.PercentWithinPhase.HasValue)
                    {
                        ProgressIsIndeterminate = false;
                        ProgressPercent = Math.Max(ProgressPercent, Math.Min(100.0, info.PercentWithinPhase.Value));
                    }
                    else
                    {
                        ProgressIsIndeterminate = true;
                    }

                    // Repaints the bar, delivers Esc / the Cancel button and asks "Cancel the process?".
                    // A confirmed cancel throws here and the engine rolls the run back.
                    ToolCancellation.CheckCurrent();
                },
                OnCompleted = result =>
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsProgressVisible = false;
                        _runCts = null;

                        if (!result.Success)
                        {
                            bool wasCancelled = result.WasCancelled;
                            AddLog(new LogEntry(
                                wasCancelled ? LogLevel.Warning : LogLevel.Error,
                                wasCancelled ? "Run cancelled by user." : $"Run failed: {result.ErrorMessage}"));
                            State = wasCancelled ? RunState.Cancelled : RunState.Ready;
                            RunCompleted?.Invoke(false);
                            return;
                        }

                        _lastResult = result;
                        LongestPath_m = result.LongestPath_m;
                        HighestElevation_mm = result.HighestElevation_mm;
                        RunDuration_sec = result.RunDuration_sec;
                        FinalDrainCount = result.DrainCount;
                        TotalDetectedCount = result.TotalDetectedCount;
                        SelectedCount = result.SelectedCount;
                        ArcsCalculated = result.ArcsCalculated;
                        DrainCirclesPlaced = result.DrainCirclesPlaced;
                        HighestCirclesPlaced = result.HighestCirclesPlaced;

                        State = RunState.Done;

                        // NEW (V005): persist settings after a successful run.
                        SettingsService.Save(new AutoSlopeDrainSettings
                        {
                            SlopePercent = slopePercent,
                            ConnectionThresholdMeters = connectionThresholdM,
                            ThresholdMeters = thresholdM,
                            PathSampleCount = pathSamples,
                            InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,
                            VerifyElevationsAfterCommit = VerifyElevationsAfterCommit,
                            ExportFolderPath = ExportFolderPath,
                            SelectedSizeFilter = SelectedSizeFilter,
                            DrainMarkerGroup = ToMarkerSettings(DrainMarkerGroup),
                            HighestPointMarkerGroup = ToMarkerSettings(HighestPointMarkerGroup)
                        }, msg => AddLog(new LogEntry(LogLevel.Warning, msg)));

                        if (!string.IsNullOrEmpty(result.ExportedFilePath))
                        {
                            var tdDrain = new TaskDialog("Export Complete");
                            tdDrain.MainContent = "Excel export completed. Open the export folder now?";
                            tdDrain.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                            if (tdDrain.Show() == TaskDialogResult.Yes)
                                System.Diagnostics.Process.Start("explorer.exe", ExportFolderPath);
                        }

                        RunCompleted?.Invoke(true);
                    }));
                }
            };

            AutoSlopeDrainEventManager.Event.Raise();
        }

        [RelayCommand]
        private void BrowseFolder()
        {
            var selected = DialogService.SelectFolder(ExportFolderPath);
            if (!string.IsNullOrEmpty(selected))
            {
                ExportFolderPath = selected;
                AddLog(new LogEntry(LogLevel.Info, $"Export folder set to: {ExportFolderPath}"));
            }
        }

        [RelayCommand]
        private void OpenExportFolder()
        {
            if (!string.IsNullOrEmpty(ExportFolderPath) && Directory.Exists(ExportFolderPath))
                System.Diagnostics.Process.Start("explorer.exe", ExportFolderPath);
        }

        [RelayCommand]
        private void ClearLog()
        {
            LogEntries.Clear();
            AddLog(new LogEntry(LogLevel.Info, "Log cleared."));
        }

        [RelayCommand]
        private void CopyAllLogs()
        {
            if (LogEntries.Count == 0) return;
            string text = string.Join(Environment.NewLine, LogEntries.Select(e => e.ToString()));
            System.Windows.Clipboard.SetText(text);
        }

        /// <summary>
        /// Called by the View's code-behind on window close, so settings capture
        /// the current UI state even if the user never clicked Run this session.
        /// </summary>
        public void SaveSettingsOnClose()
        {
            double.TryParse(SlopeInput, out double slope);
            double.TryParse(ConnectionThresholdInput, out double connThreshold);
            double.TryParse(ThresholdInput, out double threshold);
            int.TryParse(PathSampleCountInput, out int samples);

            SettingsService.Save(new AutoSlopeDrainSettings
            {
                SlopePercent = slope > 0 ? slope : AppConstants.DefaultSlopePercent,
                ConnectionThresholdMeters = connThreshold > 0 ? connThreshold : AppConstants.DefaultConnectionThresholdMeters,
                ThresholdMeters = threshold > 0 ? threshold : AppConstants.DefaultThresholdMeters,
                PathSampleCount = samples >= 2 ? samples : AppConstants.DefaultPathSampleCount,
                InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,
                VerifyElevationsAfterCommit = VerifyElevationsAfterCommit,
                ExportFolderPath = ExportFolderPath,
                SelectedSizeFilter = SelectedSizeFilter,
                DrainMarkerGroup = ToMarkerSettings(DrainMarkerGroup),
                HighestPointMarkerGroup = ToMarkerSettings(HighestPointMarkerGroup)
            });
        }

        // â”€â”€ AddLog (thread-safe) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void AddLog(LogEntry entry)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
                LogEntries.Add(entry);
            else
                dispatcher.BeginInvoke(new Action(() => LogEntries.Add(entry)));
        }
    }
}
