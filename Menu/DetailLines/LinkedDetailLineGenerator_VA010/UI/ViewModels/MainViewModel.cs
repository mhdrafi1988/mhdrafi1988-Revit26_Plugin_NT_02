using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Engine;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Models;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Services;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.UI.ViewModels
{
    /// <summary>
    /// VA010 — inherits ToolViewModelBase (log, IsRunning, Progress, SummaryText).
    /// Split into three partial files:
    ///   MainViewModel.cs       — core fields, constructor, settings, run state
    ///   MainViewModel.Tree.cs  — linked models + element tree management
    ///   MainViewModel.Run.cs   — run command, processing callback, mapping grid
    /// </summary>
    public partial class MainViewModel : ToolViewModelBase
    {
        private readonly UIApplication _uiApp;
        private readonly Element _selectedBoundaryElement;
        private readonly SettingsService _settingsService = new();
        private readonly LogExportService _logExportService = new();
        private readonly LinkService _linkService = new();
        private readonly ViewBoundaryService _viewBoundaryService = new();
        private readonly ElementBoundaryService _elementBoundaryService = new();
        private readonly DetailLineStyleService _lineStyleService = new();
        private readonly GraphicsOverrideService _graphicsOverrideService = new();

        private readonly CreateDetailLinesEventHandler _eventHandler = new();
        private readonly ExternalEvent _externalEvent;

        // Runs Pick Line's PickObject in Revit's API context (a modeless window's click is outside it).
        private readonly Revit26_Plugin_Revised.Shared.Services.RevitApiQueue _pickQueue;

        // ── VA007: pre-selected Floor/Roof header info (set once, never changes) ──
        public long SelectedElementId { get; }
        public string SelectedElementName { get; }
        public string SelectedElementCategory { get; }
        public string SelectedElementLevel { get; }

        /// <summary>Set by MainWindow's constructor (SetOwnerWindow) — hidden/shown
        /// around PickAxisLine's Selection.PickObject call.</summary>
        private Window? _ownerWindow;

        public void SetOwnerWindow(Window window) => _ownerWindow = window;

        // ── Header / Settings state ──────────────────────────────────────
        [ObservableProperty]
        private bool _settingsLoaded;

        // ── Run state (drives gray-out/inactive UI) ──────────────────────
        [ObservableProperty]
        private ToolRunState _runState = ToolRunState.Idle;

        /// <summary>True once RunState == Complete — bound to the form's IsEnabled
        /// (inverted) to produce the gray/inactive post-run state.</summary>
        public bool IsFormLocked => RunState == ToolRunState.Complete;

        // ── Metrics (Section: top metrics row) ────────────────────────────
        [ObservableProperty] private int _elementsFound;
        [ObservableProperty] private int _elementsProcessed;
        [ObservableProperty] private int _detailLinesCreated;
        [ObservableProperty] private int _filledRegionsCreated;
        [ObservableProperty] private int _elementsSkipped;
        [ObservableProperty] private int _criticalErrors;

        // ── VA007 Processing Scope toggle (default ON) ────────────────────
        [ObservableProperty]
        private bool _restrictToBoundary = true;

        public string ScopeHelpText => RestrictToBoundary
            ? "On — Links and Categories below list only elements found inside the selected Floor/Roof's boundary."
            : "Off — boundary restriction removed. Links and Categories list whatever is currently visible in the active view.";

        public string ProcessingBoundaryDisplay => RestrictToBoundary
            ? $"Selected {SelectedElementCategory}'s own boundary"
            : "Active Crop / Visible Region";

        partial void OnRestrictToBoundaryChanged(bool value)
        {
            OnPropertyChanged(nameof(ScopeHelpText));
            OnPropertyChanged(nameof(ProcessingBoundaryDisplay));
            AddLog(LogLevel.Info, value
                ? "Restrict to boundary: ON — rebuilding element tree against the selected element's own boundary."
                : "Restrict to boundary: OFF — rebuilding element tree against the active view's crop/extent.");
            RebuildElementTree();
        }

        // ── Section 4a/4b: Point Marker settings ───────────────────────
        public CircleMarkerSettings CircleMarker { get; } = new();
        public RectangleMarkerSettings RectangleMarker { get; } = new();
        public ActualProfileSettings ActualProfile { get; } = new();

        // ── Section 5: Processing Scope ─────────────────────────────────
        public ProcessingScope ProcessingScope { get; } = new();

        // ── Section 6: Complex Curve Handling ───────────────────────────
        public ComplexCurveSettings ComplexCurve { get; } = new();

        // ── Current view / scope info line ──────────────────────────────
        [ObservableProperty]
        private string _currentViewName = string.Empty;

        public MainViewModel(UIApplication uiApp, Element selectedBoundaryElement)
        {
            _uiApp = uiApp;
            _selectedBoundaryElement = selectedBoundaryElement;

            SelectedElementId = selectedBoundaryElement.Id.Value;
            SelectedElementName = selectedBoundaryElement.Name ?? "(unnamed)";
            SelectedElementCategory = selectedBoundaryElement.Category?.Name ?? "Unknown";
            SelectedElementLevel = (selectedBoundaryElement.Document.GetElement(selectedBoundaryElement.LevelId) as Level)?.Name ?? "—";

            _externalEvent = ExternalEvent.Create(_eventHandler);
            _pickQueue = new Revit26_Plugin_Revised.Shared.Services.RevitApiQueue("Detail Lines From Links VA010 — Pick Line");
            _eventHandler.OnComplete = OnProcessingComplete;

            MappingsGrouped = CollectionViewSource.GetDefaultView(Mappings);
            MappingsGrouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ElementMapping.Group)));

            LinkedModelsView = CollectionViewSource.GetDefaultView(LinkedModels);
            LinkedModelsView.Filter = FilterLinkedModel;
            LinkedModelsView.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(LinkedModelItem.DocumentTitle), System.ComponentModel.ListSortDirection.Ascending));

            LoadSettings();
            LoadLiveData();

            Mappings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TotalMappingCount));
            LinkedModels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectedLinkCount));

            RunState = ToolRunState.Configuring;
        }

        // ── Run state notifications ──────────────────────────────────────
        partial void OnRunStateChanged(ToolRunState value)
        {
            IsRunning = value == ToolRunState.Running;
            OnPropertyChanged(nameof(IsFormLocked));
            CreateDetailLinesCommand.NotifyCanExecuteChanged();
            ClearCommand.NotifyCanExecuteChanged();
        }

        // ── Settings load/save ──────────────────────────────────────────

        private void LoadSettings()
        {
            var settings = _settingsService.Load(msg => AddLog(LogLevel.Info, msg));

            CircleMarker.DiameterMm = settings.CircleMarker.DiameterMm;

            RectangleMarker.WidthMm = settings.RectangleMarker.WidthMm;
            RectangleMarker.HeightMm = settings.RectangleMarker.HeightMm;
            RectangleMarker.AlignmentMode = Enum.TryParse<RectangleAlignmentMode>(
                settings.RectangleMarker.AlignmentMode, out var alignment) ? alignment : RectangleAlignmentMode.InstanceRotation;
            RectangleMarker.ManualAngleDegrees = settings.RectangleMarker.ManualAngleDegrees;

            ActualProfile.FallbackShape = Enum.TryParse<PointMarkerShape>(
                settings.ActualProfile.FallbackShape, out var fallbackShape) ? fallbackShape : PointMarkerShape.Circle;

            ProcessingScope.LimitToActiveView = settings.ProcessingScope.LimitToActiveView;
            ProcessingScope.TrimToBoundary = settings.ProcessingScope.TrimToBoundary;
            ProcessingScope.OuterLoopClosing.CloseOpenLoops = settings.ProcessingScope.OuterLoopClosing.CloseOpenLoops;
            ProcessingScope.OuterLoopClosing.CapOpenEnds = settings.ProcessingScope.OuterLoopClosing.CapOpenEnds;
            ProcessingScope.InnerLoopClosing.CloseOpenLoops = settings.ProcessingScope.InnerLoopClosing.CloseOpenLoops;
            ProcessingScope.InnerLoopClosing.CapOpenEnds = settings.ProcessingScope.InnerLoopClosing.CapOpenEnds;
            ProcessingScope.RemoveEngulfedOnly = settings.ProcessingScope.RemoveEngulfedOnly;
            ProcessingScope.MergePartialOverlaps = settings.ProcessingScope.MergePartialOverlaps;
            ProcessingScope.JoinCollinearLines = settings.ProcessingScope.JoinCollinearLines;
            ProcessingScope.LineJoinToleranceMm = settings.ProcessingScope.LineJoinToleranceMm;

            ComplexCurve.ReplaceWithFallback = settings.ComplexCurve.ReplaceWithFallback;
            ComplexCurve.FallbackShape = Enum.TryParse<SplineFallbackShape>(
                settings.ComplexCurve.FallbackShape, out var shape) ? shape : SplineFallbackShape.StraightChord;

            GlobalOverride.IsEnabled = settings.GlobalOverride.IsEnabled;
            GlobalOverride.LineStyleName = settings.GlobalOverride.LineStyleName;
            GlobalOverride.ColorName = settings.GlobalOverride.ColorName;

            SettingsLoaded = true;
        }

        private ToolSettings BuildSettingsSnapshot() => new()
        {
            CircleMarker = new CircleMarkerSettingsDto { DiameterMm = CircleMarker.DiameterMm },
            RectangleMarker = new RectangleMarkerSettingsDto
            {
                WidthMm = RectangleMarker.WidthMm,
                HeightMm = RectangleMarker.HeightMm,
                AlignmentMode = RectangleMarker.AlignmentMode.ToString(),
                ManualAngleDegrees = RectangleMarker.ManualAngleDegrees
            },
            ActualProfile = new ActualProfileSettingsDto { FallbackShape = ActualProfile.FallbackShape.ToString() },
            ProcessingScope = new ProcessingScopeDto
            {
                LimitToActiveView = ProcessingScope.LimitToActiveView,
                TrimToBoundary = ProcessingScope.TrimToBoundary,
                OuterLoopClosing = new LoopClosingSettingsDto
                {
                    CloseOpenLoops = ProcessingScope.OuterLoopClosing.CloseOpenLoops,
                    CapOpenEnds = ProcessingScope.OuterLoopClosing.CapOpenEnds
                },
                InnerLoopClosing = new LoopClosingSettingsDto
                {
                    CloseOpenLoops = ProcessingScope.InnerLoopClosing.CloseOpenLoops,
                    CapOpenEnds = ProcessingScope.InnerLoopClosing.CapOpenEnds
                },
                RemoveEngulfedOnly = ProcessingScope.RemoveEngulfedOnly,
                MergePartialOverlaps = ProcessingScope.MergePartialOverlaps,
                JoinCollinearLines = ProcessingScope.JoinCollinearLines,
                LineJoinToleranceMm = ProcessingScope.LineJoinToleranceMm
            },
            ComplexCurve = new ComplexCurveSettingsDto
            {
                ReplaceWithFallback = ComplexCurve.ReplaceWithFallback,
                FallbackShape = ComplexCurve.FallbackShape.ToString()
            },
            GlobalOverride = new GlobalOverrideSettingsDto
            {
                IsEnabled = GlobalOverride.IsEnabled,
                LineStyleName = GlobalOverride.LineStyleName,
                ColorName = GlobalOverride.ColorName
            }
        };

        [RelayCommand]
        private void SaveSettings()
        {
            _settingsService.Save(BuildSettingsSnapshot(), msg => AddLog(LogLevel.Info, msg));
        }

        /// <summary>"Pick Line" alignment option: picks an existing Detail Line
        /// to define the Rectangle marker's rotation axis.</summary>
        [RelayCommand]
        private void PickAxisLine()
        {
            _ownerWindow?.Hide();
            if (!_pickQueue.TryRun(PickAxisLineInRevit))
            {
                _ownerWindow?.Show();
                AddLog(LogLevel.Warning, "Pick Line: Revit is busy. Finish the current command or close any open dialog, then try again.");
            }
        }

        // Runs in the Revit API context (through _pickQueue).
        private void PickAxisLineInRevit(UIApplication app)
        {
            try
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                Document hostDoc = _selectedBoundaryElement.IsValidObject ? _selectedBoundaryElement.Document : null;
                if (uiDoc == null || hostDoc == null || !uiDoc.Document.Equals(hostDoc))
                {
                    AddLog(LogLevel.Error, "Pick Line: switch back to the project this window was opened for, then try again.");
                    return;
                }

                Reference pickedRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new DetailLineSelectionFilter(),
                    "Select a Detail Line to define the Rectangle marker's alignment axis");

                if (hostDoc.GetElement(pickedRef) is not DetailLine detailLine)
                {
                    AddLog(LogLevel.Warning, "Pick Line: selected element was not a Detail Line — alignment unchanged.");
                    return;
                }

                Curve curve = detailLine.GeometryCurve;
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                XYZ direction = (end - start).Normalize();
                double angleDegrees = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;

                RectangleMarker.ManualAngleDegrees = angleDegrees;
                RectangleMarker.AlignmentMode = RectangleAlignmentMode.Manual;

                string curveNote = curve is Line ? string.Empty : " (non-straight geometry — used its start→end chord direction)";
                AddLog(LogLevel.Info, $"Pick Line: axis set to {angleDegrees:F2}° from Detail Line {detailLine.Id.Value}{curveNote} — Rectangle alignment switched to Manual.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                AddLog(LogLevel.Info, "Pick Line: selection cancelled.");
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"Pick Line: failed — {ex.Message}");
            }
            finally
            {
                _ownerWindow?.Show();
            }
        }

        /// <summary>Releases the Pick Line external event. Called when the window closes.</summary>
        public void ReleaseRevitQueue() => _pickQueue.Dispose();
    }
}
