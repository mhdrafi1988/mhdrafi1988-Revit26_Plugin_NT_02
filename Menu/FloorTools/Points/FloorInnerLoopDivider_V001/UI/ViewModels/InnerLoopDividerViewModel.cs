// =======================================================
// File: InnerLoopDividerViewModel.cs
// Location: UI/ViewModels/
// Changes vs V011:
//   NEW  Division mode: Count (points around each loop) or Spacing (max
//        gap in metres). Every row shows Total / New / Already-there
//        points live, computed by Core/Geometry/DivisionPlanner — the
//        header's "New points" is exactly what Apply will add.
//   NEW  "Reset existing offsets first" (off by default) — replaces the
//        unconditional reset V011's command did on launch.
//   NEW  Status strip: IsRunning while Revit works, SummaryText after.
//   FIX  Event.Raise() result checked; requests aren't lost silently.
//   FIX  Requests carry the floor's own Document.
//   FIX  After Apply the floor's shape points are refreshed from the
//        result, so counts update without losing row edits.
//   ~    Defaults are per loop now: Circular 6, Rectangular 8 (corners +
//        one per side), Other 8. V011's "4 on a rectangle" put 4 points on
//        EVERY side; 4 around a rectangle is just its corners.
//   =    Public surface used by CombinedRoofTools unchanged: constructor,
//        Loops/LoopsView/Log, counts, GlobalDivisionPoints, commands,
//        SelectGroupLoops/ClearGroupLoops, ApplyDivisionCompleted.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Geometry;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Models;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.UI.ViewModels
{
    public partial class InnerLoopDividerViewModel : ObservableObject
    {
        public const int DefaultCircularPoints = 6;
        public const int DefaultRectangularPoints = 8;
        public const int DefaultOtherPoints = 8;
        public const double MinSpacingMeters = 0.05;

        /// <summary>5 mm in feet — matches Infrastructure/Revit/FloorLoopReader.Tolerance.</summary>
        private const double ToleranceFeet = 5.0 / 304.8;

        private readonly UIApplication _app;
        private readonly ElementId _floorId;
        private readonly Document _doc;
        private readonly bool _circlesOnlyByDefault;
        private bool _suppressReplan;

        public ObservableCollection<FloorLoopModel> Loops { get; } = new();
        public ICollectionView LoopsView { get; private set; }
        public ObservableCollection<LogEntry> Log { get; } = new();

        // ── ToolWindowShell status strip ──
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ApplyDivisionCommand))]
        [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
        private bool isRunning;

        [ObservableProperty] private double progress;
        [ObservableProperty] private string summaryText = string.Empty;

        // ── Header counts ──
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLoops))]
        [NotifyPropertyChangedFor(nameof(TotalOpeningsCount))]
        private int innerLoopCount;

        /// <summary>New points Apply will add for the selected loops (V011: the sum of the per-row numbers).</summary>
        [ObservableProperty] private int queuedPointCount;

        /// <summary>Planned points on selected loops that are already shape points (skipped at Apply).</summary>
        [ObservableProperty] private int existingPointCount;

        [ObservableProperty] private int selectedShapeCount;

        public bool HasLoops => InnerLoopCount > 0;
        public int TotalOpeningsCount => InnerLoopCount;

        // ── Division settings ──

        /// <summary>"Set all loops to" — overwrites every row's count; rows stay editable.</summary>
        [ObservableProperty] private int globalDivisionPoints = DefaultCircularPoints;

        /// <summary>False = Count mode (points around each loop), true = Spacing mode.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UseCount))]
        private bool useSpacing;

        public bool UseCount
        {
            get => !UseSpacing;
            set => UseSpacing = !value;
        }

        /// <summary>Spacing mode: largest gap between points along a loop (m).</summary>
        [ObservableProperty] private double spacingMeters = 0.30;

        /// <summary>Set every existing shape point to offset 0 before adding points (same transaction).</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ApplyDivisionCommand))]
        private bool resetOffsetsFirst;

        partial void OnGlobalDivisionPointsChanged(int value)
        {
            _suppressReplan = true;
            foreach (var loop in Loops)
                loop.RecommendedPoints = Math.Max(0, value);
            _suppressReplan = false;
            ReplanAll();
        }

        partial void OnUseSpacingChanged(bool value) => ReplanAll();
        partial void OnSpacingMetersChanged(double value) => ReplanAll();

        public InnerLoopDividerViewModel(UIApplication app, ElementId floorId, List<FloorLoopModel> initialLoops,
                                         bool circlesOnlyByDefault = false)
        {
            _app = app;
            _floorId = floorId;
            _doc = app?.ActiveUIDocument?.Document;
            _circlesOnlyByDefault = circlesOnlyByDefault;

            SetupCollectionView();
            PopulateLoops(initialLoops ?? new List<FloorLoopModel>());

            AddLog(LogLevel.Success, $"Analysis complete — {Loops.Count} opening(s) found.");
            LogShapeBreakdown();

            InnerLoopDividerEventManager.Init();
        }

        /// <summary>Shows analysis notes (unclosed sketch loops, shape editing off …) in the log.</summary>
        public void ReportNotes(IEnumerable<string> notes)
        {
            foreach (string n in notes ?? Enumerable.Empty<string>())
                AddLog(LogLevel.Info, n);
        }

        private void SetupCollectionView()
        {
            LoopsView = CollectionViewSource.GetDefaultView(Loops);
            LoopsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FloorLoopModel.ShapeCategory)));
            LoopsView.SortDescriptions.Add(new SortDescription(nameof(FloorLoopModel.CategoryRank), ListSortDirection.Ascending));
            LoopsView.SortDescriptions.Add(new SortDescription(nameof(FloorLoopModel.Index), ListSortDirection.Ascending));
        }

        private void PopulateLoops(List<FloorLoopModel> loops)
        {
            DetachLoopHandlers();
            Loops.Clear();

            _suppressReplan = true;
            foreach (var loop in loops)
            {
                loop.IsSelected = !_circlesOnlyByDefault || loop.LoopShapeType == "Circular";
                loop.RecommendedPoints = loop.LoopShapeType switch
                {
                    "Circular"  => DefaultCircularPoints,
                    "Rectangle" => DefaultRectangularPoints,
                    _           => DefaultOtherPoints
                };

                loop.PropertyChanged += OnLoopPropertyChanged;
                Loops.Add(loop);
            }
            _suppressReplan = false;

            ReplanAll();
        }

        private void LogShapeBreakdown()
        {
            int circular   = Loops.Count(l => l.LoopShapeType == "Circular");
            int rectangles = Loops.Count(l => l.LoopShapeType == "Rectangle");
            int others     = Loops.Count(l => l.LoopShapeType == "Other");

            AddLog(LogLevel.Info, $"Circular: {circular}  ·  Rectangular: {rectangles}  ·  Other: {others}");
            AddLog(LogLevel.Info,
                $"Defaults (points around each loop, corners included) — Circular: {DefaultCircularPoints}  ·  Rectangular: {DefaultRectangularPoints}  ·  Other: {DefaultOtherPoints}");
        }

        // ── Re-analyze (via ExternalEvent) ───────────────────────────────────
        [RelayCommand(CanExecute = nameof(CanAnalyze))]
        private void Analyze()
        {
            Log.Clear();
            AddLog(LogLevel.Info, "Re-analyzing…");

            var payload = new InnerLoopDividerPayload
            {
                FloorId = _floorId,
                Document = _doc,
                Operation = InnerLoopDividerOperation.Analyze,
                Log = AddLog,
                OnCompleted = result => OnUi(() =>
                {
                    IsRunning = false;
                    if (!result.Success)
                    {
                        SummaryText = "Analysis failed — see log";
                        return;
                    }

                    PopulateLoops(result.Loops);
                    ReportNotes(result.Notes);
                    AddLog(LogLevel.Success, $"Analysis complete — {Loops.Count} opening(s) found.");
                    LogShapeBreakdown();
                    SummaryText = $"{Loops.Count} opening(s) found";
                })
            };

            Raise(payload, "Analyzing floor…");
        }

        private bool CanAnalyze() => !IsRunning;

        /// <summary>Raised after ApplyDivision's round-trip (true = success) — used by CombinedRoofTools "Run All".</summary>
        public event Action<bool> ApplyDivisionCompleted;

        // ── Apply (via ExternalEvent) ─────────────────────────────────────────
        [RelayCommand(CanExecute = nameof(CanApply))]
        private void ApplyDivision()
        {
            var selected = Loops.Where(l => l.IsSelected).ToList();
            int newPoints = selected.Sum(l => l.PlannedNew);
            AddLog(LogLevel.Info,
                $"Applying {newPoints} new point(s) to {selected.Count} loop(s)" +
                (ResetOffsetsFirst ? " — existing offsets will be reset to 0 first…" : "…"));

            var payload = new InnerLoopDividerPayload
            {
                FloorId = _floorId,
                Document = _doc,
                Operation = InnerLoopDividerOperation.Apply,
                SelectedLoops = selected,
                Mode = UseSpacing ? DivisionMode.Spacing : DivisionMode.Count,
                SpacingFeet = SpacingFeet(),
                ResetOffsetsFirst = ResetOffsetsFirst,
                Log = AddLog,
                OnCompleted = result => OnUi(() =>
                {
                    IsRunning = false;
                    if (result.Success)
                    {
                        RefreshVertices(result.VerticesAfter);
                        SummaryText = $"{result.PointsAdded} point(s) added · {result.PointsSkippedExisting} already existed" +
                                      (result.PointsFailed > 0 ? $" · {result.PointsFailed} rejected" : string.Empty);
                        if (result.PointsFailed > 0)
                            AddLog(LogLevel.Info, $"Note: Revit rejected {result.PointsFailed} point(s) — usually a point outside the floor's top surface.");
                    }
                    else
                    {
                        SummaryText = "Apply failed — nothing changed (see log)";
                    }

                    ApplyDivisionCompleted?.Invoke(result.Success);
                })
            };

            if (!Raise(payload, "Adding shape points…"))
                ApplyDivisionCompleted?.Invoke(false);
        }

        private bool CanApply() =>
            !IsRunning && Loops.Any(l => l.IsSelected && (l.PlannedNew > 0 || ResetOffsetsFirst));

        [RelayCommand]
        private void SelectAll() => SetAllSelected(true);

        [RelayCommand]
        private void ClearSelection() => SetAllSelected(false);

        public void SelectGroupLoops(string category)
        {
            foreach (var loop in Loops.Where(l => l.ShapeCategory == category))
                loop.IsSelected = true;
        }

        public void ClearGroupLoops(string category)
        {
            foreach (var loop in Loops.Where(l => l.ShapeCategory == category))
                loop.IsSelected = false;
        }

        [RelayCommand]
        private void ClearLog() => Log.Clear();

        // ── Helpers ───────────────────────────────────────────────────────────
        private bool Raise(InnerLoopDividerPayload payload, string status)
        {
            InnerLoopDividerHandler.Payload = payload;
            ExternalEventRequest request = InnerLoopDividerEventManager.Event.Raise();
            if (request != ExternalEventRequest.Accepted)
            {
                InnerLoopDividerHandler.Payload = null;
                AddLog(LogLevel.Error, $"Revit did not accept the request ({request}). Finish any active command in Revit and try again.");
                SummaryText = "Request not accepted by Revit";
                return false;
            }

            IsRunning = true;
            SummaryText = status;
            return true;
        }

        private double SpacingFeet() => Math.Max(MinSpacingMeters, SpacingMeters) / 0.3048;

        private void ReplanAll()
        {
            if (_suppressReplan) return;
            DivisionMode mode = UseSpacing ? DivisionMode.Spacing : DivisionMode.Count;
            double spacing = SpacingFeet();
            foreach (var loop in Loops)
                loop.UpdatePlan(mode, spacing, ToleranceFeet);
            RecomputeCounts();
        }

        private void RefreshVertices(List<P2> vertices)
        {
            IReadOnlyList<P2> shared = vertices ?? new List<P2>();
            foreach (var loop in Loops)
                loop.ExistingVertices = shared;
            ReplanAll();
        }

        private void SetAllSelected(bool value)
        {
            foreach (var loop in Loops)
                loop.IsSelected = value;
        }

        private void OnLoopPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FloorLoopModel.RecommendedPoints) && sender is FloorLoopModel loop && !_suppressReplan)
            {
                loop.UpdatePlan(UseSpacing ? DivisionMode.Spacing : DivisionMode.Count, SpacingFeet(), ToleranceFeet);
                RecomputeCounts();
            }
            else if (e.PropertyName == nameof(FloorLoopModel.IsSelected))
            {
                RecomputeCounts();
            }
        }

        private void RecomputeCounts()
        {
            InnerLoopCount     = Loops.Count;
            SelectedShapeCount = Loops.Count(l => l.IsSelected);
            QueuedPointCount   = Loops.Where(l => l.IsSelected).Sum(l => l.PlannedNew);
            ExistingPointCount = Loops.Where(l => l.IsSelected).Sum(l => l.PlannedExisting);
            ApplyDivisionCommand.NotifyCanExecuteChanged();
        }

        private void DetachLoopHandlers()
        {
            foreach (var loop in Loops)
                loop.PropertyChanged -= OnLoopPropertyChanged;
        }

        private static void OnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        // ── AddLog (thread-safe) ──────────────────────────────────────────────
        private void AddLog(LogLevel level, string message) => AddLog(new LogEntry(level, message));

        private void AddLog(LogEntry entry) => OnUi(() => Log.Add(entry));
    }
}
