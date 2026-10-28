// =======================================================
// File: AutoSlopeDrainViewModel.cs
// Namespace: Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.UI.ViewModels
// Rewritten for V013 on Shared ToolViewModelBase: IsRunning / Progress /
// SummaryText, the log collection (2,000 cap) and the Copy / Clear log
// commands come from the base class. Layout follows AutoSlopeByPoint V032.
// Changes vs V012:
//   - "Every run" inputs (slope, max path distance) plus three option
//     sections that start closed: Path & curves, Circle markers, Export &
//     parameters. Each header shows a one-line summary and a "Custom" tag;
//     "Reset to defaults" per section; open/closed state remembered.
//   - Inline validation; Run is disabled with the reason in the status strip
//     (V012 checked only on Run and logged the error).
//   - Run snapshots the inputs (marker groups copied); inputs lock while
//     running. Run stays available after a completed run (V012 disabled it
//     for good after "Done").
//   - Jobs go through AutoSlopeDrainEventManager.Submit, which checks that
//     Revit accepted the request (V012 used a static payload and ignored the
//     result of Raise).
//   - Six tiles: Drains selected (of detected), Processed, Skipped, Highest,
//     Longest, Run time with start → end. Run time is the wall-clock time of
//     the batch with one decimal (V012 added up whole seconds per roof).
//     The detected total no longer changes after a run (V012 replaced it
//     with the total of the roofs that ran).
//   - Results side panel for the selected roof tab: Longest 10 (PathLength_m,
//     SlopePercent, ElevCalc_mm only) / Skipped, Copy rows, Zoom to.
//   - Roof tab switch selects and shows the roof through the ExternalEvent
//     (V012 called the API straight from the WPF event).
//   - Log closed by default, with filter chips, counts and Save log….
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Engine;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Models;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.UI.ViewModels
{
    public enum DrainRunState
    {
        Ready,
        Running,
        Done,
        Cancelled,
        Failed
    }

    public enum LogFilter
    {
        All,
        Warnings,
        Errors
    }

    public sealed partial class AutoSlopeDrainViewModel : ToolViewModelBase, IDisposable
    {
        private const string Dash = "—";
        private const int PumpIntervalMs = 100;

        private readonly UIApplication _app;
        private readonly Dispatcher _dispatcher;
        private readonly string _projectTitle;
        private CancellationTokenSource _cts;
        private DateTime _runStart;
        private DateTime _lastPump = DateTime.MinValue;
        private bool _loading = true;
        private bool _disposed;
        // Active-view badge listener; disposing it is safe on window close (see RevitEventRelay).
        private IDisposable _viewActivatedSubscription;

        public AutoSlopeDrainViewModel(UIDocument uidoc, UIApplication app, List<RoofData> roofDataList, IEnumerable<LogEntry> diagnostics)
        {
            UIDoc = uidoc;
            _app = app;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _projectTitle = uidoc?.Document?.Title ?? "Unknown project";

            LogView = CollectionViewSource.GetDefaultView(LogEntries);
            LogView.Filter = PassesLogFilter;
            LogEntries.CollectionChanged += OnLogChanged;

            foreach (LogEntry entry in diagnostics ?? Enumerable.Empty<LogEntry>())
                AddLog(entry.Level, entry.Message);

            int tabNumber = 1;
            var defaultSelectionLogs = new List<string>();
            foreach (var roofData in roofDataList)
            {
                var tab = new RoofTabViewModel(roofData, tabNumber++);
                tab.SelectionChanged += OnAnyDrainSelectionChanged;
                RoofTabs.Add(tab);
                if (!string.IsNullOrEmpty(tab.DefaultSelectionSummary))
                    defaultSelectionLogs.Add(roofDataList.Count == 1 ? tab.DefaultSelectionSummary : $"[Roof {tab.TabNumber}] {tab.DefaultSelectionSummary}");
            }
            TotalDetectedCount = RoofTabs.Sum(t => t.TotalDetectedCount);

            DrainMarkerGroup = new CircleMarkerGroup { GroupLabel = "Drains" };
            HighestPointMarkerGroup = new CircleMarkerGroup { GroupLabel = "Highest point" };
            AllowedOffsetMarkerGroup = new CircleMarkerGroup { GroupLabel = "Allowed offset" };
            foreach (var g in MarkerGroups) g.PropertyChanged += OnMarkerGroupChanged;

            LoadLineStyleOptions();
            LoadSettings();

            AddLog(LogLevel.Info, RoofTabs.Count == 1
                ? $"Detected {TotalDetectedCount} drain opening(s)."
                : $"{RoofTabs.Count} roofs picked · detected {TotalDetectedCount} drain opening(s) in total.");
            foreach (var line in defaultSelectionLogs) AddLog(LogLevel.Info, line);
            AddLog(LogLevel.Info, "Nothing in the model has changed yet. Each roof is reset to 0 when you press Run.");

            UpdateViewBadge(_app.ActiveUIDocument?.ActiveView);
            _viewActivatedSubscription = RevitEventRelay.ViewActivated(_app, GetType(), OnViewActivated);

            AutoSlopeDrainEventManager.Init();
            _loading = false;

            SelectedRoofTab = RoofTabs.FirstOrDefault();
            ResetTiles();
            Revalidate();
            UpdateLogSummary();
        }

        public UIDocument UIDoc { get; }

        // ══ Header ═══════════════════════════════════════════════════════════

        public string ToolTitle => "AutoSlope By Drain";
        public string VersionText => "v" + AutoSlopeDrainEngine.ToolVersion;
        public string SubtitleText => "Revit 2026 · slopes each roof towards its selected drain openings";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateText))]
        [NotifyPropertyChangedFor(nameof(RunButtonText))]
        [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
        [NotifyPropertyChangedFor(nameof(IsCancelVisible))]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
        [NotifyCanExecuteChangedFor(nameof(BrowseFolderCommand))]
        [NotifyCanExecuteChangedFor(nameof(ZoomToRowsCommand))]
        private DrainRunState state = DrainRunState.Ready;

        [ObservableProperty] private int appliedRoofCount;

        public string StateText => State switch
        {
            DrainRunState.Running => "Running",
            DrainRunState.Done => "Completed",
            DrainRunState.Cancelled => $"Cancelled · {AppliedRoofCount} roof(s) applied",
            DrainRunState.Failed => "Failed · roofs unchanged",
            _ => "Ready"
        };

        public string RunButtonText => State switch
        {
            DrainRunState.Running => "Running…",
            DrainRunState.Ready => "Run AutoSlope",
            _ => "Run again"
        };

        public bool IsInputEnabled => State != DrainRunState.Running;
        public bool IsCancelVisible => State == DrainRunState.Running;

        /// <summary>Bound to the shell; true only while no percentage is known.</summary>
        [ObservableProperty] private bool isIndeterminate;

        [ObservableProperty] private bool isPlanView;
        [ObservableProperty] private string viewBadgeText;

        public string RoofsBadgeText
        {
            get
            {
                int runnable = RoofTabs.Count(t => t.SelectedDrainsCount > 0);
                return RoofTabs.Count == 1
                    ? RoofTabs[0].RoofSubtitle
                    : $"{RoofTabs.Count} roofs picked · {runnable} will run";
            }
        }

        // ── Tiles ──────────────────────────────────────────────────────────
        public int TotalDetectedCount { get; }
        public int AllSelectedDrainsCount => RoofTabs.Sum(t => t.SelectedDrainsCount);
        public string DrainsSubText => RoofTabs.Count == 1 ? $"of {TotalDetectedCount} detected" : $"of {TotalDetectedCount} detected · {RoofTabs.Count} roofs";

        [ObservableProperty] private string processedText = Dash;
        [ObservableProperty] private string skippedText = Dash;
        [ObservableProperty] private string highestText = Dash;
        [ObservableProperty] private string longestPathText = Dash;
        [ObservableProperty] private string runTimeText = Dash;
        [ObservableProperty] private string runTimeSubText = "Start → End";

        // ══ Roofs ════════════════════════════════════════════════════════════

        public ObservableCollection<RoofTabViewModel> RoofTabs { get; } = new ObservableCollection<RoofTabViewModel>();

        [ObservableProperty]
        private RoofTabViewModel selectedRoofTab;

        partial void OnSelectedRoofTabChanged(RoofTabViewModel value)
        {
            SetSelectedResultRows(null);
            NotifyResultsChanged();
            if (value == null || _loading || State == DrainRunState.Running) return;

            // Select the roof and show it, through the ExternalEvent (API context).
            if (AutoSlopeDrainEventManager.Handler?.HasPending != true)
                AutoSlopeDrainEventManager.Submit(new FocusRoofJob(value.RoofIdValue));
        }

        private void OnAnyDrainSelectionChanged()
        {
            OnPropertyChanged(nameof(AllSelectedDrainsCount));
            OnPropertyChanged(nameof(RoofsBadgeText));
            if (!_loading) Revalidate();
        }

        // ══ Inputs ═══════════════════════════════════════════════════════════

        public IReadOnlyList<string> SlopeOptions { get; } = new List<string> { "1.0", "1.5", "2.0", "2.5", "3.0" };

        [ObservableProperty] private string slopeText;
        [ObservableProperty] private string slopeError;
        [ObservableProperty] private string slopeHint;

        /// <summary>Max path distance (ThresholdMeters): longer total paths stay at 0.</summary>
        [ObservableProperty] private string maxPathText;
        [ObservableProperty] private string maxPathError;

        /// <summary>Max edge distance (ConnectionThresholdMeters): longer single links are never made.</summary>
        [ObservableProperty] private string maxEdgeText;
        [ObservableProperty] private string maxEdgeError;

        [ObservableProperty] private string pathSamplesText;
        [ObservableProperty] private string pathSamplesError;

        [ObservableProperty] private bool insertCurveIntersectionPoints;
        [ObservableProperty] private bool verifyElevationsAfterCommit;

        // Circle markers
        public CircleMarkerGroup DrainMarkerGroup { get; }
        public CircleMarkerGroup HighestPointMarkerGroup { get; }
        public CircleMarkerGroup AllowedOffsetMarkerGroup { get; }
        private IEnumerable<CircleMarkerGroup> MarkerGroups => new[] { DrainMarkerGroup, HighestPointMarkerGroup, AllowedOffsetMarkerGroup };

        [ObservableProperty] private string allowedOffsetThresholdText;
        [ObservableProperty] private string markersError;

        public IReadOnlyList<string> ColorPalette { get; } = NamedColorHelper.PaletteNames;
        public ObservableCollection<LineStyleOption> LineStyleOptions { get; } = new ObservableCollection<LineStyleOption>();
        private LineStyleOption _defaultLineStyle;

        // Export
        [ObservableProperty] private string exportFolderPath;
        [ObservableProperty] private bool writeCombinedSummary;
        [ObservableProperty] private bool askToOpenAfterExport;

        // ── Option sections (closed by default, state remembered) ────────
        [ObservableProperty] private bool pathOptionsExpanded;
        [ObservableProperty] private bool markersExpanded;
        [ObservableProperty] private bool exportExpanded;

        [ObservableProperty] private string pathOptionsSummary;
        [ObservableProperty] private bool isPathOptionsCustom;
        [ObservableProperty] private string markersSummary;
        [ObservableProperty] private bool isMarkersCustom;
        [ObservableProperty] private string exportSummary;
        [ObservableProperty] private bool isExportCustom;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private string validationMessage;

        partial void OnSlopeTextChanged(string value) => Revalidate();
        partial void OnMaxPathTextChanged(string value) => Revalidate();
        partial void OnMaxEdgeTextChanged(string value) => Revalidate();
        partial void OnPathSamplesTextChanged(string value) => Revalidate();
        partial void OnInsertCurveIntersectionPointsChanged(bool value) => Revalidate();
        partial void OnVerifyElevationsAfterCommitChanged(bool value) => Revalidate();
        partial void OnAllowedOffsetThresholdTextChanged(string value) => Revalidate();
        partial void OnExportFolderPathChanged(string value) => Revalidate();
        partial void OnWriteCombinedSummaryChanged(bool value) => Revalidate();
        partial void OnAskToOpenAfterExportChanged(bool value) => Revalidate();

        private void OnMarkerGroupChanged(object sender, PropertyChangedEventArgs e) => Revalidate();

        private static bool TryParse(string text, out double value)
        {
            text = (text ?? string.Empty).Trim().TrimEnd('%').Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Format(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string CheckRange(string text, double min, double max, string label, string unit, out double value)
        {
            if (!TryParse(text, out value) || double.IsNaN(value))
                return $"{label}: enter a number.";
            if (value < min || value > max)
                return $"{label} must be between {Format(min)} and {Format(max)} {unit}.";
            return null;
        }

        private static bool Same(string text, double expected) => TryParse(text, out double v) && Math.Abs(v - expected) < 1e-9;

        private void Revalidate()
        {
            if (_loading) return;

            var errors = new List<string>();

            SlopeError = CheckRange(SlopeText, AppConstants.SlopeMin, AppConstants.SlopeMax, "Slope", "%", out double slope);
            SlopeHint = SlopeError == null ? $"≈ {slope * 100:0} mm rise per 10 m" : string.Empty;
            if (SlopeError != null) errors.Add(SlopeError);

            MaxPathError = CheckRange(MaxPathText, AppConstants.DistanceMinM, AppConstants.DistanceMaxM, "Max path distance", "m", out _);
            if (MaxPathError != null) errors.Add(MaxPathError);

            MaxEdgeError = CheckRange(MaxEdgeText, AppConstants.DistanceMinM, AppConstants.DistanceMaxM, "Max edge distance", "m", out _);
            if (MaxEdgeError != null) errors.Add(MaxEdgeError);

            PathSamplesError = CheckRange(PathSamplesText, AppConstants.PathSamplesMin, AppConstants.PathSamplesMax, "Path samples", "", out double samples);
            if (PathSamplesError == null && Math.Abs(samples - Math.Round(samples)) > 1e-9)
                PathSamplesError = "Path samples must be a whole number.";
            if (PathSamplesError != null) errors.Add(PathSamplesError);

            MarkersError = null;
            foreach (var g in MarkerGroups.Where(g => g.IsEnabled))
                if (g.RadiusMm < AppConstants.MarkerRadiusMinMm || g.RadiusMm > AppConstants.MarkerRadiusMaxMm)
                    MarkersError ??= $"{g.GroupLabel} circle radius must be between {Format(AppConstants.MarkerRadiusMinMm)} and {Format(AppConstants.MarkerRadiusMaxMm)} mm.";
            if (AllowedOffsetMarkerGroup.IsEnabled)
            {
                string thr = CheckRange(AllowedOffsetThresholdText, 1, 100000, "Allowed offset threshold", "mm", out _);
                MarkersError ??= thr;
            }
            if (MarkersError != null) errors.Add(MarkersError);

            if (string.IsNullOrWhiteSpace(ExportFolderPath))
                errors.Add("Choose an export folder.");

            if (AllSelectedDrainsCount == 0)
                errors.Add("Tick at least one drain on any roof.");

            ValidationMessage = errors.FirstOrDefault();
            UpdateSectionSummaries();
            UpdateIdleSummary();
        }

        private string DefaultExportFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppConstants.DefaultExportFolder);

        private bool MarkerAtDefaults(CircleMarkerGroup g, string color) =>
            g.IsEnabled && g.ColorName == color && Math.Abs(g.RadiusMm - AppConstants.DefaultMarkerRadiusMm) < 1e-9
            && g.ShowOffsetText && (_defaultLineStyle == null || g.LineStyleName == _defaultLineStyle.Name);

        private void UpdateSectionSummaries()
        {
            PathOptionsSummary = $"Edge {MaxEdgeText} m · {PathSamplesText} samples · arcs {(InsertCurveIntersectionPoints ? "on" : "off")} · read-back {(VerifyElevationsAfterCommit ? "on" : "off")}";
            IsPathOptionsCustom = !Same(MaxEdgeText, AppConstants.DefaultConnectionThresholdMeters)
                || !Same(PathSamplesText, AppConstants.DefaultPathSampleCount)
                || InsertCurveIntersectionPoints || !VerifyElevationsAfterCommit;

            var on = new List<string>();
            if (DrainMarkerGroup.IsEnabled) on.Add("Drains");
            if (HighestPointMarkerGroup.IsEnabled) on.Add("Highest point");
            if (AllowedOffsetMarkerGroup.IsEnabled) on.Add($"Offset ≥ {AllowedOffsetThresholdText} mm");
            MarkersSummary = (on.Count == 0 ? "Off" : string.Join(" · ", on)) + " · plan view only";
            IsMarkersCustom = !MarkerAtDefaults(DrainMarkerGroup, AppConstants.DefaultDrainColor)
                || !MarkerAtDefaults(HighestPointMarkerGroup, AppConstants.DefaultHighestColor)
                || !MarkerAtDefaults(AllowedOffsetMarkerGroup, AppConstants.DefaultOffsetColor)
                || !Same(AllowedOffsetThresholdText, AppConstants.DefaultAllowedOffsetMm);

            string folderName = string.IsNullOrWhiteSpace(ExportFolderPath) ? "(no folder)" : Path.GetFileName(ExportFolderPath.TrimEnd('\\', '/'));
            ExportSummary = $"Excel · …\\{folderName}{(WriteCombinedSummary ? " · combined" : string.Empty)}{(AskToOpenAfterExport ? " · ask to open" : string.Empty)}";
            IsExportCustom = !WriteCombinedSummary || !AskToOpenAfterExport
                || !string.Equals(ExportFolderPath, DefaultExportFolder, StringComparison.OrdinalIgnoreCase);
        }

        [RelayCommand]
        private void ResetPathOptions()
        {
            MaxEdgeText = Format(AppConstants.DefaultConnectionThresholdMeters);
            PathSamplesText = AppConstants.DefaultPathSampleCount.ToString(CultureInfo.InvariantCulture);
            InsertCurveIntersectionPoints = false;
            VerifyElevationsAfterCommit = true;
        }

        [RelayCommand]
        private void ResetMarkers()
        {
            ResetMarker(DrainMarkerGroup, AppConstants.DefaultDrainColor);
            ResetMarker(HighestPointMarkerGroup, AppConstants.DefaultHighestColor);
            ResetMarker(AllowedOffsetMarkerGroup, AppConstants.DefaultOffsetColor);
            AllowedOffsetThresholdText = Format(AppConstants.DefaultAllowedOffsetMm);
        }

        private void ResetMarker(CircleMarkerGroup g, string color)
        {
            g.IsEnabled = true;
            g.ColorName = color;
            g.RadiusMm = AppConstants.DefaultMarkerRadiusMm;
            g.ShowOffsetText = true;
            if (_defaultLineStyle != null)
            {
                g.LineStyleId = _defaultLineStyle.Id;
                g.LineStyleName = _defaultLineStyle.Name;
            }
        }

        [RelayCommand]
        private void ResetExport()
        {
            ExportFolderPath = DefaultExportFolder;
            WriteCombinedSummary = true;
            AskToOpenAfterExport = true;
        }

        private void LoadLineStyleOptions()
        {
            LineStyleOptions.Clear();
            Document doc = UIDoc?.Document;
            Category lines = doc?.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines?.SubCategories != null)
                foreach (Category sub in lines.SubCategories)
                    if (sub?.GetGraphicsStyle(GraphicsStyleType.Projection) is GraphicsStyle gs)
                        LineStyleOptions.Add(new LineStyleOption { Id = gs.Id, Name = sub.Name });

            _defaultLineStyle = LineStyleOptions.FirstOrDefault(o => o.Name == "Thin Lines") ?? LineStyleOptions.FirstOrDefault();
            if (_defaultLineStyle == null)
                AddLog(LogLevel.Warning, "This project has no line styles, so circle markers use the default line style.");
        }

        // When a marker's line style is picked in the combo, keep its name for settings.
        public void SyncLineStyleNames()
        {
            foreach (var g in MarkerGroups)
                g.LineStyleName = LineStyleOptions.FirstOrDefault(o => o.Id == g.LineStyleId)?.Name ?? g.LineStyleName;
        }

        // ══ Run ══════════════════════════════════════════════════════════════

        // HasPending is checked in Run, not here: a queued tab-focus or zoom job
        // finishes on its own, and nothing would re-query CanExecute afterwards.
        private bool CanRun() => IsInputEnabled && string.IsNullOrEmpty(ValidationMessage);

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            Revalidate();
            if (!string.IsNullOrEmpty(ValidationMessage)) return;
            if (AutoSlopeDrainEventManager.Handler?.HasPending == true)
            {
                AddLog(LogLevel.Warning, "Revit is still busy with the previous request. Try Run again in a moment.");
                return;
            }

            SyncLineStyleNames();
            TryParse(SlopeText, out double slope);
            TryParse(MaxPathText, out double maxPath);
            TryParse(MaxEdgeText, out double maxEdge);
            TryParse(PathSamplesText, out double samplesD);
            TryParse(AllowedOffsetThresholdText, out double offsetThreshold);
            int samples = (int)Math.Round(samplesD);

            var runnable = RoofTabs.Where(t => t.SelectedDrainsCount > 0).ToList();

            LogEntries.Clear();
            _cts = new CancellationTokenSource();
            _runStart = DateTime.Now;
            _lastPump = DateTime.MinValue;
            AppliedRoofCount = 0;

            State = DrainRunState.Running;
            IsRunning = true;
            IsIndeterminate = true;
            Progress = 0;
            SummaryText = $"Started {_runStart:HH:mm:ss} · waiting for Revit…";
            ResetTiles();
            RunTimeText = "Running";
            RunTimeSubText = $"Started {_runStart:HH:mm:ss}";
            SetSelectedResultRows(null);

            AddLog(LogLevel.Info, $"Starting AutoSlope By Drain on {runnable.Count} of {RoofTabs.Count} roof(s) at {_runStart:HH:mm:ss}. " +
                                  $"Slope {Format(slope)} %, max path distance {Format(maxPath)} m.");
            foreach (var tab in RoofTabs)
            {
                bool willRun = runnable.Contains(tab);
                tab.PrepareForRun(willRun);
                if (!willRun) AddLog(LogLevel.Warning, $"[Roof {tab.TabNumber}] Skipped: no drains checked.");
            }
            NotifyResultsChanged();

            if (!Directory.Exists(ExportFolderPath))
            {
                try { Directory.CreateDirectory(ExportFolderPath); }
                catch (Exception ex) { AddLog(LogLevel.Warning, $"The export folder could not be created: {ex.Message}"); }
            }

            // Snapshots: the run never reads the window's live objects.
            CircleMarkerGroup drainMarkers = DrainMarkerGroup.Snapshot();
            CircleMarkerGroup highestMarkers = HighestPointMarkerGroup.Snapshot();
            CircleMarkerGroup offsetMarkers = AllowedOffsetMarkerGroup.Snapshot();

            // One continuous bar for the batch, weighted by each roof's point count.
            double totalWeight = runnable.Sum(t => (double)Math.Max(1, t.RoofData.Vertices.Count));
            double weightDone = 0;
            var payloads = new List<AutoSlopeDrainPayload>();

            foreach (var tab in runnable)
            {
                RoofTabViewModel t = tab;
                double weight = Math.Max(1, t.RoofData.Vertices.Count);
                double before = weightDone;
                weightDone += weight;
                bool started = false;

                payloads.Add(new AutoSlopeDrainPayload
                {
                    RoofId = t.RoofId,
                    RoofName = t.RoofName,
                    SelectedDrains = t.GetSelectedDrains(),
                    TotalDetectedCount = t.TotalDetectedCount,
                    SlopePercent = slope,
                    ConnectionThresholdMeters = maxEdge,
                    ThresholdMeters = maxPath,
                    PathSampleCount = samples,
                    InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,
                    VerifyElevationsAfterCommit = VerifyElevationsAfterCommit,
                    DrainMarkerGroup = drainMarkers,
                    HighestPointMarkerGroup = highestMarkers,
                    AllowedOffsetMarkerGroup = offsetMarkers,
                    AllowedOffsetThresholdMm = offsetThreshold,
                    ProjectTitle = _projectTitle,
                    ExportConfig = new ExportConfig { ExportPath = ExportFolderPath, ExportToExcel = true },
                    CancelToken = _cts.Token,
                    Log = entry => AddLog(entry.Level, $"[Roof {t.TabNumber}] {entry.Message}"),
                    // Called synchronously on Revit's thread (the window's thread too).
                    Progress = info =>
                    {
                        if (!started) { started = true; t.MarkRunning(); }
                        OnRunProgress(info, t, runnable.Count, before, weight, totalWeight);
                    },
                    OnCompleted = result =>
                    {
                        t.ApplyRunResult(result);
                        if (result.Success) AppliedRoofCount++;
                        if (ReferenceEquals(t, SelectedRoofTab)) NotifyResultsChanged();
                    }
                });
            }

            var batch = new AutoSlopeDrainMultiPayload
            {
                RoofPayloads = payloads,
                WriteCombinedSummary = WriteCombinedSummary,
                ExportFolderPath = ExportFolderPath,
                ProjectTitle = _projectTitle,
                Log = entry => AddLog(entry.Level, entry.Message),
                OnAllCompleted = FinishRun
            };

            if (!AutoSlopeDrainEventManager.Submit(new DrainRunJob(batch)))
            {
                AddLog(LogLevel.Error, "Revit did not accept the request (it may be busy). Try Run again.");
                FinishRun(new List<AutoSlopeDrainRoofResult>());
            }
        }

        private void OnRunProgress(RunProgressInfo info, RoofTabViewModel tab, int roofCount,
            double weightBefore, double weight, double totalWeight)
        {
            string prefix = roofCount > 1 ? $"Roof {tab.TabNumber} — " : string.Empty;
            if (info.PercentWithinPhase.HasValue)
            {
                IsIndeterminate = false;
                double overall = (weightBefore + weight * (info.PercentWithinPhase.Value / 100.0)) / totalWeight * 100.0;
                Progress = Math.Max(Progress, Math.Min(100.0, overall));
            }
            else
            {
                IsIndeterminate = Progress <= 0;
            }

            string within = info.PercentWithinPhase.HasValue ? $" · {info.PercentWithinPhase.Value:0} %" : "…";
            SummaryText = _cts?.IsCancellationRequested == true
                ? "Cancelling…"
                : $"Started {_runStart:HH:mm:ss} · {prefix}{info.PhaseLabel}{within}";

            if ((DateTime.Now - _lastPump).TotalMilliseconds >= PumpIntervalMs)
            {
                _lastPump = DateTime.Now;
                UiPumpHelper.DoEvents(); // repaint + let a Cancel click through
            }
        }

        private void FinishRun(List<AutoSlopeDrainRoofResult> roofResults)
        {
            bool cancelled = _cts?.IsCancellationRequested == true || roofResults.Any(r => r.Result?.WasCancelled == true);
            _cts?.Dispose();
            _cts = null;

            var end = DateTime.Now;
            double seconds = (end - _runStart).TotalSeconds;

            foreach (var tab in RoofTabs) tab.FinishBatch();

            var succeeded = roofResults.Where(r => r.Result?.Success == true).Select(r => r.Result).ToList();
            AppliedRoofCount = succeeded.Count;

            IsRunning = false;
            IsIndeterminate = false;
            Progress = 0;

            if (succeeded.Count > 0)
            {
                ProcessedText = succeeded.Sum(r => r.VerticesProcessed).ToString("N0");
                SkippedText = succeeded.Sum(r => r.VerticesSkipped).ToString("N0");
                HighestText = $"{succeeded.Max(r => r.HighestElevation_mm):0} mm";
                LongestPathText = $"{succeeded.Max(r => r.LongestPath_m):0.00} m";
            }
            else
            {
                ResetTiles();
            }
            RunTimeText = $"{seconds:0.0} s";
            RunTimeSubText = $"{_runStart:HH:mm:ss} → {end:HH:mm:ss}";

            int failed = roofResults.Count(r => r.Result != null && !r.Result.Success && !r.Result.WasCancelled);
            if (cancelled)
            {
                State = DrainRunState.Cancelled;
                SummaryText = $"Cancelled at {end:HH:mm:ss} (started {_runStart:HH:mm:ss}) · {succeeded.Count} roof(s) applied, the roof in progress rolled back";
                AddLog(LogLevel.Warning, $"Cancelled. {succeeded.Count} roof(s) had already finished and stay applied; the rest were not started.");
            }
            else if (succeeded.Count == 0)
            {
                State = DrainRunState.Failed;
                SummaryText = roofResults.Count == 0
                    ? "Not run · Revit did not accept the request"
                    : $"Failed on every roof · nothing was changed in the model · see the log";
            }
            else
            {
                State = DrainRunState.Done;
                SummaryText = $"Done · {succeeded.Count} roof(s) · {ProcessedText} processed, {SkippedText} skipped · " +
                              $"Start {_runStart:HH:mm:ss} · End {end:HH:mm:ss} · Total {seconds:0.0} s" +
                              (failed > 0 ? $" · {failed} roof(s) failed and were rolled back" : string.Empty);
                AddLog(LogLevel.Success, $"All roofs finished. Start {_runStart:HH:mm:ss} · End {end:HH:mm:ss} · Total {seconds:0.0} s.");
            }

            OnPropertyChanged(nameof(StateText));
            NotifyResultsChanged();
            RunCommand.NotifyCanExecuteChanged();

            if (succeeded.Count > 0)
            {
                SaveSettings();
                if (AskToOpenAfterExport && succeeded.Any(r => !string.IsNullOrEmpty(r.ExportedFilePath)))
                    _dispatcher.BeginInvoke(new Action(OfferToOpenFolder));
            }
        }

        private void OfferToOpenFolder()
        {
            var answer = MessageBox.Show("Excel export finished. Open the export folder now?", ToolTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) OpenExportFolder();
        }

        private bool CanCancelRun() => State == DrainRunState.Running;

        [RelayCommand(CanExecute = nameof(CanCancelRun))]
        private void CancelRun()
        {
            if (_cts == null || _cts.IsCancellationRequested) return;
            _cts.Cancel();
            SummaryText = "Cancelling…";
            AddLog(LogLevel.Warning, "Cancel requested. The roof in progress stops at the next check and is rolled back; finished roofs stay applied.");
        }

        // ══ Export folder ════════════════════════════════════════════════════

        private bool CanBrowseFolder() => IsInputEnabled;

        [RelayCommand(CanExecute = nameof(CanBrowseFolder))]
        private void BrowseFolder()
        {
            string selected = DialogService.SelectFolder(ExportFolderPath);
            if (string.IsNullOrEmpty(selected)) return;
            ExportFolderPath = selected;
            AddLog(LogLevel.Info, $"Export folder: {selected}");
        }

        [RelayCommand]
        private void OpenExportFolder()
        {
            try
            {
                if (!string.IsNullOrEmpty(ExportFolderPath) && Directory.Exists(ExportFolderPath))
                    System.Diagnostics.Process.Start("explorer.exe", ExportFolderPath);
                else
                    AddLog(LogLevel.Warning, "The export folder does not exist yet. It is created on the first run.");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"Could not open the export folder: {ex.Message}");
            }
        }

        // ══ Results side panel ═══════════════════════════════════════════════

        private const int ResultRowCount = 10;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsLongestTab))]
        [NotifyPropertyChangedFor(nameof(IsSkippedTab))]
        [NotifyPropertyChangedFor(nameof(ResultsTitle))]
        [NotifyPropertyChangedFor(nameof(ShowResultsEmpty))]
        private ResultsTab activeResultsTab = ResultsTab.Longest;

        public bool IsLongestTab => ActiveResultsTab == ResultsTab.Longest;
        public bool IsSkippedTab => ActiveResultsTab == ResultsTab.Skipped;

        public string ResultsForText => SelectedRoofTab == null ? string.Empty : $"· Roof {SelectedRoofTab.TabNumber}";

        public string SkippedTabText => SelectedRoofTab?.HasResults == true ? $"Skipped ({SelectedRoofTab.SkippedTotal})" : "Skipped";

        public string ResultsTitle => IsLongestTab
            ? "Longest 10 paths"
            : (SelectedRoofTab?.SkippedTotal ?? 0) > ResultRowCount
                ? $"Skipped points (first {ResultRowCount} of {SelectedRoofTab.SkippedTotal})"
                : "Skipped points";

        public bool ShowResultsEmpty => SelectedRoofTab == null
            || (IsLongestTab ? SelectedRoofTab.LongestRows.Count == 0 : SelectedRoofTab.SkippedRows.Count == 0);

        public string ResultsEmptyText => SelectedRoofTab == null
            ? string.Empty
            : SelectedRoofTab.HasResults && IsSkippedTab ? "No points were skipped on this roof." : SelectedRoofTab.ResultsEmptyText;

        /// <summary>Set by the window from the visible grid's selection.</summary>
        public IList<DrainResultRow> SelectedResultRows { get; private set; } = new List<DrainResultRow>();

        [ObservableProperty] private string resultsHint = "Path length, slope and calculated elevation of the 10 longest paths.";

        public void SetSelectedResultRows(IList<DrainResultRow> rows)
        {
            SelectedResultRows = rows ?? new List<DrainResultRow>();
            ResultsHint = SelectedResultRows.Count > 0
                ? $"{SelectedResultRows.Count} row(s) selected · Ctrl+click adds rows"
                : SelectedRoofTab?.HasResults == true
                    ? "Select rows, then Zoom to. Copy rows copies the selection, or the whole tab."
                    : "Path length, slope and calculated elevation of the 10 longest paths.";
            ZoomToRowsCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SetResultsTab(string tab)
        {
            if (Enum.TryParse(tab, out ResultsTab t)) ActiveResultsTab = t;
            SetSelectedResultRows(null);
            OnPropertyChanged(nameof(ResultsEmptyText));
        }

        private void NotifyResultsChanged()
        {
            OnPropertyChanged(nameof(ResultsForText));
            OnPropertyChanged(nameof(SkippedTabText));
            OnPropertyChanged(nameof(ResultsTitle));
            OnPropertyChanged(nameof(ShowResultsEmpty));
            OnPropertyChanged(nameof(ResultsEmptyText));
            CopyResultRowsCommand.NotifyCanExecuteChanged();
            ZoomToRowsCommand.NotifyCanExecuteChanged();
            SetSelectedResultRows(SelectedResultRows);
        }

        private bool CanZoomToRows() =>
            State != DrainRunState.Running && SelectedResultRows.Count > 0 && SelectedRoofTab?.HasResults == true;

        [RelayCommand(CanExecute = nameof(CanZoomToRows))]
        private void ZoomToRows()
        {
            var points = SelectedResultRows.Where(r => r.Position != null).Select(r => r.Position).ToList();
            if (!AutoSlopeDrainEventManager.Submit(new ZoomJob(SelectedRoofTab.RoofIdValue, points, (l, m) => AddLog(l, m))))
                AddLog(LogLevel.Warning, "Revit did not accept the request (it may be busy). Try again.");
        }

        private bool CanCopyResultRows() => SelectedRoofTab?.HasResults == true;

        [RelayCommand(CanExecute = nameof(CanCopyResultRows))]
        private void CopyResultRows()
        {
            var source = IsLongestTab ? SelectedRoofTab.LongestRows : SelectedRoofTab.SkippedRows;
            var rows = SelectedResultRows.Count > 0 ? SelectedResultRows.OrderBy(r => r.RowNumber).ToList() : source.ToList();
            if (rows.Count == 0) return;

            string text = DrainResultRow.Header(ActiveResultsTab) + Environment.NewLine
                + string.Join(Environment.NewLine, rows.Select(r => r.ToTsv(ActiveResultsTab)));
            try
            {
                Clipboard.SetText(text);
                AddLog(LogLevel.Info, $"Copied {rows.Count} row(s) from Roof {SelectedRoofTab.TabNumber}. Paste into Excel to get the same columns.");
            }
            catch
            {
                AddLog(LogLevel.Warning, "The clipboard is busy. Try Copy rows again.");
            }
        }

        // ══ Log ══════════════════════════════════════════════════════════════

        public ICollectionView LogView { get; }

        [ObservableProperty] private LogFilter activeLogFilter = LogFilter.All;
        [ObservableProperty] private string logSummaryText;
        [ObservableProperty] private string latestLogText;
        [ObservableProperty] private int warningCount;
        [ObservableProperty] private int errorCount;

        [RelayCommand]
        private void SetLogFilter(string filter)
        {
            if (Enum.TryParse(filter, out LogFilter f))
            {
                ActiveLogFilter = f;
                LogView.Refresh();
            }
        }

        private bool PassesLogFilter(object item)
        {
            if (item is not LogEntry e) return false;
            return ActiveLogFilter switch
            {
                LogFilter.Warnings => e.Level == LogLevel.Warning,
                LogFilter.Errors => e.Level == LogLevel.Error,
                _ => true
            };
        }

        private void OnLogChanged(object sender, NotifyCollectionChangedEventArgs e) => UpdateLogSummary();

        private void UpdateLogSummary()
        {
            int n = LogEntries.Count;
            WarningCount = LogEntries.Count(x => x.Level == LogLevel.Warning);
            ErrorCount = LogEntries.Count(x => x.Level == LogLevel.Error);

            string text = n == 1 ? "1 line" : $"{n} lines";
            if (WarningCount > 0) text += WarningCount == 1 ? " · 1 warning" : $" · {WarningCount} warnings";
            if (ErrorCount > 0) text += ErrorCount == 1 ? " · 1 error" : $" · {ErrorCount} errors";
            LogSummaryText = text;

            LogEntry last = n > 0 ? LogEntries[n - 1] : null;
            LatestLogText = last == null ? "No entries yet." : $"[{last.Time:HH:mm:ss}] {last.Message}";
        }

        [RelayCommand]
        private void SaveLog()
        {
            try
            {
                string folder = string.IsNullOrWhiteSpace(ExportFolderPath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    : ExportFolderPath;
                Directory.CreateDirectory(folder);
                string path = LogExportService.Export("AutoSlopeByDrain", LogEntries, folder);
                AddLog(LogLevel.Success, $"Log saved: {path}");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Could not save the log: {ex.Message}");
            }
        }

        // ══ Tiles / summary helpers ══════════════════════════════════════════

        private void ResetTiles()
        {
            ProcessedText = Dash;
            SkippedText = Dash;
            HighestText = Dash;
            LongestPathText = Dash;
            RunTimeText = Dash;
            RunTimeSubText = "Start → End";
        }

        private void UpdateIdleSummary()
        {
            if (State == DrainRunState.Running) return;

            if (!string.IsNullOrEmpty(ValidationMessage))
                SummaryText = "Can't run yet · " + ValidationMessage;
            else if (State == DrainRunState.Ready)
            {
                int runnable = RoofTabs.Count(t => t.SelectedDrainsCount > 0);
                SummaryText = $"Ready · {AllSelectedDrainsCount} drain(s) on {runnable} roof(s) · each roof is reset to 0 when you press Run";
            }
        }

        // ══ Active view badge ════════════════════════════════════════════════

        private void OnViewActivated(object sender, ViewActivatedEventArgs e) => UpdateViewBadge(e.CurrentActiveView);

        private void UpdateViewBadge(View view)
        {
            IsPlanView = view is ViewPlan;
            string name = view?.Name ?? "no view";
            ViewBadgeText = IsPlanView
                ? $"Plan view · {name}"
                : $"Active view is not a plan ({name}) · circle markers will be skipped";
        }

        // ══ Settings ═════════════════════════════════════════════════════════

        private void LoadSettings()
        {
            AutoSlopeDrainSettings s = DrainSettingsStore.Load(msg => AddLog(LogLevel.Warning, msg));

            SlopeText = Format(s.SlopePercent);
            MaxPathText = Format(s.ThresholdMeters);
            MaxEdgeText = Format(s.ConnectionThresholdMeters);
            PathSamplesText = s.PathSampleCount.ToString(CultureInfo.InvariantCulture);
            InsertCurveIntersectionPoints = s.InsertCurveIntersectionPoints;
            VerifyElevationsAfterCommit = s.VerifyElevationsAfterCommit;

            ApplyMarker(DrainMarkerGroup, s.DrainMarkerGroup, AppConstants.DefaultDrainColor);
            ApplyMarker(HighestPointMarkerGroup, s.HighestPointMarkerGroup, AppConstants.DefaultHighestColor);
            ApplyMarker(AllowedOffsetMarkerGroup, s.AllowedOffsetMarkerGroup, AppConstants.DefaultOffsetColor);
            AllowedOffsetThresholdText = Format(s.AllowedOffsetThresholdMm > 0 ? s.AllowedOffsetThresholdMm : AppConstants.DefaultAllowedOffsetMm);

            ExportFolderPath = string.IsNullOrWhiteSpace(s.ExportFolderPath) ? DefaultExportFolder : s.ExportFolderPath;
            WriteCombinedSummary = s.WriteCombinedSummary;
            AskToOpenAfterExport = s.AskToOpenAfterExport;

            string savedFilter = string.IsNullOrEmpty(s.SelectedSizeFilter) ? "All" : s.SelectedSizeFilter;
            foreach (var tab in RoofTabs)
                if (tab.SizeFilters.Contains(savedFilter))
                    tab.SelectedSizeFilter = savedFilter;

            PathOptionsExpanded = s.PathOptionsExpanded;
            MarkersExpanded = s.MarkersExpanded;
            ExportExpanded = s.ExportExpanded;
        }

        private void ApplyMarker(CircleMarkerGroup g, CircleMarkerGroupSettings saved, string defaultColor)
        {
            g.IsEnabled = saved?.IsEnabled ?? true;
            g.ColorName = string.IsNullOrWhiteSpace(saved?.ColorName) ? defaultColor : saved.ColorName;
            g.RadiusMm = saved != null && saved.RadiusMm > 0 ? saved.RadiusMm : AppConstants.DefaultMarkerRadiusMm;
            g.ShowOffsetText = saved?.ShowOffsetText ?? true;

            LineStyleOption match = LineStyleOptions.FirstOrDefault(o => o.Name == saved?.LineStyleName) ?? _defaultLineStyle;
            if (match != null)
            {
                g.LineStyleId = match.Id;
                g.LineStyleName = match.Name;
            }
        }

        private static CircleMarkerGroupSettings ToSettings(CircleMarkerGroup g) => new CircleMarkerGroupSettings
        {
            IsEnabled = g.IsEnabled,
            LineStyleName = g.LineStyleName,
            ColorName = g.ColorName,
            RadiusMm = g.RadiusMm,
            ShowOffsetText = g.ShowOffsetText
        };

        /// <summary>Saved after a run with at least one roof applied, and when the window closes.</summary>
        public void SaveSettings()
        {
            SyncLineStyleNames();
            var d = new AutoSlopeDrainSettings();
            TryParse(SlopeText, out double slope);
            TryParse(MaxPathText, out double maxPath);
            TryParse(MaxEdgeText, out double maxEdge);
            TryParse(PathSamplesText, out double samples);
            TryParse(AllowedOffsetThresholdText, out double offset);

            var settings = new AutoSlopeDrainSettings
            {
                SlopePercent = slope > 0 ? slope : d.SlopePercent,
                ThresholdMeters = maxPath > 0 ? maxPath : d.ThresholdMeters,
                ConnectionThresholdMeters = maxEdge > 0 ? maxEdge : d.ConnectionThresholdMeters,
                PathSampleCount = samples >= AppConstants.PathSamplesMin ? (int)Math.Round(samples) : d.PathSampleCount,
                InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,
                VerifyElevationsAfterCommit = VerifyElevationsAfterCommit,
                ExportFolderPath = ExportFolderPath,
                SelectedSizeFilter = SelectedRoofTab?.SelectedSizeFilter ?? "All",
                WriteCombinedSummary = WriteCombinedSummary,
                AskToOpenAfterExport = AskToOpenAfterExport,
                DrainMarkerGroup = ToSettings(DrainMarkerGroup),
                HighestPointMarkerGroup = ToSettings(HighestPointMarkerGroup),
                AllowedOffsetMarkerGroup = ToSettings(AllowedOffsetMarkerGroup),
                AllowedOffsetThresholdMm = offset > 0 ? offset : d.AllowedOffsetThresholdMm,
                PathOptionsExpanded = PathOptionsExpanded,
                MarkersExpanded = MarkersExpanded,
                ExportExpanded = ExportExpanded
            };

            if (!DrainSettingsStore.Save(settings))
                AddLog(LogLevel.Warning, $"Settings could not be saved to {DrainSettingsStore.SettingsPath}.");
        }

        // ══ Window close ═════════════════════════════════════════════════════

        /// <summary>Closing is blocked while a run is in progress.</summary>
        public bool CanClose()
        {
            if (State != DrainRunState.Running) return true;
            AddLog(LogLevel.Warning, "Close is blocked while a run is in progress. Press Cancel (or Esc) first.");
            return false;
        }

        /// <summary>
        /// Releases the window's listeners and cancels a run in progress. Called from the window's
        /// OnClosed, outside a Revit API context, so it makes no Revit calls.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Removing a Revit event handler here threw outside the API context (the old catch hid it
            // and left the handler attached); leaving the relay's list is always allowed.
            _viewActivatedSubscription?.Dispose();
            _viewActivatedSubscription = null;
            LogEntries.CollectionChanged -= OnLogChanged;
            foreach (var g in MarkerGroups) g.PropertyChanged -= OnMarkerGroupChanged;
            foreach (var t in RoofTabs) t.SelectionChanged -= OnAnyDrainSelectionChanged;
            _cts?.Cancel();
        }
    }
}
