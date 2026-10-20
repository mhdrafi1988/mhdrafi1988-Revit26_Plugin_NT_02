// =======================================================
// File: RoofPointsAndSlopeViewModel.cs
// Location: UI/ViewModels/
// The step pipeline window. Every option change re-plans steps 1–4 in
// plan (Core/Planning, no Revit call), so the step-list badges and
// the opening grid always show exactly what Run will add. Run and Re-analyze
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
using Revit26_Plugin_Revised.RoofPointsAndSlope.V001.Infrastructure.Export;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Infrastructure.Helpers;

namespace Revit26_Plugin_Revised.RoofPointsAndSlope.V001.UI.ViewModels
{
    /// <summary>View model of the Roof Points &amp; Slope window.</summary>
    public partial class RoofPointsAndSlopeViewModel : ToolViewModelBase
    {
        /// <summary>Settings folder name under %AppData%\Revit26_Plugin.</summary>
        public const string SettingsName = "RoofPointsAndSlope";

        private readonly Document _doc;
        private ElementId _roofId => SelectedRoof?.Id ?? ElementId.InvalidElementId;
        private RoofOutline _outline;
        private RidgeInput _ridgeInput;
        private bool _suspendPlan;
        private PipelinePlan _lastPlan;
        private VoronoiRidgeResult _lastRidge;
        private CancellationTokenSource _runCts;
        private CancellationTokenSource _reachCts;

        /// <summary>The roof's openings (grid rows).</summary>
        public ObservableCollection<OpeningModel> Openings { get; } = new();

        /// <summary>The roofs picked in Revit (the window works on one at a time; Run on All Roofs runs them all).</summary>
        public ObservableCollection<RoofEntry> Roofs { get; } = new();

        /// <summary>The roof shown in the window.</summary>
        [ObservableProperty] private RoofEntry selectedRoof;

        /// <summary>True when more than one roof was picked.</summary>
        public bool HasManyRoofs => Roofs.Count > 1;

        /// <summary>"Roof name · Id 123" of the roof shown.</summary>
        public string RoofLabel => SelectedRoof?.Label ?? string.Empty;

        /// <summary>The document's line styles, for the ridge detail lines.</summary>
        public ObservableCollection<string> LineStyleOptions { get; } = new();

        // ── Before step 1 ───────────────────────────────────────────────────
        [ObservableProperty] private bool clearExistingFirst;
        [ObservableProperty] private double minPointSpacingMm;

        /// <summary>Shown when the roof already has slope or extra points (empty otherwise).</summary>
        [ObservableProperty] private string existingShapeWarning = string.Empty;

        /// <summary>Plan warnings: points skipped for spacing, ridge problems, drain mismatches (empty when none).</summary>
        [ObservableProperty] private string planWarnings = string.Empty;

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
        [ObservableProperty] private bool saveRunReport;

        // ── Presets ─────────────────────────────────────────────────────────
        /// <summary>Saved option sets.</summary>
        public ObservableCollection<SettingsPreset> Presets { get; } = new();

        /// <summary>The preset picked in the list.</summary>
        [ObservableProperty] private SettingsPreset selectedPreset;

        /// <summary>Name typed in the preset box (used by Save).</summary>
        [ObservableProperty] private string presetName = string.Empty;

        // ── Markers ─────────────────────────────────────────────────────────
        /// <summary>True while this tool's temporary markers are shown in Revit.</summary>
        [ObservableProperty] private bool markersShown;

        /// <summary>Result of "Check slope reach" (empty until checked; cleared when the plan changes).</summary>
        [ObservableProperty] private string reachText = string.Empty;

        /// <summary>True while the reach check runs.</summary>
        [ObservableProperty] private bool isCheckingReach;

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

        /// <summary>Creates the view model for one or more roofs (the first is shown).</summary>
        public RoofPointsAndSlopeViewModel(Document doc, IReadOnlyList<RoofEntry> roofs)
        {
            if (roofs == null || roofs.Count == 0) throw new ArgumentException("At least one roof is needed.", nameof(roofs));
            _doc = doc;
            foreach (RoofEntry r in roofs) r.RidgeInput ??= new RidgeInput();
            foreach (string style in roofs[0].RidgeInput.LineStyles) LineStyleOptions.Add(style);

            LoadSettings(SettingsService<PipelineSettings>.Load(SettingsName).Normalize());
            LoadPresets();

            foreach (RoofEntry r in roofs)
            {
                Roofs.Add(r);
                InitialiseRoof(r, roofs.Count > 1);
            }

            OnPropertyChanged(nameof(HasManyRoofs));
            SelectedRoof = Roofs[0];
        }

