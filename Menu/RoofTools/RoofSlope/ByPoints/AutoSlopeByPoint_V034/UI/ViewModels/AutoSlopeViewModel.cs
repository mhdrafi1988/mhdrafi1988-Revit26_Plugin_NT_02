// =======================================================
// File: AutoSlopeViewModel.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V034.UI.ViewModels
// Rewritten for V032 on Shared ToolViewModelBase (plan 1.1): IsRunning /
// Progress / SummaryText, the log collection (2,000 cap) and the Copy /
// Clear log commands come from the base class.
// Changes vs V030 (codes refer to claude/AutoSlopeByPoint_V030_Refactor_Plan.md):
//   B2  defaults come from AutoSlopeConstants via AutoSlopeSettings
//   B3  drainage distance and max link length are separate decimal inputs
//   B4  no garbled strings
//   B7  Run snapshots an immutable AutoSlopeRequest; inputs lock while running
//   B8  real start / end / total seconds
//   B9  every file open goes through DialogService.TryOpenFile
//   2.1 6 result tiles, "—" before the first run, plan-view badge
//   2.3 inline validation; Run disabled with the reason in the status strip
//   2.5 one RunState drives the UI; Run again after a run
//   D2  Drain mode option; re-pick drains from the window
// V032 (layout round 2):
//   - set-once options live in four sections that start closed; each
//     header shows a one-line summary and a "Custom" tag when anything
//     differs from the defaults; "Reset to defaults" per section; open /
//     closed state is remembered in settings
//   - results side panel: Longest 10 paths / Skipped points, same columns
//     as the Excel "Vertex Data" sheet; Zoom to and Copy rows
// V033:
//   - "Ridge lines" section: tick box (off by default) and line style
// =======================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Core.Services;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.Helpers;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V034.Infrastructure.RoofOps;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V034.UI.ViewModels
{
    public sealed record DrainModeOption(DrainMode Mode, string Label, string Hint);

    public sealed partial class AutoSlopeViewModel : ToolViewModelBase, IDisposable
    {
        private const string Dash = "—";
        private const int PumpIntervalMs = 100;

        private readonly UIApplication _app;
        private readonly Dispatcher _dispatcher;
        private readonly long _roofIdValue;
        private readonly string _projectTitle;
        private List<Point3> _picks;
        private CancellationTokenSource _cts;
        private AutoSlopeResult _lastResult;
        private DateTime _runStart;
        private DateTime _lastPump = DateTime.MinValue;
        private RunState _stateBeforePicking = RunState.Ready;
        private bool _loading = true;
        private bool _disposed;
        private readonly LineStyleInfo _defaultRidgeLineStyle;

        public AutoSlopeViewModel(UIApplication app, RoofBase roof, List<Point3> picks)
        {
            _app = app;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _roofIdValue = roof.Id.Value;
            _projectTitle = roof.Document?.Title ?? "Unknown project";
            _picks = picks ?? new List<Point3>();

            RoofInfo info = ProjectQueries.DescribeRoof(roof);
            RoofText = $"{info.TypeName} · Id {info.IdValue}";
            RoofLevelText = $"Level: {info.BaseLevelName}";

            var lineStyles = ProjectQueries.GetLineStyles(roof.Document);
            RidgeLineStyleOptions = lineStyles;
            _defaultRidgeLineStyle = lineStyles.FirstOrDefault(s => s.Name == AutoSlopeConstants.DefaultRidgeLineStyleName)
                                     ?? lineStyles.FirstOrDefault();
            SelectedRidgeLineStyle = _defaultRidgeLineStyle;
            MarkerGroups = new ObservableCollection<CircleMarkerGroupViewModel>
            {
                new CircleMarkerGroupViewModel(MarkerGroupKind.Drain, "Drains",
                    "A circle on each drain point.", AutoSlopeConstants.DefaultDrainColor, lineStyles),
                new CircleMarkerGroupViewModel(MarkerGroupKind.HighestPoint, "Highest point",
                    "A circle on every point tied at the highest calculated elevation.", AutoSlopeConstants.DefaultHighestColor, lineStyles),
                new CircleMarkerGroupViewModel(MarkerGroupKind.AllowedOffset, "Allowed offset",
                    "A circle on every point at or above the threshold, except points already marked highest.", AutoSlopeConstants.DefaultOffsetColor, lineStyles),
            };
            foreach (var g in MarkerGroups) g.PropertyChanged += OnMarkerGroupChanged;

            LogView = CollectionViewSource.GetDefaultView(LogEntries);
            LogView.Filter = PassesLogFilter;
            LogEntries.CollectionChanged += OnLogChanged;

            if (lineStyles.Count == 0)
                AddLog(LogLevel.Warning, "This project has no line styles, so circle markers use the default line style.");

            LoadSettings();
            UpdateViewBadge(_app.ActiveUIDocument?.ActiveView);
            _app.ViewActivated += OnViewActivated;

            AutoSlopeEventManager.Init();
            _loading = false;

            ResetTiles();
            Revalidate();
            UpdateLogSummary();
        }

        // ══ Header ═══════════════════════════════════════════════════════════

        public string ToolTitle => "Auto Slope By Point";
        public string VersionText => "v" + AutoSlopeToolInfo.ToolVersion;

        /// <summary>Window caption, "Auto Slope By Point — vX.Y.Z"; version comes from ToolCatalog.</summary>
        public string WindowTitle => ToolTitle + " — " + VersionText;
        public string RoofText { get; }
        public string RoofLevelText { get; }

        [ObservableProperty]
        private bool isPlanView;

        [ObservableProperty]
        private string viewBadgeText;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateText))]
        [NotifyPropertyChangedFor(nameof(RunButtonText))]
        [NotifyPropertyChangedFor(nameof(IsInputEnabled))]
        [NotifyPropertyChangedFor(nameof(IsCancelVisible))]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
        [NotifyCanExecuteChangedFor(nameof(RepickDrainsCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportResultsCommand))]
        [NotifyCanExecuteChangedFor(nameof(BrowseFolderCommand))]
        [NotifyCanExecuteChangedFor(nameof(ZoomToRowsCommand))]
        [NotifyPropertyChangedFor(nameof(ResultsEmptyText))]
        private RunState state = RunState.Ready;

        public string StateText => State switch
        {
            RunState.Picking => "Picking drains",
            RunState.Running => "Running",
            RunState.Done => "Completed",
            RunState.Cancelled => "Cancelled · roof unchanged",
            RunState.Failed => "Failed · roof unchanged",
            _ => "Ready"
        };

        public string RunButtonText => State switch
        {
            RunState.Running => "Running…",
            RunState.Ready or RunState.Picking => "Run AutoSlope",
            RunState.Done => "Done · re-pick to run",
            _ => "Run again"
        };

        public bool IsInputEnabled => State != RunState.Running && State != RunState.Picking;
        public bool IsCancelVisible => State == RunState.Running;

        /// <summary>Bound to the shell; true only while no percentage is known.</summary>
        [ObservableProperty]
        private bool isIndeterminate;

        // Result tiles
        [ObservableProperty] private string processedText = Dash;
        [ObservableProperty] private string skippedText = Dash;
        [ObservableProperty] private string drainsText = Dash;
        [ObservableProperty] private string highestText = Dash;
        [ObservableProperty] private string longestPathText = Dash;
        [ObservableProperty] private string runTimeText = Dash;
        [ObservableProperty] private string runTimeSubText = "Start → End";

        // ══ Inputs ═══════════════════════════════════════════════════════════

        public IReadOnlyList<string> SlopeOptions { get; } =
            AutoSlopeConstants.SlopePresets.Select(InputParsing.Format).ToList();

        [ObservableProperty] private string slopeText;
        [ObservableProperty] private string slopeError;
        [ObservableProperty] private string slopeHint;

        [ObservableProperty] private string maxDrainDistanceText;
        [ObservableProperty] private string maxDrainDistanceError;

        [ObservableProperty] private string maxEdgeLengthText;
        [ObservableProperty] private string maxEdgeLengthError;

        public IReadOnlyList<DrainModeOption> DrainModeOptions { get; } = new List<DrainModeOption>
        {
            new DrainModeOption(DrainMode.SnapToNearestPoint, "Snap to the nearest shape point",
                "Each pick uses the closest existing roof shape point. A pick far from any point is logged."),
            new DrainModeOption(DrainMode.AddPointAtPick, "Add a shape point at each pick",
                "Adds a new roof shape point exactly where you clicked and uses it as the drain."),
        };

        [ObservableProperty] private DrainModeOption selectedDrainMode;

        [ObservableProperty] private bool includeNearbyPoints;
        [ObservableProperty] private string snapRadiusText;
        [ObservableProperty] private string snapRadiusError;

        [ObservableProperty] private bool insertCurveIntersectionPoints;

        public ObservableCollection<CircleMarkerGroupViewModel> MarkerGroups { get; }

        // ── Ridge lines (V033) ─────────────────────────────────────────────
        /// <summary>Draw detail lines along the ridges between drain groups. Off by default.</summary>
        [ObservableProperty] private bool drawRidgeLines;

        /// <summary>The project's line styles, for the ridge line style combo box.</summary>
        public IReadOnlyList<LineStyleInfo> RidgeLineStyleOptions { get; }

        [ObservableProperty] private LineStyleInfo selectedRidgeLineStyle;

        // ── Option sections (closed by default, state remembered) ────────
        [ObservableProperty] private bool drainOptionsExpanded;
        [ObservableProperty] private bool pathOptionsExpanded;
        [ObservableProperty] private bool markersExpanded;
        [ObservableProperty] private bool exportExpanded;
        [ObservableProperty] private bool ridgeLinesExpanded;

        [ObservableProperty] private string drainOptionsSummary;
        [ObservableProperty] private bool isDrainOptionsCustom;
        [ObservableProperty] private string pathOptionsSummary;
        [ObservableProperty] private bool isPathOptionsCustom;
        [ObservableProperty] private string markersSummary;
        [ObservableProperty] private bool isMarkersCustom;
        [ObservableProperty] private string exportSummary;
        [ObservableProperty] private bool isExportCustom;
        [ObservableProperty] private string ridgeLinesSummary;
        [ObservableProperty] private bool isRidgeLinesCustom;

        private string DefaultExportFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AutoSlopeConstants.DefaultExportFolder);

        private static bool Same(string text, double expected) =>
            InputParsing.TryParse(text, out double v) && Math.Abs(v - expected) < 1e-9;

        private void UpdateSectionSummaries()
        {
            bool snap = (SelectedDrainMode?.Mode ?? DrainMode.SnapToNearestPoint) == DrainMode.SnapToNearestPoint;
            DrainOptionsSummary = (snap ? "Snap to nearest point" : "Add a point at each pick")
                + (IncludeNearbyPoints ? $" · nearby {SnapRadiusText} mm" : " · nearby off");
            IsDrainOptionsCustom = !snap || !IncludeNearbyPoints || !Same(SnapRadiusText, AutoSlopeConstants.DefaultSnapRadiusMm);

            bool autoEdge = string.IsNullOrWhiteSpace(MaxEdgeLengthText) || Same(MaxEdgeLengthText, 0);
            PathOptionsSummary = $"Max link: {(autoEdge ? "automatic" : MaxEdgeLengthText + " m")} · arc points {(InsertCurveIntersectionPoints ? "on" : "off")}";
            IsPathOptionsCustom = !autoEdge || !InsertCurveIntersectionPoints;

            var on = MarkerGroups.Where(g => g.IsEnabled).Select(g => g.ChipText).ToList();
            MarkersSummary = (on.Count == 0 ? "Off" : string.Join(" · ", on)) + " · plan view only";
            IsMarkersCustom = MarkerGroups.Any(g => !g.IsAtDefaults);

            string ridgeStyle = SelectedRidgeLineStyle?.Name ?? AutoSlopeConstants.DefaultRidgeLineStyleName;
            RidgeLinesSummary = DrawRidgeLines ? $"On · {ridgeStyle} · plan view only" : "Off";
            IsRidgeLinesCustom = DrawRidgeLines || !ReferenceEquals(SelectedRidgeLineStyle, _defaultRidgeLineStyle);

            string folderName = string.IsNullOrWhiteSpace(ExportFolderPath) ? "(no folder)" : Path.GetFileName(ExportFolderPath.TrimEnd('\\', '/'));
            ExportSummary = ExportToExcel
                ? $"Excel after each run · …\\{folderName}{(AskToOpenAfterExport ? " · ask to open" : string.Empty)}"
                : "Excel off";
            IsExportCustom = !ExportToExcel || AskToOpenAfterExport
                || !string.Equals(ExportFolderPath, DefaultExportFolder, StringComparison.OrdinalIgnoreCase);
        }

        [RelayCommand]
        private void ResetDrainOptions()
        {
            SelectedDrainMode = DrainModeOptions[0];
            IncludeNearbyPoints = true;
            SnapRadiusText = InputParsing.Format(AutoSlopeConstants.DefaultSnapRadiusMm);
        }

        [RelayCommand]
        private void ResetPathOptions()
        {
            MaxEdgeLengthText = InputParsing.Format(AutoSlopeConstants.DefaultMaxEdgeLengthM);
            InsertCurveIntersectionPoints = true;
        }

        [RelayCommand]
        private void ResetMarkers()
        {
            foreach (var g in MarkerGroups) g.ResetToDefaults();
        }

        [RelayCommand]
        private void ResetRidgeLines()
        {
            DrawRidgeLines = false;
            SelectedRidgeLineStyle = _defaultRidgeLineStyle;
        }

        [RelayCommand]
        private void ResetExport()
        {
            ExportToExcel = true;
            ExportFolderPath = DefaultExportFolder;
            AskToOpenAfterExport = false;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAskToOpen))]
        private bool exportToExcel;

        [ObservableProperty] private string exportFolderPath;
        [ObservableProperty] private bool askToOpenAfterExport;
        public bool CanAskToOpen => ExportToExcel;

        public string DrainCountText => _picks.Count == 1 ? "1 Drain Picked" : $"{_picks.Count} Drains Picked";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private string validationMessage;

        // Re-validate whenever an input changes.
        partial void OnSlopeTextChanged(string value) => Revalidate();
        partial void OnMaxDrainDistanceTextChanged(string value) => Revalidate();
        partial void OnMaxEdgeLengthTextChanged(string value) => Revalidate();
        partial void OnIncludeNearbyPointsChanged(bool value) => Revalidate();
        partial void OnSnapRadiusTextChanged(string value) => Revalidate();
        partial void OnExportToExcelChanged(bool value) => Revalidate();
        partial void OnExportFolderPathChanged(string value) => Revalidate();
        partial void OnSelectedDrainModeChanged(DrainModeOption value) => Revalidate();
        partial void OnInsertCurveIntersectionPointsChanged(bool value) => Revalidate();
        partial void OnAskToOpenAfterExportChanged(bool value) => Revalidate();
        partial void OnDrawRidgeLinesChanged(bool value) => Revalidate();
        partial void OnSelectedRidgeLineStyleChanged(LineStyleInfo value) => Revalidate();

        private void OnMarkerGroupChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CircleMarkerGroupViewModel.RadiusError) ||
                e.PropertyName == nameof(CircleMarkerGroupViewModel.ThresholdError)) return;
            Revalidate();
        }

        private void Revalidate()
        {
            if (_loading) return;

            var errors = new List<string>();

            SlopeError = InputParsing.CheckRange(SlopeText,
                AutoSlopeConstants.SlopeMin, AutoSlopeConstants.SlopeMax, "Slope", "%", out double slope);
            SlopeHint = SlopeError == null ? $"≈ {slope * 100:0} mm rise per 10 m" : string.Empty;
            if (SlopeError != null) errors.Add(SlopeError);

            MaxDrainDistanceError = InputParsing.CheckRange(MaxDrainDistanceText,
                AutoSlopeConstants.DrainDistanceMinM, AutoSlopeConstants.DrainDistanceMaxM,
                "Drainage distance", "m", out _);
            if (MaxDrainDistanceError != null) errors.Add(MaxDrainDistanceError);

            MaxEdgeLengthError = null;
            bool autoEdge = string.IsNullOrWhiteSpace(MaxEdgeLengthText)
                || (InputParsing.TryParse(MaxEdgeLengthText, out double edge) && edge == 0);
            if (!autoEdge)
            {
                MaxEdgeLengthError = InputParsing.CheckRange(MaxEdgeLengthText,
                    AutoSlopeConstants.EdgeLengthMinM, AutoSlopeConstants.EdgeLengthMaxM,
                    "Max link length", "m (or 0 for automatic)", out _);
                if (MaxEdgeLengthError != null) errors.Add(MaxEdgeLengthError);
            }

            SnapRadiusError = null;
            if (IncludeNearbyPoints)
            {
                SnapRadiusError = InputParsing.CheckRange(SnapRadiusText,
                    AutoSlopeConstants.SnapRadiusMinMm, AutoSlopeConstants.SnapRadiusMaxMm,
                    "Nearby radius", "mm", out _);
                if (SnapRadiusError != null) errors.Add(SnapRadiusError);
            }

            foreach (var g in MarkerGroups)
                errors.AddRange(g.Validate());

            if (ExportToExcel && string.IsNullOrWhiteSpace(ExportFolderPath))
                errors.Add("Choose an export folder, or turn Excel export off.");

            ValidationMessage = errors.FirstOrDefault();
            UpdateSectionSummaries();
            UpdateIdleSummary();
        }

        // ══ Run ══════════════════════════════════════════════════════════════

        // Disabled after a successful run; re-picking the drains sets State back to Ready.
        private bool CanRun() =>
            IsInputEnabled
            && State != RunState.Done
            && string.IsNullOrEmpty(ValidationMessage)
            && _picks.Count > 0
            && AutoSlopeEventManager.Handler?.HasPending != true;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            Revalidate();
            if (!string.IsNullOrEmpty(ValidationMessage)) return;

            AutoSlopeRequest request = BuildRequest();

            LogEntries.Clear();
            _cts = new CancellationTokenSource();
            _runStart = DateTime.Now;
            _lastPump = DateTime.MinValue;

            State = RunState.Running;
            IsRunning = true;
            IsIndeterminate = true; // until the first progress report arrives
            Progress = 0;
            SummaryText = $"Started {_runStart:HH:mm:ss} · waiting for Revit…";
            ResetTiles();
            ClearResults();
            RunTimeText = "Running";
            RunTimeSubText = $"Started {_runStart:HH:mm:ss}";

            var job = new RunJob(request, OnRunProgress, Log, _cts.Token, OnRunCompleted);
            if (!AutoSlopeEventManager.Submit(job))
            {
                AddLog(LogLevel.Error, "Revit did not accept the request (it may be busy). Try Run again.");
                FinishRun(AutoSlopeResult.Failed("Revit did not accept the request.", _runStart));
            }
        }

        private AutoSlopeRequest BuildRequest()
        {
            InputParsing.TryParse(SlopeText, out double slope);
            InputParsing.TryParse(MaxDrainDistanceText, out double distance);
            InputParsing.TryParse(MaxEdgeLengthText, out double edge);
            InputParsing.TryParse(SnapRadiusText, out double radius);
            var offsetGroup = MarkerGroups.First(g => g.Kind == MarkerGroupKind.AllowedOffset);

            return new AutoSlopeRequest
            {
                RoofIdValue = _roofIdValue,
                PickedPoints = _picks.ToList(),
                SlopePercent = slope,
                MaxDrainDistanceM = distance,
                MaxEdgeLengthM = Math.Max(0, edge),
                DrainMode = SelectedDrainMode?.Mode ?? DrainMode.SnapToNearestPoint,
                IncludeNearbyPoints = IncludeNearbyPoints,
                SnapRadiusMm = radius,
                InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,
                ExportToExcel = ExportToExcel,
                ExportFolder = ExportFolderPath,
                Markers = MarkerGroups.Select(g => g.ToStyle()).ToList(),
                DrawRidgeLines = DrawRidgeLines,
                RidgeLineStyleIdValue = SelectedRidgeLineStyle?.IdValue ?? 0,
                RidgeLineStyleName = SelectedRidgeLineStyle?.Name,
                AllowedOffsetThresholdMm = offsetGroup.ThresholdMm,
                ProjectTitle = _projectTitle
            };
        }

        /// <summary>Called synchronously on Revit's thread during the run.</summary>
        private void OnRunProgress(RunProgressInfo info)
        {
            IsIndeterminate = false;
            Progress = Math.Max(Progress, info.OverallPercent);
            string within = info.PercentWithinPhase.HasValue ? $" · {info.PercentWithinPhase.Value:0} %" : string.Empty;
            SummaryText = _cts?.IsCancellationRequested == true
                ? "Cancelling…"
                : $"Started {_runStart:HH:mm:ss} · {info.PhaseLabel}{within}";

            if ((DateTime.Now - _lastPump).TotalMilliseconds >= PumpIntervalMs)
            {
                _lastPump = DateTime.Now;
                UiPumpHelper.DoEvents(); // repaint + let a Cancel click through
            }
        }

        private void Log(LogLevel level, string message) => AddLog(level, message);

        private void OnRunCompleted(AutoSlopeResult result) => FinishRun(result);

        private void FinishRun(AutoSlopeResult result)
        {
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
            IsIndeterminate = false;
            Progress = 0;

            if (result.Success)
            {
                _lastResult = result;
                State = RunState.Done;
                ShowResultTiles(result);
                FillResults(result);
                SummaryText =
                    $"Done · {result.VerticesProcessed} processed, {result.VerticesSkipped} skipped · " +
                    $"Start {result.StartTime:HH:mm:ss} · End {result.EndTime:HH:mm:ss} · Total {result.DurationSeconds:0.0} s" +
                    (string.IsNullOrEmpty(result.ExportedFilePath) ? string.Empty : " · Excel exported");
                SaveSettings();

                if (!string.IsNullOrEmpty(result.ExportedFilePath) && AskToOpenAfterExport)
                {
                    string path = result.ExportedFilePath;
                    _dispatcher.BeginInvoke(new Action(() => OfferToOpen(path)));
                }
            }
            else
            {
                _lastResult = null;
                State = result.WasCancelled ? RunState.Cancelled : RunState.Failed;
                ResetTiles();
                ClearResults();
                RunTimeText = result.WasCancelled ? "Stopped" : "Failed";
                RunTimeSubText = $"{result.StartTime:HH:mm:ss} → {result.EndTime:HH:mm:ss}";
                SummaryText = result.WasCancelled
                    ? $"Cancelled at {result.EndTime:HH:mm:ss} (started {result.StartTime:HH:mm:ss}) · nothing was changed in the model"
                    : $"Failed · {result.ErrorMessage} · nothing was changed in the model";
            }
        }

        private bool CanCancelRun() => State == RunState.Running;

        [RelayCommand(CanExecute = nameof(CanCancelRun))]
        private void CancelRun()
        {
            if (_cts == null || _cts.IsCancellationRequested) return;
            _cts.Cancel();
            SummaryText = "Cancelling…";
            AddLog(LogLevel.Warning, "Cancel requested. The run stops at the next check and every change is rolled back.");
        }

        // ══ Re-pick drains ═══════════════════════════════════════════════════

        private bool CanRepickDrains() => IsInputEnabled && AutoSlopeEventManager.Handler?.HasPending != true;

        [RelayCommand(CanExecute = nameof(CanRepickDrains))]
        private void RepickDrains()
        {
            _stateBeforePicking = State;
            State = RunState.Picking;
            SummaryText = "Pick drain points in the Revit view, then click Finish. Esc keeps the current drains.";

            var job = new RepickJob(_roofIdValue, OnDrainsPicked, OnRepickCancelled);
            if (!AutoSlopeEventManager.Submit(job))
            {
                AddLog(LogLevel.Error, "Revit did not accept the request (it may be busy). Try again.");
                OnRepickCancelled();
            }
        }

        private void OnDrainsPicked(List<Point3> points)
        {
            _picks = points;
            OnPropertyChanged(nameof(DrainCountText));
            AddLog(LogLevel.Info, $"Drains re-picked: {DrainCountText}.");
            State = RunState.Ready;
            ResetTiles();
            ClearResults();
            Revalidate();
            RunCommand.NotifyCanExecuteChanged();
        }

        private void OnRepickCancelled()
        {
            State = _stateBeforePicking;
            AddLog(LogLevel.Info, $"Re-pick cancelled. Keeping {DrainCountText}.");
            UpdateIdleSummary();
        }

        // ══ Export ═══════════════════════════════════════════════════════════

        private bool CanBrowseFolder() => IsInputEnabled;

        [RelayCommand(CanExecute = nameof(CanBrowseFolder))]
        private void BrowseFolder()
        {
            string selected = DialogService.SelectFolder(ExportFolderPath);
            if (string.IsNullOrEmpty(selected)) return;
            ExportFolderPath = selected;
            AddLog(LogLevel.Info, $"Export folder: {selected}");
        }

        private bool CanExportResults() => State == RunState.Done && _lastResult?.Success == true;

        [RelayCommand(CanExecute = nameof(CanExportResults))]
        private void ExportResults()
        {
            string path = DialogService.SaveExcel(ExportFolderPath, ExcelExportService.DefaultFileName(_lastResult));
            if (string.IsNullOrEmpty(path)) return;

            string error = ExcelExportService.ExportTo(path, _lastResult, Log);
            if (error != null)
            {
                AddLog(LogLevel.Error, $"Export failed: {error}");
                return;
            }

            AddLog(LogLevel.Success, $"Results exported: {path}");
            if (AskToOpenAfterExport) OfferToOpen(path);
        }

        private void OfferToOpen(string path)
        {
            if (!DialogService.AskToOpen(path)) return;
            string error = DialogService.TryOpenFile(path);
            if (error != null) AddLog(LogLevel.Warning, $"Could not open the file: {error}");
        }

        // ══ Results side panel (V032) ════════════════════════════════════════

        private const int ResultRowCount = 10;

        public ObservableCollection<ResultRow> LongestRows { get; } = new ObservableCollection<ResultRow>();
        public ObservableCollection<ResultRow> SkippedRows { get; } = new ObservableCollection<ResultRow>();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsLongestTab))]
        [NotifyPropertyChangedFor(nameof(IsSkippedTab))]
        [NotifyPropertyChangedFor(nameof(ResultsTitle))]
        [NotifyPropertyChangedFor(nameof(ResultsEmptyText))]
        [NotifyPropertyChangedFor(nameof(ShowResultsEmpty))]
        private ResultsTab activeResultsTab = ResultsTab.Longest;

        public bool IsLongestTab => ActiveResultsTab == ResultsTab.Longest;
        public bool IsSkippedTab => ActiveResultsTab == ResultsTab.Skipped;

        [ObservableProperty] private int skippedTotal;

        public string SkippedTabText => _lastResult == null ? "Skipped" : $"Skipped ({SkippedTotal})";

        public string ResultsTitle => IsLongestTab
            ? "Longest 10 paths"
            : SkippedTotal > ResultRowCount ? $"Skipped points (first {ResultRowCount} of {SkippedTotal})" : "Skipped points";

        public bool ShowResultsEmpty => IsLongestTab ? LongestRows.Count == 0 : SkippedRows.Count == 0;

        public string ResultsEmptyText => State switch
        {
            RunState.Running => "Running… the table fills when the run finishes.",
            RunState.Cancelled => "The run was cancelled, so there is nothing to show.",
            RunState.Failed => "The run failed, so there is nothing to show.",
            RunState.Done when IsSkippedTab => "No points were skipped.",
            _ => IsLongestTab ? "Run AutoSlope to see the 10 longest drain paths." : "Skipped points appear here after a run."
        };

        /// <summary>Set by the window from the active grid's selection.</summary>
        public IList<ResultRow> SelectedResultRows { get; private set; } = new List<ResultRow>();

        [ObservableProperty] private string resultsHint = "Same columns as the Excel “Vertex Data” sheet.";

        public void SetSelectedResultRows(IList<ResultRow> rows)
        {
            SelectedResultRows = rows ?? new List<ResultRow>();
            ResultsHint = SelectedResultRows.Count > 0
                ? $"{SelectedResultRows.Count} row(s) selected · Ctrl+click adds rows"
                : "Same columns as the Excel “Vertex Data” sheet. Select rows, then Zoom to.";
            ZoomToRowsCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void SetResultsTab(string tab)
        {
            if (Enum.TryParse(tab, out ResultsTab t)) ActiveResultsTab = t;
            SetSelectedResultRows(null);
        }

        private void FillResults(AutoSlopeResult r)
        {
            LongestRows.Clear();
            SkippedRows.Clear();

            int n = 1;
            foreach (var v in r.Vertices.Where(v => v.WasProcessed).OrderByDescending(v => v.PathLengthMeters).Take(ResultRowCount))
                LongestRows.Add(ResultRow.FromProcessed(v, n++));

            var skipped = r.Vertices.Where(v => !v.WasProcessed)
                .OrderBy(v => v.Outcome == VertexOutcome.NoPath ? 0 : 1)
                .ThenBy(v => v.PathLengthMeters)
                .ToList();
            SkippedTotal = skipped.Count;
            n = 1;
            foreach (var v in skipped.Take(ResultRowCount))
                SkippedRows.Add(ResultRow.FromSkipped(v, n++));

            NotifyResultsChanged();
        }

        private void ClearResults()
        {
            LongestRows.Clear();
            SkippedRows.Clear();
            SkippedTotal = 0;
            SetSelectedResultRows(null);
            NotifyResultsChanged();
        }

        private void NotifyResultsChanged()
        {
            OnPropertyChanged(nameof(SkippedTabText));
            OnPropertyChanged(nameof(ResultsTitle));
            OnPropertyChanged(nameof(ShowResultsEmpty));
            OnPropertyChanged(nameof(ResultsEmptyText));
            CopyResultRowsCommand.NotifyCanExecuteChanged();
            ZoomToRowsCommand.NotifyCanExecuteChanged();
        }

        private bool CanZoomToRows() =>
            State == RunState.Done && SelectedResultRows.Count > 0 && AutoSlopeEventManager.Handler?.HasPending != true;

        [RelayCommand(CanExecute = nameof(CanZoomToRows))]
        private void ZoomToRows()
        {
            var points = SelectedResultRows.Select(r => r.Position).ToList();
            if (!AutoSlopeEventManager.Submit(new ZoomJob(_roofIdValue, points, Log)))
                AddLog(LogLevel.Warning, "Revit did not accept the request (it may be busy). Try again.");
        }

        private bool CanCopyResultRows() => LongestRows.Count > 0 || SkippedRows.Count > 0;

        /// <summary>Copies the selected rows (or the whole tab) as tab-separated text for Excel.</summary>
        [RelayCommand(CanExecute = nameof(CanCopyResultRows))]
        private void CopyResultRows()
        {
            var rows = SelectedResultRows.Count > 0
                ? SelectedResultRows.OrderBy(r => r.RowNumber).ToList()
                : (IsLongestTab ? LongestRows : SkippedRows).ToList();
            if (rows.Count == 0) return;

            string text = ResultRow.Header(ActiveResultsTab) + Environment.NewLine
                + string.Join(Environment.NewLine, rows.Select(r => r.ToTsv(ActiveResultsTab)));
            try
            {
                Clipboard.SetText(text);
                AddLog(LogLevel.Info, $"Copied {rows.Count} row(s). Paste into Excel to get the same columns.");
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
                string path = LogExportService.Export("AutoSlopeByPoint", LogEntries, folder);
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
            DrainsText = $"{_picks.Count} → {Dash}";
            HighestText = Dash;
            LongestPathText = Dash;
            RunTimeText = Dash;
            RunTimeSubText = "Start → End";
        }

        private void ShowResultTiles(AutoSlopeResult r)
        {
            ProcessedText = r.VerticesProcessed.ToString("N0");
            SkippedText = r.VerticesSkipped.ToString("N0");
            DrainsText = $"{r.PickedDrainCount} → {r.FinalDrainCount}";
            HighestText = $"{r.HighestElevationMm:0} mm";
            LongestPathText = $"{r.LongestPathM:0.00} m";
            RunTimeText = $"{r.DurationSeconds:0.0} s";
            RunTimeSubText = $"{r.StartTime:HH:mm:ss} → {r.EndTime:HH:mm:ss}";
        }

        private void UpdateIdleSummary()
        {
            if (State == RunState.Running || State == RunState.Picking) return;

            if (!string.IsNullOrEmpty(ValidationMessage))
                SummaryText = "Can't run yet · " + ValidationMessage;
            else if (State == RunState.Ready)
                SummaryText = $"Ready · {DrainCountText}";
        }

        // ══ Active view badge ════════════════════════════════════════════════

        private void OnViewActivated(object sender, ViewActivatedEventArgs e) => UpdateViewBadge(e.CurrentActiveView);

        private void UpdateViewBadge(View view)
        {
            ViewInfo v = ProjectQueries.DescribeView(view);
            IsPlanView = v.IsPlan;
            ViewBadgeText = v.IsPlan
                ? $"Plan view · {v.Name}"
                : $"Active view is not a plan ({v.Name}) · circle markers and ridge lines will be skipped";
        }

        // ══ Settings ═════════════════════════════════════════════════════════

        private void LoadSettings()
        {
            AutoSlopeSettings s = AutoSlopeSettingsStore.Load();

            MarkerGroups[0].Apply(s.DrainMarkerGroup);
            MarkerGroups[1].Apply(s.HighestPointMarkerGroup);
            MarkerGroups[2].Apply(s.AllowedOffsetMarkerGroup);
            MarkerGroups[2].ThresholdText = InputParsing.Format(s.AllowedOffsetThresholdMm);

            SlopeText = InputParsing.Format(s.SlopePercent);
            MaxDrainDistanceText = InputParsing.Format(s.MaxDrainDistanceM);
            MaxEdgeLengthText = InputParsing.Format(s.MaxEdgeLengthM);
            SelectedDrainMode = DrainModeOptions.FirstOrDefault(o => o.Mode == s.DrainMode) ?? DrainModeOptions[0];
            IncludeNearbyPoints = s.IncludeNearbyPoints;
            SnapRadiusText = InputParsing.Format(s.SnapRadiusMm);
            InsertCurveIntersectionPoints = s.InsertCurveIntersectionPoints;

            ExportFolderPath = string.IsNullOrWhiteSpace(s.ExportFolderPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AutoSlopeConstants.DefaultExportFolder)
                : s.ExportFolderPath;
            ExportToExcel = s.ExportToExcel;
            AskToOpenAfterExport = s.AskToOpenAfterExport;

            DrainOptionsExpanded = s.DrainOptionsExpanded;
            PathOptionsExpanded = s.PathOptionsExpanded;
            MarkersExpanded = s.MarkersExpanded;
            ExportExpanded = s.ExportExpanded;

            DrawRidgeLines = s.DrawRidgeLines;
            if (!string.IsNullOrWhiteSpace(s.RidgeLineStyleName))
            {
                var match = RidgeLineStyleOptions.FirstOrDefault(o => o.Name == s.RidgeLineStyleName);
                if (match != null) SelectedRidgeLineStyle = match; // missing in this project → keep the default
            }
            RidgeLinesExpanded = s.RidgeLinesExpanded;
        }

        /// <summary>Saved after a successful run and when the window closes.</summary>
        public void SaveSettings()
        {
            InputParsing.TryParse(SlopeText, out double slope);
            InputParsing.TryParse(MaxDrainDistanceText, out double distance);
            InputParsing.TryParse(MaxEdgeLengthText, out double edge);
            InputParsing.TryParse(SnapRadiusText, out double radius);
            var d = new AutoSlopeSettings();

            var settings = new AutoSlopeSettings
            {
                DrainMarkerGroup = MarkerGroups[0].ToSettings(),
                HighestPointMarkerGroup = MarkerGroups[1].ToSettings(),
                AllowedOffsetMarkerGroup = MarkerGroups[2].ToSettings(),
                AllowedOffsetThresholdMm = MarkerGroups[2].ThresholdMm,
                SlopePercent = slope > 0 ? slope : d.SlopePercent,
                MaxDrainDistanceM = distance > 0 ? distance : d.MaxDrainDistanceM,
                MaxEdgeLengthM = Math.Max(0, edge),
                DrainMode = SelectedDrainMode?.Mode ?? DrainMode.SnapToNearestPoint,
                IncludeNearbyPoints = IncludeNearbyPoints,
                SnapRadiusMm = radius > 0 ? radius : d.SnapRadiusMm,
                InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,
                ExportFolderPath = ExportFolderPath,
                ExportToExcel = ExportToExcel,
                AskToOpenAfterExport = AskToOpenAfterExport,
                DrainOptionsExpanded = DrainOptionsExpanded,
                PathOptionsExpanded = PathOptionsExpanded,
                MarkersExpanded = MarkersExpanded,
                ExportExpanded = ExportExpanded,
                DrawRidgeLines = DrawRidgeLines,
                RidgeLineStyleName = SelectedRidgeLineStyle?.Name,
                RidgeLinesExpanded = RidgeLinesExpanded
            };

            if (!AutoSlopeSettingsStore.Save(settings))
                AddLog(LogLevel.Warning, $"Settings could not be saved to {AutoSlopeSettingsStore.SettingsPath}.");
        }

        // ══ Window close ═════════════════════════════════════════════════════

        /// <summary>The window asks before closing; closing is blocked while a run is in progress.</summary>
        public bool CanClose()
        {
            if (State != RunState.Running) return true;
            AddLog(LogLevel.Warning, "Close is blocked while a run is in progress. Press Cancel (or Esc) first.");
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _app.ViewActivated -= OnViewActivated; } catch { /* Revit shutting down */ }
            LogEntries.CollectionChanged -= OnLogChanged;
            foreach (var g in MarkerGroups) g.PropertyChanged -= OnMarkerGroupChanged;
            _cts?.Cancel();
        }
    }
}
