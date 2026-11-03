// =======================================================
// File: AutoSlopeViewModel.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001
// Refactored to CommunityToolkit.Mvvm ([ObservableProperty]/[RelayCommand])
// per MasterGuide stack convention, matching CircleMarkerGroup.cs in this
// same tool. Same public API (property/command names unchanged, so XAML
// bindings are untouched) â€” INotifyPropertyChanged/RelayCommand backing
// fields are now source-generated instead of hand-written.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.UI.ViewModels
{
    public partial class AutoSlopeViewModel : ObservableObject
    {
        // â”€â”€ RunState â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // NEW: Cancelled added, per Rafi's confirmed Cancel decision.
        private enum RunState { Ready, Running, Done, Cancelled }

        private RunState _state = RunState.Ready;
        private RunState State
        {
            get => _state;
            set
            {
                _state = value;
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(StatusColor));
                RunCommand.NotifyCanExecuteChanged();
                ExportResultsCommand.NotifyCanExecuteChanged();
                CancelRunCommand.NotifyCanExecuteChanged();
            }
        }

        private bool IsRunning  => _state == RunState.Running;
        private bool IsComplete => _state == RunState.Done;

        // â”€â”€ Multi-Slope Floor Variants: 4 fixed rows â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>Suggested slope % values offered in each row's typable ComboBox â€” not a restriction, any positive value can be typed.</summary>
        public List<double> PercentPresets { get; } = new List<double> { 0.5, 1, 1.5, 2, 3, 4 };

        public List<SlopeRowViewModel> SlopeRows { get; }

        // â”€â”€ Live metrics (recalculated as checkboxes/combos change) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        private string selectedFloorLabel;

        [ObservableProperty]
        private int activeSlopeCount;

        [ObservableProperty]
        private int copiesToCreate;

        [ObservableProperty]
        private int worksetsNewCount;

        /// <summary>
        /// Recomputes the live metrics card from the current row states.
        /// WorksetsNewCount checks the live document's worksets, so a name
        /// already created by an earlier Run in this session correctly stops
        /// counting as "new" without needing a re-open of the window.
        /// </summary>
        private void RecalculateLiveMetrics()
        {
            var activeRows = SlopeRows.Where(r => r.IsRowActive).ToList();
            ActiveSlopeCount = activeRows.Count;
            CopiesToCreate = activeRows.Count;

            Document doc = UIDoc?.Document;
            if (doc == null || !doc.IsWorkshared)
            {
                WorksetsNewCount = 0;
                return;
            }

            var existingNames = new HashSet<string>(
                new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).Select(w => w.Name));

            WorksetsNewCount = activeRows.Count(r =>
                !existingNames.Contains(string.Format(AppConstants.WorksetNameFormat, FormatPercent(r.Percent))));
        }

        private static string FormatPercent(double percent) =>
            percent % 1 == 0 ? percent.ToString("0") : percent.ToString("0.##");

        // â”€â”€ Input properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        private int thresholdMeters = AppConstants.DefaultThresholdMeters;

        [ObservableProperty]
        private int drainToleranceMm = AppConstants.DefaultDrainToleranceMm;

        [ObservableProperty]
        private bool enableDrainTolerance = true;

        [ObservableProperty]
        private bool insertCurveIntersectionPoints = true;

        // â”€â”€ Circle Markers (V026) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>Style/config for circles placed on final drain points.</summary>
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

        /// <summary>Style/config for circles placed on processed vertices meeting the Allowed Offset threshold.</summary>
        public CircleMarkerGroup AllowedOffsetMarkerGroup { get; } = new CircleMarkerGroup
        {
            GroupLabel = "Allowed Offset",
            ColorName = "Orange",
            RadiusMm = 250
        };

        [ObservableProperty]
        private double allowedOffsetThresholdMm = 500;

        /// <summary>Named colors offered in each group's Color dropdown.</summary>
        public IReadOnlyList<string> ColorPalette { get; } = NamedColorHelper.PaletteNames;

        /// <summary>
        /// Project's existing Line Style options (OST_Lines subcategories),
        /// populated in the constructor via FilteredElementCollector. The tool
        /// never creates new line styles â€” user picks from what already exists.
        /// </summary>
        public ObservableCollection<LineStyleOption> LineStyleOptions { get; } = new ObservableCollection<LineStyleOption>();

        [ObservableProperty]
        private string exportFolderPath;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAskToOpenAfterExport))]
        private bool exportToExcel = true;

        /// <summary>
        /// When true, a successful Excel export (Run-completion path or manual
        /// Export) prompts the user with a Yes/No "Open it now?" dialog.
        /// When false (default), the exported file is never opened automatically
        /// and no prompt is shown. Disabled in the UI unless ExportToExcel is on.
        /// </summary>
        [ObservableProperty]
        private bool askToOpenAfterExport = false;

        /// <summary>UI-only gate: the "Ask to open" checkbox is enabled only when Export to Excel is on.</summary>
        public bool CanAskToOpenAfterExport => ExportToExcel;

        // â”€â”€ Log (Shared LogEntry collection) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// Bound to the log ListView/ItemsControl in the View.
        /// Each entry carries LogLevel for colour-coding via LogLevelToColorConverter.
        /// </summary>
        public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();

        // â”€â”€ Status â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public string StatusMessage => _state switch
        {
            RunState.Running   => "Processing...",
            RunState.Done      => "Completed",
            RunState.Cancelled => "Cancelled",
            _                  => "Ready to run"
        };

        public string StatusColor => _state switch
        {
            RunState.Running   => AppConstants.Color_Processing,
            RunState.Done      => AppConstants.Color_Success,
            RunState.Cancelled => AppConstants.Color_Warning,
            _                  => AppConstants.Color_Ready
        };

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

        private bool CanCancelRun() => _state == RunState.Running;

        [RelayCommand(CanExecute = nameof(CanCancelRun))]
        private void CancelRun()
        {
            if (_runCts == null || _runCts.IsCancellationRequested) return;
            _runCts.Cancel();
            ProgressPhaseText = "Cancellingâ€¦";
            AddLog(new LogEntry(LogLevel.Warning, "Cancel requested â€” finishing the current slope's in-progress work, then stopping."));
        }

        // â”€â”€ Result properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        private int verticesProcessed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        private int verticesSkipped;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        private int pickedDrainCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        private int finalDrainCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HighestElevationDisplay))]
        private double highestElevation_mm;

        public string HighestElevationDisplay => $"{HighestElevation_mm:0} mm";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LongestPathDisplay))]
        private double longestPath_m;

        public string LongestPathDisplay => $"{LongestPath_m:0.00} m";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RunDurationDisplay))]
        [NotifyPropertyChangedFor(nameof(RunDuration_ms))]
        private int runDuration_sec;

        // Displayed in milliseconds: whole-seconds value from the engine Ã— 1000
        // (quick display-only conversion â€” not true ms-precision timing).
        public string RunDurationDisplay => $"{RunDuration_ms} ms";
        public int RunDuration_ms => RunDuration_sec * 1000;

        [ObservableProperty]
        private int curvesCalculated;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        private string runDate;

        // â”€â”€ Circle Marker result counts (V026) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        private int drainCirclesPlaced;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        private int highestCirclesPlaced;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        private int offsetCirclesPlaced;

        // â”€â”€ Multi-Slope completion totals â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CompletionSummaryText))]
        private int copiesCreatedCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CompletionSummaryText))]
        private int worksetsCreatedCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CompletionSummaryText))]
        private int failedCount;

        public string CompletionSummaryText =>
            $"{CopiesCreatedCount} copies created | {WorksetsCreatedCount} worksets created | {FailedCount} failed";

        /// <summary>Engine result for the last successfully-completed copy in the run â€” feeds SummaryText and the manual Export Results command.</summary>
        private AutoSlopeResult _lastResult;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SummaryText))]
        private double lastResultPercent;

        public string SummaryText =>