        /// <summary>Default counts and the read summary for a newly read roof.</summary>
        private void InitialiseRoof(RoofEntry r, bool many)
        {
            foreach (OpeningModel o in r.Outline.Openings) o.PointCount = DefaultCount(o);
            string prefix = many ? r.Label + ": " : string.Empty;
            AddLog(LogLevel.Success, $"{prefix}roof read: {r.Outline.Boundaries.Count} boundary loop(s), {r.Outline.Openings.Count} opening(s), " +
                                     $"{r.Outline.Edges.Count} edge(s), {r.Outline.ExistingVertices.Count} shape point(s).");
            foreach (string note in r.Outline.Notes) AddLog(LogLevel.Info, prefix + note);
            string warning = ExistingShapeText(r.Outline);
            if (!string.IsNullOrEmpty(warning)) AddLog(LogLevel.Warning, prefix + warning);
        }

        partial void OnSelectedRoofChanged(RoofEntry value)
        {
            if (value == null) return;
            _ridgeInput = value.RidgeInput ?? new RidgeInput();
            OnPropertyChanged(nameof(RoofLabel));
            if (MarkersShown) AddLog(LogLevel.Info, "The markers shown belong to the previous roof — Clear markers or show them again.");
            SetOutline(value.Outline);
        }

        // ── Settings ↔ properties ───────────────────────────────────────────
        private void LoadSettings(PipelineSettings s)
        {
            _suspendPlan = true;
            ClearExistingFirst = s.ClearExistingFirst;
            MinPointSpacingMm = s.MinPointSpacingMm;
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
            SaveRunReport = s.SaveRunReport;
            ExportFolder = string.IsNullOrWhiteSpace(s.ExportFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AutoSlope_Reports")
                : s.ExportFolder;
            _suspendPlan = false;
        }

        /// <summary>The current options as a settings object.</summary>
        public PipelineSettings CurrentSettings() => new PipelineSettings
        {
            ClearExistingFirst = ClearExistingFirst,
            MinPointSpacingMm = MinPointSpacingMm,
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
            ExportFolder = ExportFolder ?? string.Empty,
            SaveRunReport = SaveRunReport
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
                case nameof(ExistingShapeWarning):
                case nameof(PlanWarnings):
                case nameof(ReachText):
                case nameof(IsCheckingReach):
                case nameof(SelectedPreset):
                case nameof(PresetName):
                case nameof(MarkersShown):
                case nameof(SelectedRoof):
                case nameof(RoofLabel):
                case nameof(HasManyRoofs):
                    break;
                default:
                    Replan();
                    break;
            }

            if (e.PropertyName == nameof(IsRunning))
            {
                RunCommand.NotifyCanExecuteChanged();
                RunAllCommand.NotifyCanExecuteChanged();
                AnalyzeCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
                CheckReachCommand.NotifyCanExecuteChanged();
            }

            if (e.PropertyName is nameof(IsRunning) or nameof(MarkersShown))
            {
                ShowPlannedPointsCommand.NotifyCanExecuteChanged();
                ShowOpeningCommand.NotifyCanExecuteChanged();
                ClearMarkersCommand.NotifyCanExecuteChanged();
            }

            if (e.PropertyName == nameof(SelectedPreset))
            {
                if (SelectedPreset != null) PresetName = SelectedPreset.Name;
                ApplyPresetCommand.NotifyCanExecuteChanged();
                DeletePresetCommand.NotifyCanExecuteChanged();
            }

            if (e.PropertyName == nameof(PresetName))
                SavePresetCommand.NotifyCanExecuteChanged();

            if (e.PropertyName == nameof(IsCheckingReach))
                CheckReachCommand.NotifyCanExecuteChanged();
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
                o.PropertyChanged += OnOpeningChanged;
                Openings.Add(o);
            }

            ExistingShapeWarning = ExistingShapeText(outline);

            _suspendPlan = false;
            OnPropertyChanged(nameof(HasOpenings));
            Replan();
        }

