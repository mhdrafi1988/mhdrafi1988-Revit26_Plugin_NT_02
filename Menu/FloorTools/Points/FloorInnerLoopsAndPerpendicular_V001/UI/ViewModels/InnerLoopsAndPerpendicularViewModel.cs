// =======================================================
// File: InnerLoopsAndPerpendicularViewModel.cs
// Location: UI/ViewModels/
// Renamed from PerpendicularPointViewModel (V003) for consistency with
// the tool's own name.
//
// CHANGES vs V003:
//   Constructor now takes (UIApplication, ElementId, initial inner loops,
//   outer loop) instead of (Document, Floor) — a modeless window
//   cannot hold a live Document/Floor and call Revit API methods on
//   it directly from a button click. Analyze(), GeneratePerpendicularPoints(),
//   and ApplyPerpendicular() now raise the ExternalEvent instead of
//   calling the services directly; the floor is re-resolved from FloorId
//   inside InnerLoopsAndPerpendicularEngine, which only ever runs inside
//   a valid API context (Command or ExternalEvent handler). The FIRST
//   analysis pass is done by the Command before the window opens — only
//   "Re-analyze" goes through the event.
//   _outerLoop is still cached client-side (its CurveLoop geometry is a
//   plain value type, safe to carry across the ExternalEvent boundary —
//   same as AutoSlopeByDrain's DrainItem.LoopCurves) so Generate doesn't
//   need to re-run Analyze just to get it again.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Core.Models;
using Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Inner Loops And Perpendicular tool.
    /// User selects any inner loop shapes (Circular / Rectangular / Other) from the
    /// datagrid; perpendicular candidates are generated only for rectangular (4-side)
    /// shapes among the selection — non-rectangular selections are logged and skipped.
    /// </summary>
    public partial class InnerLoopsAndPerpendicularViewModel : ObservableObject
    {
        private readonly UIApplication _app;
        private readonly ElementId _floorId;

        private FloorLoopModel _outerLoop;

        public ObservableCollection<FloorLoopModel> Loops { get; } = new();
        public ICollectionView LoopsView { get; private set; }
        public ObservableCollection<PerpendicularPointModel> PerpendicularPoints { get; } = new();
        public ObservableCollection<LogEntry> Log { get; } = new();

        // ── ToolWindowShell status strip (CLAUDE.md v1.1) ──
        [ObservableProperty] private bool   isRunning;
        [ObservableProperty] private double progress;
        [ObservableProperty] private string summaryText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLoops))]
        private int innerLoopCount;

        [ObservableProperty]
        private int selectedShapeCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPerpendicularPoints))]
        private int perpendicularPointCount;

        public bool HasLoops => InnerLoopCount > 0;
        public bool HasPerpendicularPoints => PerpendicularPointCount > 0;

        /// <summary>
        /// Tolerance (mm) used to decide whether an existing SlabShapeVertex sits on the
        /// opening's edge (included in the point pool). User-editable, default 3mm.
        /// </summary>
        [ObservableProperty]
        private double toleranceMm = 3.0;

        /// <summary>
        /// XY proximity (mm) used to cluster pool points into groups before casting
        /// directional perpendicular feet. Default 500mm.
        /// </summary>
        [ObservableProperty]
        private double groupProximityMm = 500.0;

        // When true, only Circular loops start selected; Rectangular/Other start unselected.
        // Opt-in so Combined Floor Tools (which shares this ViewModel) keeps select-all.
        private readonly bool _circlesOnlyByDefault;

        public InnerLoopsAndPerpendicularViewModel(
            UIApplication app, ElementId floorId, List<FloorLoopModel> initialInnerLoops, FloorLoopModel initialOuterLoop,
            bool circlesOnlyByDefault = false)
        {
            _app = app;
            _floorId = floorId;
            _circlesOnlyByDefault = circlesOnlyByDefault;

            SetupCollectionView();
            PopulateLoops(initialInnerLoops, initialOuterLoop);

            AddLog(LogLevel.Success, $"Analysis complete — {Loops.Count} shape(s) found.");
            LogShapeBreakdown();

            InnerLoopsAndPerpendicularEventManager.Init();
        }

        private void SetupCollectionView()
        {
            LoopsView = CollectionViewSource.GetDefaultView(Loops);
            LoopsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FloorLoopModel.ShapeCategory)));
            LoopsView.SortDescriptions.Add(new SortDescription(nameof(FloorLoopModel.CategoryRank), ListSortDirection.Ascending));
            LoopsView.SortDescriptions.Add(new SortDescription(nameof(FloorLoopModel.PerimeterMm), ListSortDirection.Ascending));
        }

        private void PopulateLoops(List<FloorLoopModel> innerLoops, FloorLoopModel outerLoop)
        {
            DetachLoopHandlers();
            Loops.Clear();
            PerpendicularPoints.Clear();

            _outerLoop = outerLoop;

            foreach (var loop in innerLoops)
            {
                loop.IsSelected = !_circlesOnlyByDefault || loop.LoopShapeType == "Circular";
                loop.PropertyChanged += OnLoopPropertyChanged;
                Loops.Add(loop);
            }

            RecomputeCounts();
            GeneratePerpendicularPointsCommand.NotifyCanExecuteChanged();
        }

        private void LogShapeBreakdown()
        {
            int circular   = Loops.Count(l => l.LoopShapeType == "Circular");
            int rectangles = Loops.Count(l => l.LoopShapeType == "Rectangle");
            int others     = Loops.Count(l => l.LoopShapeType == "Other");

            AddLog(LogLevel.Info, $"Circular: {circular}  ·  Rectangular: {rectangles}  ·  Other: {others}");

            if (_outerLoop == null)
                AddLog(LogLevel.Warning, "No outer boundary loop found — perpendicular points unavailable.");
        }

        // ── Re-analyze (via ExternalEvent) ───────────────────────────────────
        [RelayCommand]
        private void Analyze()
        {
            Log.Clear();
            AddLog(LogLevel.Info, "Re-analyzing...");

            InnerLoopsAndPerpendicularHandler.Payload = new InnerLoopsAndPerpendicularPayload
            {
                FloorId = _floorId,
                Operation = InnerLoopsAndPerpendicularOperation.Analyze,
                Log = AddLog,
                OnCompleted = result =>
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!result.Success)
                        {
                            AddLog(LogLevel.Error, $"Analysis failed: {result.ErrorMessage}");
                            return;
                        }

                        PopulateLoops(result.Loops, result.OuterLoop);
                        AddLog(LogLevel.Success, $"Analysis complete — {Loops.Count} shape(s) found.");
                        LogShapeBreakdown();
                    }));
                }
            };

            InnerLoopsAndPerpendicularEventManager.Event.Raise();
        }

        /// <summary>Raised after Generate's/Apply's ExternalEvent round-trip completes
        /// (true = success), so callers outside this ViewModel (e.g. the Combined
        /// Floor Tools "Run All" orchestrator) can await completion without polling.</summary>
        public event Action<bool> GeneratePerpendicularPointsCompleted;
        public event Action<bool> ApplyPerpendicularCompleted;

        // ── Generate (via ExternalEvent) ──────────────────────────────────────
        /// <summary>
        /// Generates perpendicular border-point candidates for every selected shape.
        /// Only rectangular (4-side) shapes among the selection produce valid candidates;
        /// other selected shapes are logged and skipped.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanGenerate))]
        private void GeneratePerpendicularPoints()
        {
            DetachPointHandlers();
            PerpendicularPoints.Clear();

            if (_outerLoop == null)
            {
                AddLog(LogLevel.Error, "Cannot generate perpendicular points — no outer boundary loop available.");
                RecomputePerpendicularCount();
                GeneratePerpendicularPointsCompleted?.Invoke(false);
                return;
            }

            var selectedShapes = Loops.Where(l => l.IsSelected).ToList();
            if (!selectedShapes.Any())
            {
                AddLog(LogLevel.Warning, "No shapes selected — nothing to project.");
                RecomputePerpendicularCount();
                GeneratePerpendicularPointsCompleted?.Invoke(false);
                return;
            }

            InnerLoopsAndPerpendicularHandler.Payload = new InnerLoopsAndPerpendicularPayload
            {
                FloorId = _floorId,
                Operation = InnerLoopsAndPerpendicularOperation.Generate,
                SelectedLoops = selectedShapes,
                OuterLoop = _outerLoop,
                ToleranceMm = ToleranceMm,
                GroupProximityMm = GroupProximityMm,
                Log = AddLog,
                OnCompleted = result =>
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!result.Success)
                        {
                            AddLog(LogLevel.Error, $"Generate failed: {result.ErrorMessage}");
                            RecomputePerpendicularCount();
                            GeneratePerpendicularPointsCompleted?.Invoke(false);
                            return;
                        }

                        foreach (var candidate in result.PerpendicularPoints)
                        {
                            candidate.PropertyChanged += OnPerpendicularPointChanged;
                            PerpendicularPoints.Add(candidate);
                        }

                        RecomputePerpendicularCount();
                        ApplyPerpendicularCommand.NotifyCanExecuteChanged();
                        GeneratePerpendicularPointsCompleted?.Invoke(true);
                    }));
                }
            };

            InnerLoopsAndPerpendicularEventManager.Event.Raise();
        }

        private bool CanGenerate() => Loops.Any(l => l.IsSelected);

        // ── Apply (via ExternalEvent) ─────────────────────────────────────────
        [RelayCommand(CanExecute = nameof(CanApplyPerpendicular))]
        private void ApplyPerpendicular()
        {
            var validPoints = PerpendicularPoints.Where(p => p.IsSelected && p.IsValid).ToList();
            AddLog(LogLevel.Info, $"Applying {validPoints.Count} perpendicular point(s)…");

            InnerLoopsAndPerpendicularHandler.Payload = new InnerLoopsAndPerpendicularPayload
            {
                FloorId = _floorId,
                Operation = InnerLoopsAndPerpendicularOperation.Apply,
                SelectedPoints = validPoints,
                Log = AddLog,
                OnCompleted = result =>
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!result.Success)
                            AddLog(LogLevel.Error, $"Apply failed: {result.ErrorMessage}");

                        ApplyPerpendicularCompleted?.Invoke(result.Success);
                    }));
                }
            };

            InnerLoopsAndPerpendicularEventManager.Event.Raise();
        }

        private bool CanApplyPerpendicular() => PerpendicularPoints.Any(p => p.IsSelected && p.IsValid);

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

        private void SetAllSelected(bool value)
        {
            foreach (var loop in Loops)
                loop.IsSelected = value;
        }

        private void OnLoopPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FloorLoopModel.IsSelected))
            {
                RecomputeCounts();
                GeneratePerpendicularPointsCommand.NotifyCanExecuteChanged();
            }
        }

        private void OnPerpendicularPointChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PerpendicularPointModel.IsSelected))
            {
                RecomputePerpendicularCount();
                ApplyPerpendicularCommand.NotifyCanExecuteChanged();
            }
        }

        private void RecomputeCounts()
        {
            InnerLoopCount     = Loops.Count;
            SelectedShapeCount = Loops.Count(l => l.IsSelected);
        }

        private void RecomputePerpendicularCount()
            => PerpendicularPointCount = PerpendicularPoints.Count(p => p.IsSelected && p.IsValid);

        private void DetachLoopHandlers()
        {
            foreach (var loop in Loops)
                loop.PropertyChanged -= OnLoopPropertyChanged;
        }

        private void DetachPointHandlers()
        {
            foreach (var p in PerpendicularPoints)
                p.PropertyChanged -= OnPerpendicularPointChanged;
        }

        // ── AddLog (thread-safe) ──────────────────────────────────────────────
        private void AddLog(LogLevel level, string message) => AddLog(new LogEntry(level, message));

        private void AddLog(LogEntry entry)
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            if (dispatcher.CheckAccess())
                Log.Add(entry);
            else
                dispatcher.BeginInvoke(new Action(() => Log.Add(entry)));
        }
    }
}
