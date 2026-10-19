// =======================================================
// File: RoofPointsAndSlopeViewModel.cs
// Location: UI/ViewModels/
// The step pipeline window. Every option change re-plans steps 1–4 in
// plan (Core/Planning, no Revit call), so the header tiles and the
// opening grid always show exactly what Run will add. Run and Re-analyze
// go through the ExternalEvent. Settings are loaded when the window
// opens and saved when it closes and after each run.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Engine;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Geometry;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Models;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Core.Planning;
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Models;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.UI.ViewModels
{
    /// <summary>View model of the Roof Points &amp; Slope window.</summary>
    public partial class RoofPointsAndSlopeViewModel : ToolViewModelBase
    {
        /// <summary>Settings folder name under %AppData%\Revit26_Plugin.</summary>
        public const string SettingsName = "RoofPointsAndSlope";

        private readonly Document _doc;
        private readonly ElementId _roofId;
        private RoofOutline _outline;
        private RidgeInput _ridgeInput;
        private bool _suspendPlan;

        /// <summary>The roof's openings (grid rows).</summary>
        public ObservableCollection<OpeningModel> Openings { get; } = new();

        /// <summary>"Roof name · Id 123".</summary>
        public string RoofLabel { get; }

        /// <summary>The document's line styles, for the ridge detail lines.</summary>
        public ObservableCollection<string> LineStyleOptions { get; } = new();

        // ── Step 1 ──────────────────────────────────────────────────────────
        [ObservableProperty] private bool innerPointsEnabled;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(UseCount))] private bool useSpacing;
        [ObservableProperty] private int circularPoints;
        [ObservableProperty] private int corneredPoints;
        [ObservableProperty] private double innerSpacingMeters;

        /// <summary>Step 1 count mode (the opposite of <see cref="UseSpacing"/>).</summary>
        public bool UseCount
        {
            get => !UseSpacing;
            set => UseSpacing = !value;
        }

        // ── Step 2 ──────────────────────────────────────────────────────────
        [ObservableProperty] private bool perpendicularEnabled;
        [ObservableProperty] private double groupProximityMm;

        // ── Step 3 ──────────────────────────────────────────────────────────
        [ObservableProperty] private bool midQuarterEnabled;
        [ObservableProperty] private bool addMidpoint;
        [ObservableProperty] private bool addQuarterPoints;
        [ObservableProperty] private bool midBoundary;
        [ObservableProperty] private bool midOpenings;
        [ObservableProperty] private bool midLines;
        [ObservableProperty] private bool midCurves;

        // ── Step 4 ──────────────────────────────────────────────────────────
        [ObservableProperty] private bool gapFillEnabled;
        [ObservableProperty] private double gapIntervalMeters;
        [ObservableProperty] private bool gapBoundary;
        [ObservableProperty] private bool gapOpenings;
        [ObservableProperty] private bool gapLines;
        [ObservableProperty] private bool gapCurves;

        // ── Step 5 ──────────────────────────────────────────────────────────
        [ObservableProperty] private bool ridgeEnabled;
        [ObservableProperty] private double ridgeProximityMm;
        [ObservableProperty] private double ridgeValidationToleranceMm;
        [ObservableProperty] private bool ridgeShapePoints;
        [ObservableProperty] private bool ridgeDetailLines;
        [ObservableProperty] private string ridgeLineStyle;

        // ── Step 6 ──────────────────────────────────────────────────────────
        [ObservableProperty] private bool slopeEnabled;
        [ObservableProperty] private double slopePercent;
        [ObservableProperty] private double maxEdgeMeters;
        [ObservableProperty] private double maxPathMeters;
        [ObservableProperty] private int pathSamples;
        [ObservableProperty] private bool insertCurveIntersectionPoints;
        [ObservableProperty] private bool exportToExcel;
        [ObservableProperty] private string exportFolder;

        // ── Header tiles ────────────────────────────────────────────────────
        [ObservableProperty] private int openingPointsNew;
        [ObservableProperty] private int perpendicularNew;
        [ObservableProperty] private int midQuarterNew;
        [ObservableProperty] private int gapFillNew;
        [ObservableProperty] private string ridgeSummary = "—";
        [ObservableProperty] private int drainCount;

        /// <summary>Why Run is unavailable, or empty when it can run.</summary>
        [ObservableProperty] private string validationText = string.Empty;

        /// <summary>True when the roof has openings.</summary>
        public bool HasOpenings => Openings.Count > 0;

        /// <summary>Creates the view model for one roof.</summary>
        public RoofPointsAndSlopeViewModel(Document doc, ElementId roofId, string roofLabel, RoofOutline outline, RidgeInput ridgeInput)
        {
            _doc = doc;
            _roofId = roofId;
            RoofLabel = roofLabel;
            _ridgeInput = ridgeInput ?? new RidgeInput();
            foreach (string style in _ridgeInput.LineStyles) LineStyleOptions.Add(style);

            LoadSettings(SettingsService<PipelineSettings>.Load(SettingsName).Normalize());
            SetOutline(outline);
            AddLog(LogLevel.Success, $"Roof read: {outline.Boundaries.Count} boundary loop(s), {outline.Openings.Count} opening(s), " +
                                     $"{outline.Edges.Count} edge(s), {outline.ExistingVertices.Count} shape point(s).");
        }

        // ── Settings ↔ properties ───────────────────────────────────────────
        private void LoadSettings(PipelineSettings s)
        {
            _suspendPlan = true;
            InnerPointsEnabled = s.InnerPointsEnabled;
            UseSpacing = s.InnerMode == DivisionMode.Spacing;
            CircularPoints = s.CircularPoints;
            CorneredPoints = s.CorneredPoints;
            InnerSpacingMeters = s.InnerSpacingMeters;
            PerpendicularEnabled = s.PerpendicularEnabled;
            GroupProximityMm = s.GroupProximityMm;
            MidQuarterEnabled = s.MidQuarterEnabled;
            AddMidpoint = s.AddMidpoint;
            AddQuarterPoints = s.AddQuarterPoints;
            MidBoundary = s.MidQuarterScope.Boundary;
            MidOpenings = s.MidQuarterScope.Openings;
            MidLines = s.MidQuarterScope.Lines;
            MidCurves = s.MidQuarterScope.Curves;
            GapFillEnabled = s.GapFillEnabled;
            GapIntervalMeters = s.GapIntervalMeters;
            GapBoundary = s.GapScope.Boundary;
            GapOpenings = s.GapScope.Openings;
            GapLines = s.GapScope.Lines;
            GapCurves = s.GapScope.Curves;
            RidgeEnabled = s.RidgeEnabled;
            RidgeProximityMm = s.RidgeProximityMm;
            RidgeValidationToleranceMm = s.RidgeValidationToleranceMm;
            RidgeShapePoints = s.RidgeShapePoints;
            RidgeDetailLines = s.RidgeDetailLines;
            RidgeLineStyle = LineStyleOptions.Contains(s.RidgeLineStyle) ? s.RidgeLineStyle : LineStyleOptions.FirstOrDefault() ?? string.Empty;
            SlopeEnabled = s.SlopeEnabled;
            SlopePercent = s.SlopePercent;
            MaxEdgeMeters = s.MaxEdgeMeters;
            MaxPathMeters = s.MaxPathMeters;
            PathSamples = s.PathSamples;
            InsertCurveIntersectionPoints = s.InsertCurveIntersectionPoints;
            ExportToExcel = s.ExportToExcel;
            ExportFolder = string.IsNullOrWhiteSpace(s.ExportFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AutoSlope_Reports")
                : s.ExportFolder;
            _suspendPlan = false;
        }

        /// <summary>The current options as a settings object.</summary>
        public PipelineSettings CurrentSettings() => new PipelineSettings
        {
            InnerPointsEnabled = InnerPointsEnabled,
            InnerMode = UseSpacing ? DivisionMode.Spacing : DivisionMode.Count,
            CircularPoints = CircularPoints,
            CorneredPoints = CorneredPoints,
            InnerSpacingMeters = InnerSpacingMeters,
            PerpendicularEnabled = PerpendicularEnabled,
            GroupProximityMm = GroupProximityMm,
            MidQuarterEnabled = MidQuarterEnabled,
            AddMidpoint = AddMidpoint,
            AddQuarterPoints = AddQuarterPoints,
            MidQuarterScope = new EdgeScope { Boundary = MidBoundary, Openings = MidOpenings, Lines = MidLines, Curves = MidCurves },
            GapFillEnabled = GapFillEnabled,
            GapIntervalMeters = GapIntervalMeters,
            GapScope = new EdgeScope { Boundary = GapBoundary, Openings = GapOpenings, Lines = GapLines, Curves = GapCurves },
            RidgeEnabled = RidgeEnabled,
            RidgeProximityMm = RidgeProximityMm,
            RidgeValidationToleranceMm = RidgeValidationToleranceMm,
            RidgeShapePoints = RidgeShapePoints,
            RidgeDetailLines = RidgeDetailLines,
            RidgeLineStyle = RidgeLineStyle ?? string.Empty,
            SlopeEnabled = SlopeEnabled,
            SlopePercent = SlopePercent,
            MaxEdgeMeters = MaxEdgeMeters,
            MaxPathMeters = MaxPathMeters,
            PathSamples = PathSamples,
            InsertCurveIntersectionPoints = InsertCurveIntersectionPoints,
            ExportToExcel = ExportToExcel,
            ExportFolder = ExportFolder ?? string.Empty
        };

        /// <summary>Saves the current options to the settings file.</summary>
        public void SaveSettings() => SettingsService<PipelineSettings>.Save(SettingsName, CurrentSettings());

        /// <summary>Every option change re-plans (the generated property hook covers all of them).</summary>
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            switch (e.PropertyName)
            {
                case nameof(CircularPoints):
                case nameof(CorneredPoints):
                    ApplyDefaultCounts();
                    Replan();
                    break;
                case nameof(OpeningPointsNew):
                case nameof(PerpendicularNew):
                case nameof(MidQuarterNew):
                case nameof(GapFillNew):
                case nameof(RidgeSummary):
                case nameof(DrainCount):
                case nameof(ValidationText):
                case nameof(IsRunning):
                case nameof(Progress):
                case nameof(SummaryText):
                case nameof(UseCount):
                case nameof(HasOpenings):
                    break;
                default:
                    Replan();
                    break;
            }

            if (e.PropertyName == nameof(IsRunning))
            {
                RunCommand.NotifyCanExecuteChanged();
                AnalyzeCommand.NotifyCanExecuteChanged();
            }
        }

        // ── Openings ────────────────────────────────────────────────────────
        private void SetOutline(RoofOutline outline)
        {
            _suspendPlan = true;
            foreach (OpeningModel o in Openings) o.PropertyChanged -= OnOpeningChanged;
            Openings.Clear();

            _outline = outline;
            foreach (OpeningModel o in outline.Openings)
            {
                o.PointCount = DefaultCount(o);
                o.PropertyChanged += OnOpeningChanged;
                Openings.Add(o);
            }

            foreach (string note in outline.Notes)
                AddLog(LogLevel.Info, note);

            _suspendPlan = false;
            OnPropertyChanged(nameof(HasOpenings));
            Replan();
        }

        private int DefaultCount(OpeningModel o) =>
            o.Shape?.Shape == LoopShape.Circular ? Math.Max(1, CircularPoints) : Math.Max(1, CorneredPoints);

        private void ApplyDefaultCounts()
        {
            bool was = _suspendPlan;
            _suspendPlan = true;
            foreach (OpeningModel o in Openings) o.PointCount = DefaultCount(o);
            _suspendPlan = was;
        }

        private void OnOpeningChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(OpeningModel.PlannedInner) or nameof(OpeningModel.PlannedPerpendicular)) return;
            Replan();
        }

        /// <summary>Ticks or unticks one grid column ("Points", "Perpendicular" or "Drain") for every opening.</summary>
        [RelayCommand]
        private void SetColumn(string spec)
        {
            // spec: "Points:1", "Perpendicular:0", "Drain:1"
            string[] parts = (spec ?? string.Empty).Split(':');
            if (parts.Length != 2) return;
            bool value = parts[1] == "1";

            _suspendPlan = true;
            foreach (OpeningModel o in Openings)
            {
                switch (parts[0])
                {
                    case "Points": o.AddInnerPoints = value; break;
                    case "Perpendicular": o.AddPerpendicular = value; break;
                    case "Drain": o.IsDrain = value && o.CanBeDrain; break;
                }
            }

            _suspendPlan = false;
            Replan();
        }

        // ── Planning ────────────────────────────────────────────────────────
        private void Replan()
        {
            if (_suspendPlan || _outline == null) return;

            PipelinePlan plan;
            try
            {
                plan = PointPipelinePlanner.Plan(_outline, CurrentSettings().Normalize());
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Could not plan the points: {ex.Message}");
                return;
            }

            OpeningPointsNew = plan[PointStep.OpeningPoints].Points.Count;
            PerpendicularNew = plan[PointStep.Perpendicular].Points.Count;
            MidQuarterNew = plan[PointStep.MidQuarter].Points.Count;
            GapFillNew = plan[PointStep.GapFill].Points.Count;

            var innerBy = plan[PointStep.OpeningPoints].Points.GroupBy(p => p.OpeningIndex).ToDictionary(g => g.Key, g => g.Count());
            var perpBy = plan[PointStep.Perpendicular].Points.GroupBy(p => p.OpeningIndex).ToDictionary(g => g.Key, g => g.Count());
            foreach (OpeningModel o in Openings)
            {
                o.PlannedInner = innerBy.TryGetValue(o.Index, out int a) ? a : 0;
                o.PlannedPerpendicular = perpBy.TryGetValue(o.Index, out int b) ? b : 0;
            }

            DrainCount = Openings.Count(o => o.IsDrain && o.CanBeDrain);
            RidgeSummary = PlanRidge(out _ridgeError);
            ValidationText = Validate() ?? string.Empty;
            SummaryText = string.IsNullOrEmpty(ValidationText)
                ? $"Ready: {plan.TotalNew} new point(s)" + (RidgeEnabled ? $", ridge {RidgeSummary}" : string.Empty) +
                  (SlopeEnabled ? $", slope to {DrainCount} drain(s)" : string.Empty)
                : ValidationText;
            RunCommand.NotifyCanExecuteChanged();
        }

        private string _ridgeError;

        /// <summary>Step 5 preview: "4 lines · 9 pts", or "—" when off or not possible.</summary>
        private string PlanRidge(out string error)
        {
            error = null;
            if (!RidgeEnabled) return "Off";
            try
            {
                VoronoiRidgeResult ridge = RidgePlanner.Plan(_ridgeInput, Openings.Where(o => o.IsDrain && o.CanBeDrain),
                                                             CurrentSettings().Normalize(), out error);
                if (ridge == null) return "—";
                int points = ridge.ShapePoints.Count + ridge.RidgeEdgeMidPoints.Count;
                return RidgeShapePoints ? $"{ridge.ClippedEdges.Count} lines · {points} pts" : $"{ridge.ClippedEdges.Count} lines";
            }
            catch (Exception ex)
            {
                error = $"the ridge could not be worked out ({ex.Message}).";
                return "—";
            }
        }

        private string Validate()
        {
            if (!InnerPointsEnabled && !PerpendicularEnabled && !MidQuarterEnabled && !GapFillEnabled && !SlopeEnabled)
                return "Tick at least one step.";
            if (InnerPointsEnabled && UseSpacing && InnerSpacingMeters < 0.05)
                return "Step 1: the spacing must be at least 0.05 m.";
            if (GapFillEnabled && GapIntervalMeters < 0.05)
                return "Step 4: the interval must be at least 0.05 m.";
            if (RidgeEnabled)
            {
                if (!RidgeShapePoints && !RidgeDetailLines) return "Step 5: tick shape points and/or detail lines.";
                if (RidgeProximityMm <= 0) return "Step 5: the drain grouping distance must be more than 0 mm.";
                if (!string.IsNullOrEmpty(_ridgeError)) return "Step 5: " + _ridgeError;
            }

            if (SlopeEnabled)
            {
                if (SlopePercent < 0.1 || SlopePercent > 10) return "Step 6: the slope must be 0.1–10 %.";
                if (MaxEdgeMeters < 0.5 || MaxEdgeMeters > 500) return "Step 6: max edge distance must be 0.5–500 m.";
                if (MaxPathMeters < 0.5 || MaxPathMeters > 500) return "Step 6: max path distance must be 0.5–500 m.";
                if (PathSamples < 2) return "Step 6: path samples must be at least 2.";
                if (DrainCount == 0) return "Step 6: tick at least one opening as a drain.";
                if (ExportToExcel && string.IsNullOrWhiteSpace(ExportFolder)) return "Step 6: choose a folder for the Excel report.";
            }

            return null;
        }

        // ── Run ─────────────────────────────────────────────────────────────
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            PipelineSettings settings = CurrentSettings().Normalize();
            List<OpeningModel> drainOpenings = Openings.Where(o => o.IsDrain && o.CanBeDrain).ToList();
            List<DrainItem> drains = drainOpenings.Select(o => o.Drain).ToList();

            if (settings.SlopeEnabled && settings.ExportToExcel)
            {
                try { Directory.CreateDirectory(settings.ExportFolder); }
                catch (Exception ex)
                {
                    AddLog(LogLevel.Error, $"The report folder could not be created: {ex.Message}");
                    return;
                }
            }

            SaveSettings();
            AddLog(LogLevel.Info, $"Run started: {OpeningPointsNew + PerpendicularNew + MidQuarterNew + GapFillNew} planned point(s)" +
                                  (settings.RidgeEnabled ? ", then the ridge" : string.Empty) +
                                  (settings.SlopeEnabled ? $", then slope {settings.SlopePercent} % to {drains.Count} drain(s)." : "."));

            var payload = new RunPayload
            {
                Operation = RunOperation.Run,
                RoofId = _roofId,
                Document = _doc,
                Outline = _outline,
                Settings = settings,
                RidgeInput = _ridgeInput,
                DrainOpenings = drainOpenings,
                SelectedDrains = drains,
                DetectedDrainCount = _outline.DetectedDrainCount,
                Log = entry => AddLog(entry.Level, entry.Message),
                Progress = (pct, text) => { Progress = pct; SummaryText = text; },
                OnCompleted = OnRunCompleted
            };

            Raise(payload, "Running…");
        }

        private bool CanRun() => !IsRunning && string.IsNullOrEmpty(ValidationText);

        private void OnRunCompleted(RunResult result)
        {
            IsRunning = false;
            Progress = 0;
            if (result.Success)
            {
                _outline.ExistingVertices = result.VerticesAfter ?? new List<P2>();
                Replan();
                string ridge = result.RidgeLines > 0 ? $" · {result.RidgeLines} ridge line(s)" : string.Empty;
                string slope = result.Sloped ? $" · sloped, highest point {result.HighestElevationMm:0} mm" : string.Empty;
                SummaryText = $"Done: {result.TotalAdded} point(s) added{ridge}{slope}";
                AddLog(LogLevel.Success, SummaryText + ". One Undo in Revit reverts the whole run.");
            }
            else
            {
                SummaryText = "Run failed — nothing changed (see log)";
            }
        }

        // ── Re-analyze ──────────────────────────────────────────────────────
        [RelayCommand(CanExecute = nameof(CanAnalyze))]
        private void Analyze()
        {
            AddLog(LogLevel.Info, "Re-reading the roof…");
            var payload = new RunPayload
            {
                Operation = RunOperation.Analyze,
                RoofId = _roofId,
                Document = _doc,
                Log = entry => AddLog(entry.Level, entry.Message),
                OnCompleted = result =>
                {
                    IsRunning = false;
                    if (!result.Success || result.Outline == null)
                    {
                        SummaryText = "Re-read failed — see log";
                        return;
                    }

                    if (result.RidgeInput != null) _ridgeInput = result.RidgeInput;
                    SetOutline(result.Outline);
                    AddLog(LogLevel.Success, $"Roof re-read: {result.Outline.Openings.Count} opening(s), {result.Outline.ExistingVertices.Count} shape point(s).");
                }
            };
            Raise(payload, "Reading roof…");
        }

        private bool CanAnalyze() => !IsRunning;

        private void Raise(RunPayload payload, string status)
        {
            RoofPointsAndSlopeHandler.Payload = payload;
            ExternalEventRequest request = RoofPointsAndSlopeEventManager.Event.Raise();
            if (request != ExternalEventRequest.Accepted)
            {
                RoofPointsAndSlopeHandler.Payload = null;
                AddLog(LogLevel.Error, $"Revit did not accept the request ({request}). Finish any active command in Revit and try again.");
                SummaryText = "Request not accepted by Revit";
                return;
            }

            IsRunning = true;
            SummaryText = status;
        }

        /// <summary>Adds a log line (also used by the window).</summary>
        public void Log(LogLevel level, string message) => AddLog(level, message);
    }
}