        /// <summary>"This roof already has …" when it has slope or more points than corners.</summary>
        private static string ExistingShapeText(RoofOutline outline)
        {
            int extra = outline.ExistingVertices.Count - outline.Edges.Count;
            if (outline.ExistingSlopeMm > 1)
                return $"This roof already has slope ({outline.ExistingSlopeMm:0} mm high to low). Running again builds on it — tick \"Clear existing shape points first\" to start from a flat roof.";
            if (extra > 0)
                return $"This roof already has about {extra} shape point(s) besides its corners. Tick \"Clear existing shape points first\" to start clean.";
            return string.Empty;
        }

        private int DefaultCount(OpeningModel o) =>
            o.Shape?.Shape == LoopShape.Circular ? Math.Max(1, CircularPoints) : Math.Max(1, CorneredPoints);

        private void ApplyDefaultCounts()
        {
            bool was = _suspendPlan;
            _suspendPlan = true;
            foreach (OpeningModel o in Roofs.SelectMany(r => r.Outline.Openings)) o.PointCount = DefaultCount(o);
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

            _lastPlan = plan;
            CancelReachCheck();
            ReachText = string.Empty;

            DrainCount = Openings.Count(o => o.IsDrain && o.CanBeDrain);
            RidgeSummary = PlanRidge(out _ridgeError);
            PlanWarnings = BuildPlanWarnings(plan);
            ValidationText = Validate() ?? string.Empty;
            SummaryText = string.IsNullOrEmpty(ValidationText)
                ? $"Ready: {plan.TotalNew} new point(s)" + (RidgeEnabled ? $", ridge {RidgeSummary}" : string.Empty) +
                  (SlopeEnabled ? $", slope to {DrainCount} drain(s)" : string.Empty)
                : ValidationText;
            RunCommand.NotifyCanExecuteChanged();
            RunAllCommand.NotifyCanExecuteChanged();
            CheckReachCommand.NotifyCanExecuteChanged();
        }

        private string _ridgeError;