$@"Last Completed Slope     : {LastResultPercent}%
Vertices Processed       : {VerticesProcessed}
Vertices Skipped         : {VerticesSkipped}
Picked Drain Count       : {PickedDrainCount}
Final Drain Count        : {FinalDrainCount}
Highest Elevation        : {HighestElevationDisplay}
Longest Path             : {LongestPathDisplay}
Run Duration             : {RunDurationDisplay}
Run Date                 : {RunDate}
Export Folder            : {ExportFolderPath}
Circles Placed           : {DrainCirclesPlaced} drain / {HighestCirclesPlaced} highest / {OffsetCirclesPlaced} offset
{CompletionSummaryText}";

        // â”€â”€ Constructor fields â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public UIDocument UIDoc { get; }
        public UIApplication App { get; }
        public ElementId FloorId { get; }
        public List<XYZ> DrainPoints { get; }

        public AutoSlopeViewModel(
            UIDocument uidoc,
            UIApplication app,
            ElementId floorId,
            List<XYZ> drainPoints)
        {
            UIDoc      = uidoc;
            App        = app;
            FloorId     = floorId;
            DrainPoints = drainPoints;

            PickedDrainCount = drainPoints?.Count ?? 0;
            FinalDrainCount  = drainPoints?.Count ?? 0;

            SlopeRows = Enumerable.Range(1, 4).Select(i =>
            {
                var row = new SlopeRowViewModel(i);
                row.Changed += RecalculateLiveMetrics;
                return row;
            }).ToList();

            Element floorElement = uidoc?.Document?.GetElement(floorId);
            SelectedFloorLabel = floorElement != null
                ? $"{floorElement.Category?.Name ?? "Floor"} (Id {floorId.Value})"
                : $"Id {floorId?.Value}";

            ExportFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                AppConstants.DefaultExportFolder);

            LoadLineStyleOptions();
            LoadAllSettings();
            RecalculateLiveMetrics();

            AutoSlopeEventManager.Init();
        }

        // â”€â”€ LoadLineStyleOptions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// Populates LineStyleOptions from the project's existing OST_Lines
        /// subcategories (GraphicsStyle elements) â€” the same list Revit shows
        /// in its own "Line Style" dropdowns. No new line styles are created.
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

            // Sensible default: "Thin Lines" if present (Revit's built-in default
            // line style), otherwise the first available option.
            var defaultOption = LineStyleOptions.FirstOrDefault(o => o.Name == "Thin Lines")
                                 ?? LineStyleOptions.FirstOrDefault();

            if (defaultOption != null)
            {
                foreach (var group in new[] { DrainMarkerGroup, HighestPointMarkerGroup, AllowedOffsetMarkerGroup })
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

        // â”€â”€ LoadAllSettings â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// Applies ALL persisted settings (settings.json) onto the ViewModel:
        /// the 3 Circle Marker groups, the offset threshold, the run-input
        /// fields (slope %, threshold, drain tolerance, curve-intersection
        /// toggle), and the export fields (folder, export-to-Excel,
        /// ask-to-open-after-export). Must run AFTER LoadLineStyleOptions,
        /// since the persisted LineStyleName is matched against the freshly
        /// loaded LineStyleOptions â€” a name from a previous project that no
        /// longer exists here simply leaves the constructor's default line
        /// style in place. Window size/position is intentionally not part
        /// of this scope.
        /// </summary>
        private void LoadAllSettings()
        {
            AutoSlopeSettings settings = SettingsService.Load();

            ApplySettings(DrainMarkerGroup, settings.DrainMarkerGroup);
            ApplySettings(HighestPointMarkerGroup, settings.HighestPointMarkerGroup);
            ApplySettings(AllowedOffsetMarkerGroup, settings.AllowedOffsetMarkerGroup);
            AllowedOffsetThresholdMm = settings.AllowedOffsetThresholdMm;

            for (int i = 0; i < SlopeRows.Count && i < settings.SlopeRows.Count; i++)
            {
                SlopeRows[i].IsEnabled = settings.SlopeRows[i].IsEnabled;
                SlopeRows[i].PercentText = settings.SlopeRows[i].Percent.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }

            ThresholdMeters = settings.ThresholdMeters;
            EnableDrainTolerance = settings.EnableDrainTolerance;
            DrainToleranceMm = settings.DrainToleranceMm;
            InsertCurveIntersectionPoints = settings.InsertCurveIntersectionPoints;

            // Export folder: only override the MyDocuments default set above
            // if a previously-saved path exists (first-run has none).
            if (!string.IsNullOrWhiteSpace(settings.ExportFolderPath))
                ExportFolderPath = settings.ExportFolderPath;

            ExportToExcel = settings.ExportToExcel;
            AskToOpenAfterExport = settings.AskToOpenAfterExport;
        }

        private void ApplySettings(CircleMarkerGroup group, CircleMarkerGroupSettings saved)
        {
            if (group == null || saved == null) return;

            group.IsEnabled = saved.IsEnabled;
            group.ColorName = string.IsNullOrWhiteSpace(saved.ColorName) ? group.ColorName : saved.ColorName;
            group.RadiusMm = saved.RadiusMm > 0 ? saved.RadiusMm : group.RadiusMm;
            group.ShowOffsetText = saved.ShowOffsetText;

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

        // â”€â”€ SaveAllSettings â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// Persists ALL user-changeable fields to settings.json: the 3 marker
        /// groups + offset threshold, the run inputs, and the export fields.
        /// Called after a completed Run and again on window close, so the
        /// last values used â€” whichever came last â€” are what's remembered.
        /// Best-effort â€” failures are logged, not thrown. Public: also
        /// invoked from FloorSlopeByPointMultiCopiesWindow.OnClosing (code-behind).
        /// </summary>
        public void SaveAllSettings()
        {
            var settings = new AutoSlopeSettings
            {
                DrainMarkerGroup = ToSettings(DrainMarkerGroup),
                HighestPointMarkerGroup = ToSettings(HighestPointMarkerGroup),
                AllowedOffsetMarkerGroup = ToSettings(AllowedOffsetMarkerGroup),
                AllowedOffsetThresholdMm = AllowedOffsetThresholdMm,

                SlopeRows = SlopeRows.Select(r => new SlopeRowSetting
                {
                    RowIndex = r.RowIndex,
                    IsEnabled = r.IsEnabled,
                    Percent = r.Percent
                }).ToList(),
                ThresholdMeters = ThresholdMeters,
                EnableDrainTolerance = EnableDrainTolerance,
                DrainToleranceMm = DrainToleranceMm,
                InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,

                ExportFolderPath = ExportFolderPath,
                ExportToExcel = ExportToExcel,
                AskToOpenAfterExport = AskToOpenAfterExport
            };

            if (!SettingsService.Save(settings))
            {
                AddLog(new LogEntry(LogLevel.Warning, "Could not save settings."));
            }
        }

        private static CircleMarkerGroupSettings ToSettings(CircleMarkerGroup group) => new CircleMarkerGroupSettings
        {
            IsEnabled = group.IsEnabled,
            LineStyleName = group.LineStyleName,
            ColorName = group.ColorName,
            RadiusMm = group.RadiusMm,
            ShowOffsetText = group.ShowOffsetText
        };

        // â”€â”€ Run â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            State = RunState.Running;
            LogEntries.Clear();

            // NEW: fresh CancellationTokenSource per Run click. The bar starts
            // indeterminate ("Startingâ€¦") since nothing has reported yet.
            _runCts = new CancellationTokenSource();
            IsProgressVisible = true;
            ProgressPercent = 0;
            ProgressIsIndeterminate = true;
            ProgressPhaseText = "Startingâ€¦";

            AddLog(new LogEntry(LogLevel.Info, "Starting Multi-Slope Floor Variants..."));

            if (ExportToExcel && !Directory.Exists(ExportFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(ExportFolderPath);
                    AddLog(new LogEntry(LogLevel.Info,
                        $"Created export directory: {ExportFolderPath}"));
                }
                catch (Exception ex)
                {
                    AddLog(new LogEntry(LogLevel.Warning,
                        $"Warning: Failed to create export directory: {ex.Message}"));
                }
            }

            AutoSlopeHandler.Payload = new MultiSlopePayload
            {
                FloorId             = FloorId,
                PickedDrainPoints  = DrainPoints,
                DrainPoints        = DrainPoints,
                SlopeRows          = SlopeRows.Select(r => new SlopeRowSetting
                {
                    RowIndex  = r.RowIndex,
                    IsEnabled = r.IsEnabled,
                    Percent   = r.Percent
                }).ToList(),
                ThresholdMeters    = ThresholdMeters,
                EnableDrainTolerance = EnableDrainTolerance,
                DrainToleranceMm   = DrainToleranceMm,
                InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,
                ProjectTitle       = UIDoc?.Document?.Title ?? "Unknown Project",
                WorksetNameFormat  = AppConstants.WorksetNameFormat,
                Log                = AddLog,      // Action<LogEntry>
                ExportConfig       = new ExportConfig
                {
                    ExportPath           = ExportFolderPath,
                    ExportToExcel        = ExportToExcel,
                    IncludeVertexDetails = false
                },
                DrainMarkerGroup         = DrainMarkerGroup,
                HighestPointMarkerGroup  = HighestPointMarkerGroup,
                AllowedOffsetMarkerGroup = AllowedOffsetMarkerGroup,
                AllowedOffsetThresholdMm = AllowedOffsetThresholdMm,

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

                    UiPumpHelper.DoEvents();
                },

                OnCompleted = summary =>
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        IsProgressVisible = false;
                        _runCts = null;

                        if (!summary.Success)
                        {
                            AddLog(new LogEntry(LogLevel.Error,
                                $"Multi-Slope Floor Variants failed: {summary.ErrorMessage}"));
                            State = RunState.Ready;
                            return;
                        }

                        CopiesCreatedCount   = summary.CopiesCreated;
                        WorksetsCreatedCount = summary.WorksetsCreated;
                        FailedCount          = summary.Failed;

                        MultiSlopeVariantResult lastSuccess = summary.Results.LastOrDefault(r => r.Success);
                        if (lastSuccess?.EngineResult != null)
                        {
                            AutoSlopeResult result = lastSuccess.EngineResult;
                            _lastResult        = result;
                            LastResultPercent = lastSuccess.Percent;

                            VerticesProcessed = result.VerticesProcessed;
                            VerticesSkipped   = result.VerticesSkipped;
                            PickedDrainCount  = result.PickedDrainCount;
                            FinalDrainCount   = result.FinalDrainCount;

                            HighestElevation_mm = result.HighestElevation_mm;
                            LongestPath_m       = result.LongestPath_m;
                            RunDuration_sec     = result.RunDuration_sec;
                            RunDate             = result.RunDate;
                            CurvesCalculated    = result.CurvesCalculated;

                            DrainCirclesPlaced   = result.DrainCirclesPlaced;
                            HighestCirclesPlaced = result.HighestCirclesPlaced;
                            OffsetCirclesPlaced  = result.OffsetCirclesPlaced;
                        }

                        SaveAllSettings();
                        RecalculateLiveMetrics();

                        // State stays Cancelled (not Done) when the user cancelled, even
                        // though some rows succeeded â€” the Run button does not get
                        // permanently disabled after a cancel, so the user can retry.
                        State = summary.WasCancelled ? RunState.Cancelled : RunState.Done;
                        ExportResultsCommand.NotifyCanExecuteChanged();

                        if (AskToOpenAfterExport)
                        {
                            string exportedFile = summary.Results
                                .Where(r => r.Success && !string.IsNullOrEmpty(r.EngineResult?.ExportedFilePath))
                                .Select(r => r.EngineResult.ExportedFilePath)
                                .LastOrDefault();

                            if (!string.IsNullOrEmpty(exportedFile))
                            {
                                var tdExport = new TaskDialog("Export Complete");
                                tdExport.MainContent = "Excel file(s) saved. Open the most recent one now?";
                                tdExport.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                                if (tdExport.Show() == TaskDialogResult.Yes)
                                {
                                    try
                                    {
                                        System.Diagnostics.Process.Start(
                                            new System.Diagnostics.ProcessStartInfo
                                            {
                                                FileName        = exportedFile,
                                                UseShellExecute = true
                                            });
                                    }
                                    catch (Exception ex)
                                    {
                                        AddLog(new LogEntry(LogLevel.Warning,
                                            $"âš  Could not open file: {ex.Message}"));
                                    }
                                }
                            }
                        }
                    }));
                }
            };

            AutoSlopeEventManager.Event.Raise();
        }

        private bool CanRun() => !IsRunning && !IsComplete && ActiveSlopeCount > 0;

        partial void OnActiveSlopeCountChanged(int value) => RunCommand.NotifyCanExecuteChanged();

        // â”€â”€ BrowseFolder â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ ClearLog â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [RelayCommand]
        private void ClearLog()
        {
            LogEntries.Clear();
            AddLog(new LogEntry(LogLevel.Info, "Log cleared."));
        }

        // â”€â”€ ExportResults â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [RelayCommand(CanExecute = nameof(CanExportResults))]
        private void ExportResults()
        {
            if (_lastResult == null || !_lastResult.Success)
            {
                AddLog(new LogEntry(LogLevel.Warning, "Warning: Run Floor Slope successfully first."));
                return;
            }

            string filePath = DialogService.ShowSaveFileDialog(
                "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                ExportFolderPath,
                $"FloorSlopeMultiCopies_Results_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            if (string.IsNullOrEmpty(filePath)) return;

            string savedPath = ExcelExportService.ExportResultsSummary(
                filePath,
                _lastResult,
                LastResultPercent,
                ThresholdMeters,
                EnableDrainTolerance,
                DrainToleranceMm,
                ExportFolderPath,
                AddLog);   // Action<LogEntry>

            if (!string.IsNullOrEmpty(savedPath))
            {
                AddLog(new LogEntry(LogLevel.Success, $"âœ… Results exported to: {savedPath}"));

                if (AskToOpenAfterExport)
                {
                    var tdSave = new TaskDialog("Export Complete");
                    tdSave.MainContent = "Excel file saved. Open it now?";
                    tdSave.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                    if (tdSave.Show() == TaskDialogResult.Yes)
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo
                            {
                                FileName        = savedPath,
                                UseShellExecute = true
                            });
                }
            }
        }

        private bool CanExportResults() => IsComplete && _lastResult?.Success == true;

        // â”€â”€ AddLog â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// Thread-safe: if already on the UI thread, adds directly;
        /// otherwise dispatches via BeginInvoke.
        /// </summary>
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