        /// <summary>Warnings the preview can show before running (items 3, 4 and 11 of the review).</summary>
        private string BuildPlanWarnings(PipelinePlan plan)
        {
            var lines = new List<string>();
            PipelineSettings settings = CurrentSettings().Normalize();

            int tooClose = plan.Steps.Sum(s => s.TooClose);
            if (tooClose > 0)
                lines.Add($"{tooClose} planned point(s) skipped — closer than {settings.MinPointSpacingMm:0} mm to another point.");

            if (RidgeEnabled && _lastRidge != null)
            {
                if (_lastRidge.FailCount > 0)
                    lines.Add($"Ridge: {_lastRidge.FailCount} of {_lastRidge.PassCount + _lastRidge.FailCount} point(s) are not midway between their drains (more than {settings.RidgeValidationToleranceMm:0} mm out).");

                if (RidgeShapePoints && settings.MinPointSpacingMm > 0)
                {
                    double spacing = settings.MinPointSpacingMm / 304.8;
                    List<P2> known = AllPlannedPoints(plan, settings, includeRidge: false);
                    int near = RidgePoints().Count(r => known.Any(k =>
                    {
                        double d = k.DistanceTo(r);
                        return d > PointPipelinePlanner.Tolerance && d < spacing;
                    }));
                    if (near > 0)
                        lines.Add($"Ridge: {near} point(s) fall within {settings.MinPointSpacingMm:0} mm of another point — they can make small bumps.");
                }
            }

            // Drain consistency: every drain must be an opening the ridge and slope steps both know.
            if (RidgeEnabled || SlopeEnabled)
            {
                int unknownToRidge = _ridgeInput.InnerLoops.Count(loop => !Openings.Any(o => SameCentre(o.Loop, loop)));
                if (unknownToRidge > 0)
                    lines.Add($"{unknownToRidge} opening(s) read for the ridge match no opening in the list — the ridge treats them as obstacles.");
                int drainsNotForRidge = Openings.Count(o => o.IsDrain && o.CanBeDrain && !_ridgeInput.InnerLoops.Any(loop => SameCentre(o.Loop, loop)));
                if (RidgeEnabled && drainsNotForRidge > 0)
                    lines.Add($"{drainsNotForRidge} drain(s) were not found by the ridge reader — check those openings in Revit.");
                int notDrainable = Openings.Count(o => !o.CanBeDrain);
                if (SlopeEnabled && notDrainable > 0 && DrainCount < Openings.Count)
                    lines.Add($"{notDrainable} opening(s) were not detected as possible drains by Auto Slope By Drain and can't be ticked.");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static bool SameCentre(Loop2D loop, List<XYZ> polygon)
        {
            if (loop == null || polygon == null || polygon.Count == 0) return false;
            var (min, max) = loop.Bounds;
            double cx = (min.X + max.X) / 2, cy = (min.Y + max.Y) / 2;
            double px = (polygon.Min(p => p.X) + polygon.Max(p => p.X)) / 2, py = (polygon.Min(p => p.Y) + polygon.Max(p => p.Y)) / 2;
            return Math.Abs(cx - px) <= 10.0 / 304.8 && Math.Abs(cy - py) <= 10.0 / 304.8;
        }

        private IEnumerable<P2> RidgePoints() =>
            _lastRidge == null
                ? Enumerable.Empty<P2>()
                : _lastRidge.ShapePoints.Concat(_lastRidge.RidgeEdgeMidPoints).Select(x => new P2(x.X, x.Y));

        /// <summary>Every point the roof will have after steps 1–4 (and the ridge, when asked).</summary>
        private List<P2> AllPlannedPoints(PipelinePlan plan, PipelineSettings settings, bool includeRidge)
        {
            var all = new List<P2>(PointPipelinePlanner.StartingPoints(_outline, settings));
            foreach (StepPlan step in plan.Steps) all.AddRange(step.Points.Select(p => p.Position));
            if (includeRidge && RidgeEnabled && RidgeShapePoints) all.AddRange(RidgePoints());
            return all;
        }

        /// <summary>Step 5 preview: "4 lines · 9 pts", or "—" when off or not possible.</summary>
        private string PlanRidge(out string error)
        {
            error = null;
            _lastRidge = null;
            if (!RidgeEnabled) return "Off";
            try
            {
                _lastRidge = null;
                VoronoiRidgeResult ridge = RidgePlanner.Plan(_ridgeInput, Openings.Where(o => o.IsDrain && o.CanBeDrain),
                                                             CurrentSettings().Normalize(), out error);
                if (ridge == null) return "—";
                _lastRidge = ridge;
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
            if (MinPointSpacingMm < 0 || MinPointSpacingMm > 5000)
                return "Minimum point spacing must be 0–5000 mm.";
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

            if (SaveRunReport && string.IsNullOrWhiteSpace(ExportFolder)) return "Choose a folder for the run report.";

            return null;
        }

        // ── Run ─────────────────────────────────────────────────────────────
        /// <summary>Runs the ticked steps on the roof shown.</summary>
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run() => StartRun(new List<RoofEntry> { SelectedRoof });

        /// <summary>Runs the ticked steps on every roof, each with its own opening ticks, as one Undo step.</summary>
        [RelayCommand(CanExecute = nameof(CanRunAll))]
        private void RunAll() => StartRun(Roofs.ToList());

        private bool CanRunAll() => HasManyRoofs && CanRun();

        private static List<OpeningModel> DrainsOf(RoofEntry r) => r.Outline.Openings.Where(o => o.IsDrain && o.CanBeDrain).ToList();

        private void StartRun(List<RoofEntry> roofs)
        {
            if (roofs.Count == 0 || roofs.Any(r => r == null)) return;
            PipelineSettings settings = CurrentSettings().Normalize();
            bool many = roofs.Count > 1;

            // Each roof must pass the checks the window shows for the roof on screen.
            if (many)
            {
                var problems = new List<string>();
                foreach (RoofEntry r in roofs)
                {
                    List<OpeningModel> d = DrainsOf(r);
                    if (settings.SlopeEnabled && d.Count == 0)
                        problems.Add($"{r.Label}: no opening is ticked as a drain (step 6).");
                    else if (settings.RidgeEnabled && RidgePlanner.Plan(r.RidgeInput, d, settings, out string ridgeError) == null)
                        problems.Add($"{r.Label}: step 5 — {ridgeError}");
                }

                if (problems.Count > 0)
                {
                    foreach (string p in problems) AddLog(LogLevel.Error, p);
                    SummaryText = "Not run — fix the roofs listed in the log";
                    MessageBox.Show("Some roofs can't run with these settings:\n\n" + string.Join("\n", problems) +
                                    "\n\nPick each roof in the list to fix its openings, or turn the step off.",
                                    "Roof Points & Slope", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if ((settings.SlopeEnabled && settings.ExportToExcel) || settings.SaveRunReport)
            {
                try { Directory.CreateDirectory(settings.ExportFolder); }
                catch (Exception ex)
                {
                    AddLog(LogLevel.Error, $"The report folder could not be created: {ex.Message}");
                    return;
                }
            }

            List<RoofEntry> sloped = roofs.Where(r => r.Outline.ExistingSlopeMm > 1).ToList();
            if (!settings.ClearExistingFirst && sloped.Count > 0)
            {
                string which = many
                    ? $"{sloped.Count} of the roofs already have slope ({string.Join(", ", sloped.Select(r => r.Label))})."
                    : $"This roof already has slope ({sloped[0].Outline.ExistingSlopeMm:0} mm high to low).";
                MessageBoxResult answer = MessageBox.Show(
                    which + "\n\n" +
                    "Running again builds on the current shape. To start from a flat roof, choose No and tick " +
                    "\"Clear existing shape points first\".\n\nRun anyway?",
                    "Roof Points & Slope", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }

            SaveSettings();
            MarkersShown = false; // the handler clears them before running
            _runSettings = settings;
            _runRoofs = roofs;
            AddLog(LogLevel.Info, many
                ? $"Run started on {roofs.Count} roofs, one Undo step" +
                  (settings.RidgeEnabled ? ", with the ridge" : string.Empty) +
                  (settings.SlopeEnabled ? $", slope {settings.SlopePercent} %." : ".")
                : $"Run started: {OpeningPointsNew + PerpendicularNew + MidQuarterNew + GapFillNew} planned point(s)" +
                  (settings.RidgeEnabled ? ", then the ridge" : string.Empty) +
                  (settings.SlopeEnabled ? $", then slope {settings.SlopePercent} % to {DrainsOf(roofs[0]).Count} drain(s)." : "."));

            CancellationToken token = (_runCts = new CancellationTokenSource()).Token;
            var perRoof = new List<RunPayload>();
            for (int i = 0; i < roofs.Count; i++)
            {
                RoofEntry r = roofs[i];
                int index = i;
                string prefix = many ? r.Label + " · " : string.Empty;
                List<OpeningModel> drainOpenings = DrainsOf(r);
                perRoof.Add(new RunPayload
                {
                    Operation = RunOperation.Run,
                    RoofId = r.Id,
                    RoofLabel = r.Label,
                    Document = _doc,
                    Outline = r.Outline,
                    Settings = settings.Clone(),
                    RidgeInput = r.RidgeInput,
                    DrainOpenings = drainOpenings,
                    SelectedDrains = drainOpenings.Select(o => o.Drain).ToList(),
                    DetectedDrainCount = r.Outline.DetectedDrainCount,
                    Cancel = token,
                    Log = entry => AddLog(entry.Level, prefix + entry.Message),
                    Progress = (pct, text) =>
                    {
                        Progress = (index * 100 + pct) / roofs.Count;
                        SummaryText = prefix + text;
                        UiPumpHelper.DoEvents(); // repaint + let a Cancel click through
                    }
                });
            }

            var payload = new RunPayload
            {
                Operation = RunOperation.Run,
                RoofId = roofs[0].Id,
                Document = _doc,
                Roofs = perRoof,
                Log = entry => AddLog(entry.Level, entry.Message),
                OnCompleted = OnRunCompleted
            };

            Raise(payload, many ? $"Running on {roofs.Count} roofs…" : "Running…");
        }

        private List<RoofEntry> _runRoofs = new();

        private bool CanRun() => !IsRunning && string.IsNullOrEmpty(ValidationText);

        private void OnRunCompleted(RunResult result)
        {
            IsRunning = false;
            Progress = 0;
            _runCts?.Dispose();
            _runCts = null;
            if (result.Success)
            {
                var done = new List<(RoofEntry Roof, RunResult Result)>();
                foreach (RunResult r in result.PerRoof)
                {
                    RoofEntry roof = _runRoofs.FirstOrDefault(x => x.Id == r.RoofId);
                    if (roof == null) continue;
                    roof.Outline.ExistingVertices = r.VerticesAfter ?? new List<P2>();
                    if (r.Sloped) roof.Outline.ExistingSlopeMm = r.HighestElevationMm;
                    done.Add((roof, r));
                }

                ExistingShapeWarning = ExistingShapeText(_outline);
                Replan();
                string roofs = done.Count > 1 ? $" on {done.Count} roofs" : string.Empty;
                string ridge = result.RidgeLines > 0 ? $" · {result.RidgeLines} ridge line(s)" : string.Empty;
                string slope = result.Sloped ? $" · sloped, highest point {result.HighestElevationMm:0} mm" : string.Empty;
                SummaryText = $"Done: {result.TotalAdded} point(s) added{roofs}{ridge}{slope}";
                AddLog(LogLevel.Success, SummaryText + ". One Undo in Revit reverts the whole run.");
                if (_runSettings?.SaveRunReport == true)
                    WriteReport(done, _runSettings);
            }
            else
            {
                SummaryText = result.WasCancelled ? "Cancelled — nothing changed" : "Run failed — nothing changed (see log)";
            }
        }

        private PipelineSettings _runSettings;

        /// <summary>Saves the run report; a failure is logged, the run itself stays.</summary>
        private void WriteReport(List<(RoofEntry Roof, RunResult Result)> done, PipelineSettings s)
        {
            if (done.Count == 0) return;
            var report = new RunReport { Roof = done.Count == 1 ? done[0].Roof.Label : $"{done.Count} roofs", Date = DateTime.Now };
            report.Summary.Add(("Tool version", ToolCatalog.RoofPointsAndSlope.Version));
            if (s.SlopeEnabled) report.Summary.Add(("Slope (%)", s.SlopePercent.ToString("0.###")));
            report.Summary.Add(("Min spacing (mm)", s.MinPointSpacingMm.ToString("0")));
            report.Summary.Add(("Cleared existing points first", s.ClearExistingFirst ? "Yes" : "No"));

            foreach (var (roof, result) in done)
            {
                string p = done.Count > 1 ? roof.Label + " · " : string.Empty;
                if (done.Count > 1) report.Summary.Add((string.Empty, string.Empty));
                foreach (var kv in result.AddedPerStep) report.Summary.Add(($"{p}Points added · {kv.Key}", kv.Value.ToString()));
                report.Summary.Add(($"{p}Points added · total", result.TotalAdded.ToString()));
                report.Summary.Add(($"{p}Points rejected by Revit", result.TotalRejected.ToString()));
                report.Summary.Add(($"{p}Ridge detail lines", result.RidgeLines.ToString()));
                report.Summary.Add(($"{p}Sloped", result.Sloped ? "Yes" : "No"));
                if (result.Sloped)
                {
                    report.Summary.Add(($"{p}Drains", DrainsOf(roof).Count.ToString()));
                    report.Summary.Add(($"{p}Highest point (mm)", result.HighestElevationMm.ToString("0")));
                }

                List<ReportPoint> points = result.ReportPoints ?? new List<ReportPoint>();
                report.Summary.Add(($"{p}Shape points after the run", points.Count.ToString()));
                report.Points.AddRange(points.Select(x => new ReportPoint
                {
                    Roof = roof.Label, Step = x.Step, XMeters = x.XMeters, YMeters = x.YMeters, HeightMm = x.HeightMm
                }));
            }

            try
            {
                string path = RunReportWriter.Write(report, s.ExportFolder);
                AddLog(LogLevel.Success, $"Run report saved: {path}");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"The run report could not be saved ({ex.Message}). The run itself is kept.");
            }
        }

        // ── Presets ─────────────────────────────────────────────────────────
        private void LoadPresets()
        {
            PresetStore store = PresetStore.Load(SettingsName, out string error);
            if (error != null) AddLog(LogLevel.Warning, error);
            Presets.Clear();
            foreach (SettingsPreset p in store.Presets) Presets.Add(p);
        }

        private string SavePresets()
        {
            var store = new PresetStore();
            store.Presets.AddRange(Presets);
            return store.Save(SettingsName);
        }

        /// <summary>Loads the picked preset's options into the window.</summary>
        [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
        private void ApplyPreset()
        {
            if (SelectedPreset == null) return;
            LoadSettings(SelectedPreset.Settings.Clone().Normalize());
            ApplyDefaultCounts();
            Replan();
            AddLog(LogLevel.Info, $"Preset \"{SelectedPreset.Name}\" applied.");
        }

        /// <summary>Saves the current options under the typed name (replacing a preset with that name).</summary>
        [RelayCommand(CanExecute = nameof(CanSavePreset))]
        private void SavePreset()
        {
            string name = (PresetName ?? string.Empty).Trim();
            if (name.Length == 0) return;
            SettingsPreset existing = Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null &&
                MessageBox.Show($"Replace the preset \"{existing.Name}\" with the current options?", "Roof Points & Slope",
                                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            var preset = new SettingsPreset { Name = name, Settings = CurrentSettings().Normalize() };
            if (existing != null) Presets[Presets.IndexOf(existing)] = preset;
            else Presets.Add(preset);
            SelectedPreset = preset;

            string error = SavePresets();
            AddLog(error == null ? LogLevel.Success : LogLevel.Error, error ?? $"Preset \"{name}\" saved.");
        }

        /// <summary>Deletes the picked preset.</summary>
        [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
        private void DeletePreset()
        {
            SettingsPreset p = SelectedPreset;
            if (p == null) return;
            if (MessageBox.Show($"Delete the preset \"{p.Name}\"?", "Roof Points & Slope",
                                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            Presets.Remove(p);
            SelectedPreset = null;
            PresetName = string.Empty;
            string error = SavePresets();
            AddLog(error == null ? LogLevel.Info : LogLevel.Error, error ?? $"Preset \"{p.Name}\" deleted.");
        }

        private bool HasSelectedPreset() => SelectedPreset != null;
        private bool CanSavePreset() => !string.IsNullOrWhiteSpace(PresetName);

        // ── Temporary markers in Revit ──────────────────────────────────────
        /// <summary>Shows every planned new point (steps 1–4, and ridge points) as coloured markers in the active view.</summary>
        [RelayCommand(CanExecute = nameof(CanShowMarkers))]
        private void ShowPlannedPoints()
        {
            if (_lastPlan == null) return;
            var markers = new List<MarkerPoint>();
            var kinds = new Dictionary<PointStep, MarkerKind>
            {
                [PointStep.OpeningPoints] = MarkerKind.OpeningPoint,
                [PointStep.Perpendicular] = MarkerKind.Perpendicular,
                [PointStep.MidQuarter] = MarkerKind.MidQuarter,
                [PointStep.GapFill] = MarkerKind.GapFill
            };
            foreach (StepPlan step in _lastPlan.Steps)
                markers.AddRange(step.Points.Select(p => new MarkerPoint(p.Position, kinds[step.Step])));
            if (RidgeEnabled && RidgeShapePoints)
                markers.AddRange(RidgePoints().Select(p => new MarkerPoint(p, MarkerKind.Ridge)));

            if (markers.Count == 0)
            {
                AddLog(LogLevel.Info, "No new points are planned, so there is nothing to show.");
                return;
            }

            RaiseMarkers(markers, null, $"{markers.Count} planned point(s)");
        }

        /// <summary>Highlights one opening (ring markers round its edge) and zooms to it.</summary>
        [RelayCommand(CanExecute = nameof(CanShowMarkers))]
        private void ShowOpening(OpeningModel opening)
        {
            if (opening?.Loop == null) return;
            IReadOnlyList<P2> samples = opening.Loop.Samples;
            int stride = Math.Max(1, samples.Count / 24);
            var markers = samples.Where((_, i) => i % stride == 0).Select(p => new MarkerPoint(p, MarkerKind.Opening)).ToList();
            RaiseMarkers(markers, opening.Loop.Bounds, $"opening {opening.Index}");
        }

        /// <summary>Removes the markers from the view.</summary>
        [RelayCommand(CanExecute = nameof(CanClearMarkers))]
        private void ClearMarkers()
        {
            var payload = new RunPayload
            {
                Operation = RunOperation.ClearMarkers,
                RoofId = _roofId,
                Document = _doc,
                Log = entry => AddLog(entry.Level, entry.Message),
                OnCompleted = _ => { IsRunning = false; MarkersShown = false; SummaryText = "Markers cleared"; }
            };
            Raise(payload, "Clearing markers…");
        }

        /// <summary>Called when the window closes: removes the markers without waiting for the result.</summary>
        public void ClearMarkersOnClose()
        {
            if (!MarkersShown || IsRunning) return;
            MarkersShown = false;
            RoofPointsAndSlopeHandler.Payload = new RunPayload { Operation = RunOperation.ClearMarkers, RoofId = _roofId, Document = _doc };
            RoofPointsAndSlopeEventManager.Event?.Raise();
        }

        private bool CanShowMarkers() => !IsRunning;
        private bool CanClearMarkers() => !IsRunning && MarkersShown;

        private void RaiseMarkers(List<MarkerPoint> markers, (P2 Min, P2 Max)? zoom, string what)
        {
            var payload = new RunPayload
            {
                Operation = RunOperation.ShowMarkers,
                RoofId = _roofId,
                Document = _doc,
                Markers = markers,
                ZoomTo = zoom,
                Log = entry => AddLog(entry.Level, entry.Message),
                OnCompleted = result =>
                {
                    IsRunning = false;
                    if (!result.Success) { SummaryText = "Markers not shown — see log"; return; }
                    MarkersShown = result.MarkersShown > 0;
                    string capped = result.MarkersShown < markers.Count ? $" (first {result.MarkersShown} of {markers.Count})" : string.Empty;
                    SummaryText = $"Showing {what}{capped} in the active view";
                    AddLog(LogLevel.Info, SummaryText + " — temporary markers, not saved; cleared on Run or Close.");
                }
            };
            Raise(payload, "Showing markers…");
        }

        /// <summary>Stops the running steps; the whole run is rolled back.</summary>
        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel()
        {
            if (_runCts == null || _runCts.IsCancellationRequested) return;
            _runCts.Cancel();
            SummaryText = "Cancelling…";
            AddLog(LogLevel.Warning, "Cancel requested — stopping and rolling back.");
            CancelCommand.NotifyCanExecuteChanged();
        }

        private bool CanCancel() => IsRunning && _runCts != null && !_runCts.IsCancellationRequested;

        // ── Slope reach estimate ────────────────────────────────────────────
        /// <summary>
        /// Estimates, before running, how many points the slope step would leave at 0 (dips):
        /// points with no on-roof path to a drain within Max edge hops, or a path longer than Max path.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanCheckReach))]
        private async Task CheckReach()
        {
            if (_outline == null || _lastPlan == null) return;
            PipelineSettings settings = CurrentSettings().Normalize();
            List<P2> points = AllPlannedPoints(_lastPlan, settings, includeRidge: true);
            if (points.Count > ReachEstimator.MaxPoints)
            {
                ReachText = $"Too many points to check ({points.Count}; the limit is {ReachEstimator.MaxPoints}).";
                return;
            }

            var drains = Openings.Where(o => o.IsDrain && o.CanBeDrain).Select(o => o.Loop).ToList();
            var openings = Openings.Select(o => o.Loop).ToList();
            var boundaries = _outline.Boundaries.ToList();
            double maxEdge = settings.MaxEdgeMeters / 0.3048, maxPath = settings.MaxPathMeters / 0.3048;

            CancelReachCheck();
            var cts = _reachCts = new CancellationTokenSource();
            IsCheckingReach = true;
            ReachText = $"Checking {points.Count} point(s)…";
            try
            {
                ReachEstimate est = await Task.Run(() =>
                    ReachEstimator.Estimate(points, drains, openings, boundaries, maxEdge, maxPath, cts.Token), cts.Token);
                if (cts.IsCancellationRequested) return;

                if (est.DrainPointCount == 0)
                    ReachText = "No point lies on a drain's edge — the slope step would leave every point at 0.";
                else if (est.LeftAtZero == 0)
                    ReachText = $"All {est.PointCount} point(s) reach a drain — no dips expected.";
                else
                    ReachText = $"{est.LeftAtZero} of {est.PointCount} point(s) would stay at 0 (dips): " +
                                $"{est.NoPath.Count} with no path to a drain, {est.BeyondMaxPath.Count} beyond Max path. " +
                                "Add points (steps 3–4) or raise Max edge / Max path.";
                AddLog(est.LeftAtZero == 0 ? LogLevel.Success : LogLevel.Warning, "Slope reach check: " + ReachText);
            }
            catch (OperationCanceledException)
            {
                // the plan changed; the text was already cleared
            }
            catch (Exception ex)
            {
                ReachText = "The reach check failed: " + ex.Message;
                AddLog(LogLevel.Error, ReachText);
            }
            finally
            {
                if (ReferenceEquals(_reachCts, cts)) { _reachCts = null; IsCheckingReach = false; }
                cts.Dispose();
            }
        }

        private bool CanCheckReach() => !IsRunning && !IsCheckingReach && SlopeEnabled && DrainCount > 0;

        private void CancelReachCheck()
        {
            if (_reachCts == null) return;
            _reachCts.Cancel();
            _reachCts = null;
            IsCheckingReach = false;
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

                    RoofEntry roof = SelectedRoof;
                    if (roof == null) return;
                    if (result.RidgeInput != null) roof.RidgeInput = _ridgeInput = result.RidgeInput;
                    roof.Outline = result.Outline;
                    InitialiseRoof(roof, HasManyRoofs);
                    SetOutline(roof.Outline);
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
