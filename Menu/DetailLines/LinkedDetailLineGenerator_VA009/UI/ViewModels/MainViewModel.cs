using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Core.Engine;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Core.Models;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Core.Services;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA009.UI.ViewModels
{
    /// <summary>
    /// PHASE 4 SCOPE (feature-complete for V1's three representation groups): Live
    /// Revit data for Linked Models and all three Element Tree branches — Profile
    /// (Floor/Roof), Linear (Wall/Structural Framing), and Point (Structural/
    /// Architectural Columns, Mechanical Equipment). "Create Detail Lines" runs the
    /// real ProfileProcessingEngine, LinearProcessingEngine, and PointProcessingEngine
    /// via CreateDetailLinesEventHandler / ExternalEvent for enabled mappings in any
    /// group. Mechanical Equipment elements are classified per-element (Point vs
    /// Curve location) at processing time, not assumed from category, per spec
    /// Section 21.
    /// </summary>
    public partial class MainViewModel : ObservableObject
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
        /// around PickAxisLine's Selection.PickObject call, same pattern as
        /// RoofRidgeViewModel's SelectRoof across the suite.</summary>
        private Window? _ownerWindow;

        public void SetOwnerWindow(Window window) => _ownerWindow = window;

        // ── Header / Settings state ──────────────────────────────────────
        [ObservableProperty]

        private bool _settingsLoaded;

        // ── Run state (drives gray-out/inactive UI) ──────────────────────
        [ObservableProperty]
        private ToolRunState _runState = ToolRunState.Idle;

        /// <summary>True while RunState == Running — used to disable the Run button
        /// during the operation per suite convention.</summary>
        public bool IsRunning => RunState == ToolRunState.Running;

        /// <summary>True once RunState == Complete — bound to the form's IsEnabled
        /// (inverted) to produce the gray/inactive post-run state.</summary>
        public bool IsFormLocked => RunState == ToolRunState.Complete;

        // ── ToolWindowShell status strip (CLAUDE.md v1.1) ─────────────────
        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _summaryText = "";

        // ── Metrics (Section: top metrics row) ────────────────────────────
        [ObservableProperty]
        private int _elementsFound;

        [ObservableProperty]
        private int _elementsProcessed;

        [ObservableProperty]
        private int _detailLinesCreated;

        [ObservableProperty]
        private int _filledRegionsCreated;

        [ObservableProperty]
        private int _elementsSkipped;

        [ObservableProperty]
        private int _criticalErrors;

        // ── VA007 Processing Scope toggle (default ON) ────────────────────
        // ON:  Links/Categories below are restricted to elements found within the
        //      selected Floor/Roof's own boundary (ElementBoundaryService).
        // OFF: boundary restriction is removed — falls back to VA006's behavior,
        //      whatever is view-visible (ViewBoundaryService's crop/extent boundary).
        [ObservableProperty]
        private bool _restrictToBoundary = true;

        public string ScopeHelpText => RestrictToBoundary
            ? "On — Links and Categories below list only elements found inside the selected Floor/Roof's boundary."
            : "Off — boundary restriction removed. Links and Categories list whatever is currently visible in the active view.";

        /// <summary>Shown on the footer meta line — what the FINAL Create Detail
        /// Lines boundary will be, mirroring ScopeHelpText's wording.</summary>
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

        // ── Section 1: Linked Models & Instances (expander, default expanded) ──
        [ObservableProperty]
        private bool _isLinkedModelsExpanded = true;

        public ObservableCollection<LinkedModelItem> LinkedModels { get; } = new();

        /// <summary>Filtered/sorted view over LinkedModels for the Links panel —
        /// search text, status chip and sort mode all apply here without touching
        /// the underlying selection state in LinkedModels itself.</summary>
        public ICollectionView LinkedModelsView { get; }

        public int SelectedLinkCount => LinkedModels.Count(l => l.IsSelected);

        [ObservableProperty]
        private string _linkSearchText = string.Empty;

        [ObservableProperty]
        private LinkStatusFilter _linkStatusFilter = LinkStatusFilter.All;

        [ObservableProperty]
        private LinkSortMode _linkSortMode = LinkSortMode.Name;

        partial void OnLinkSearchTextChanged(string value) => LinkedModelsView.Refresh();
        partial void OnLinkStatusFilterChanged(LinkStatusFilter value) => LinkedModelsView.Refresh();

        partial void OnLinkSortModeChanged(LinkSortMode value)
        {
            LinkedModelsView.SortDescriptions.Clear();
            LinkedModelsView.SortDescriptions.Add(value == LinkSortMode.Status
                ? new SortDescription(nameof(LinkedModelItem.Status), ListSortDirection.Ascending)
                : new SortDescription(nameof(LinkedModelItem.DocumentTitle), ListSortDirection.Ascending));
            LinkedModelsView.Refresh();
        }

        private bool FilterLinkedModel(object obj)
        {
            if (obj is not LinkedModelItem link) return false;

            bool statusOk = LinkStatusFilter switch
            {
                LinkStatusFilter.Loaded => link.Status == LinkedFileStatus.Loaded,
                LinkStatusFilter.NeedsReview => link.Status == LinkedFileStatus.CanBeUpgraded,
                _ => true
            };
            if (!statusOk) return false;

            if (string.IsNullOrWhiteSpace(LinkSearchText)) return true;
            return link.DocumentTitle.Contains(LinkSearchText, StringComparison.OrdinalIgnoreCase)
                || link.InstanceName.Contains(LinkSearchText, StringComparison.OrdinalIgnoreCase);
        }

        // ── Section 2: Element Selection (Phase 5: three always-visible
        //    per-group expanders — Profile / Linear / Point — replacing the old
        //    single-tree-plus-tab-switcher layout, which never actually filtered
        //    the tree by tab; see LinkTreeNode.*Categories) ─────────────────
        [ObservableProperty]
        private bool _isProfileSelectionExpanded = true;

        [ObservableProperty]
        private bool _isLinearSelectionExpanded = true;

        [ObservableProperty]
        private bool _isPointSelectionExpanded = true;

        public ObservableCollection<LinkTreeNode> ElementTree { get; } = new();

        [ObservableProperty]
        private string _treeSearchText = string.Empty;

        // ── VA007 Categories panel: Width/Height/Perimeter/Area filters ───
        // Width/Height are free-form (bounding-box feet); Perimeter/Area are the
        // spec's fixed 100→1000-step-50 combo values (feet / sq ft), min and max.
        public static readonly double[] RangeFilterSteps =
            Enumerable.Range(2, 19).Select(i => i * 50.0).ToArray(); // 100,150,...,1000

        /// <summary>Instance-property wrapper so XAML can bind ComboBox.ItemsSource
        /// to the static RangeFilterSteps array via a normal {Binding}.</summary>
        public double[] RangeFilterStepsList => RangeFilterSteps;

        [ObservableProperty]
        private double? _widthMinFt;

        [ObservableProperty]
        private double? _widthMaxFt;

        [ObservableProperty]
        private double? _heightMinFt;

        [ObservableProperty]
        private double? _heightMaxFt;

        [ObservableProperty]
        private double? _perimeterMinFt;

        [ObservableProperty]
        private double? _perimeterMaxFt;

        [ObservableProperty]
        private double? _areaMinSqFt;

        [ObservableProperty]
        private double? _areaMaxSqFt;

        /// <summary>False = "By Category" (existing Category>Family>Type tree,
        /// Section 2a/2b/2c's default view). True = "By Type": the same Types,
        /// flattened and grouped by Type name instead — two independent views of
        /// the identical checked set (see TypeGroupItem's remarks).</summary>
        [ObservableProperty]
        private bool _groupElementsByType;

        public ObservableCollection<TypeGroupItem> ProfileTypeGroups { get; } = new();
        public ObservableCollection<TypeGroupItem> LinearTypeGroups { get; } = new();
        public ObservableCollection<TypeGroupItem> PointTypeGroups { get; } = new();

        partial void OnTreeSearchTextChanged(string value) => RecomputeTreeVisibility();
        partial void OnWidthMinFtChanged(double? value) => RecomputeTreeVisibility();
        partial void OnWidthMaxFtChanged(double? value) => RecomputeTreeVisibility();
        partial void OnHeightMinFtChanged(double? value) => RecomputeTreeVisibility();
        partial void OnHeightMaxFtChanged(double? value) => RecomputeTreeVisibility();
        partial void OnPerimeterMinFtChanged(double? value) => RecomputeTreeVisibility();
        partial void OnPerimeterMaxFtChanged(double? value) => RecomputeTreeVisibility();
        partial void OnAreaMinSqFtChanged(double? value) => RecomputeTreeVisibility();
        partial void OnAreaMaxSqFtChanged(double? value) => RecomputeTreeVisibility();

        // ── Section 3: Mapping Grid ─────────────────────────────────────
        public ObservableCollection<ElementMapping> Mappings { get; } = new();

        /// <summary>Grouped view of Mappings, grouped by RepresentationGroup, for the
        /// expander-per-group mapping grid display. XAML binds to this instead of
        /// Mappings directly so ItemsControl.GroupStyle has a live grouping to render.</summary>
        public ICollectionView MappingsGrouped { get; }

        /// <summary>Grand total badge shown at the bottom of the Mapping card —
        /// sum of all mapping rows across all groups. (Flagged assumption: confirm
        /// this is the intended meaning vs. total Detail Lines, which is only known
        /// post-run and already surfaced via DetailLinesCreated above.)</summary>
        public int TotalMappingCount => Mappings.Count;

        [ObservableProperty]
        private string _mappingFilterText = string.Empty;

        /// <summary>Per-type Detail Line Style options for the Mapping Grid's style
        /// selector — host project's Lines subcategories, populated in LoadLiveData.
        /// Each ElementMapping row picks independently via its own DetailLineStyleName,
        /// so different Types can be assigned different line types.</summary>
        public ObservableCollection<string> AvailableLineStyleNames { get; } = new();

        /// <summary>Per-type color override options for the Mapping Grid's color
        /// selector. "None" (no override) plus the fixed named palette from
        /// GraphicsOverrideService. Each ElementMapping row picks independently via
        /// its own ColorName, so different Types can be assigned different colors.</summary>
        public ObservableCollection<string> AvailableColorNames { get; } = new();

        /// <summary>Mapping Grid master override (Section 3): when enabled, every
        /// enabled mapping's Detail Lines are created with THIS Line Style/Color
        /// instead of its own row's selection — applied at generation time only
        /// (CreateDetailLinesEventHandler), never overwriting the per-row values
        /// themselves. Per-row Line Style/Color combos are disabled in the UI while
        /// this is on, so it's visually clear they're not taking effect.</summary>
        public GlobalOverrideSettings GlobalOverride { get; } = new();

        // ── Section 4a/4b: Point Marker settings ───────────────────────
        public CircleMarkerSettings CircleMarker { get; } = new();
        public RectangleMarkerSettings RectangleMarker { get; } = new();
        public ActualProfileSettings ActualProfile { get; } = new();

        // ── Section 5: Processing Scope ─────────────────────────────────
        public ProcessingScope ProcessingScope { get; } = new();

        // ── Section 6: Complex Curve Handling ───────────────────────────
        public ComplexCurveSettings ComplexCurve { get; } = new();

        // ── Log panel ────────────────────────────────────────────────────
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

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

            // ExternalEvent.Create() called here — MainViewModel is constructed from
            // MainWindow's constructor, which runs in the active API context triggered
            // by the PushButton command (per suite convention: never lazily inside
            // Execute()).
            _externalEvent = ExternalEvent.Create(_eventHandler);
            _pickQueue = new Revit26_Plugin_Revised.Shared.Services.RevitApiQueue("Detail Lines From Links VA009 — Pick Line");
            _eventHandler.OnComplete = OnProcessingComplete;

            MappingsGrouped = CollectionViewSource.GetDefaultView(Mappings);
            MappingsGrouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ElementMapping.Group)));

            LinkedModelsView = CollectionViewSource.GetDefaultView(LinkedModels);
            LinkedModelsView.Filter = FilterLinkedModel;
            LinkedModelsView.SortDescriptions.Add(new SortDescription(nameof(LinkedModelItem.DocumentTitle), ListSortDirection.Ascending));

            LoadSettings();
            LoadLiveData();

            Mappings.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TotalMappingCount));
            LinkedModels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectedLinkCount));

            RunState = ToolRunState.Configuring;
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
            CircleMarker = new CircleMarkerSettingsDto
            {
                DiameterMm = CircleMarker.DiameterMm
            },
            RectangleMarker = new RectangleMarkerSettingsDto
            {
                WidthMm = RectangleMarker.WidthMm,
                HeightMm = RectangleMarker.HeightMm,
                AlignmentMode = RectangleMarker.AlignmentMode.ToString(),
                ManualAngleDegrees = RectangleMarker.ManualAngleDegrees
            },
            ActualProfile = new ActualProfileSettingsDto
            {
                FallbackShape = ActualProfile.FallbackShape.ToString()
            },
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

        /// <summary>"Pick Line" alignment option (4B): lets the user define the
        /// Rectangle marker's rotation axis by picking an existing Detail Line in
        /// the host view, instead of typing a Manual angle or relying on one of the
        /// automatic modes.
        ///   1. Hide this window so it doesn't block the view.
        ///   2. Single-selection PickObject restricted to Detail Lines (DetailLineSelectionFilter).
        ///   3. Show this window again (finally — runs on cancel/error too).
        ///   4. Extract the line's direction as an angle and feed it into the SAME
        ///      alignment logic Manual mode already uses — ManualAngleDegrees is the
        ///      one place PointProcessingEngine reads a fixed angle from, so setting
        ///      AlignmentMode to Manual here reuses that path rather than adding a
        ///      parallel one.</summary>
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

        /// <summary>Snapshots the live ProcessingScope for a run request. A plain
        /// object initializer can't set OuterLoopClosing/InnerLoopClosing (get-only
        /// properties, each holding its own mutual-exclusivity wiring), so those are
        /// copied field-by-field after construction instead.</summary>
        private ProcessingScope BuildProcessingScopeSnapshot()
        {
            var snapshot = new ProcessingScope
            {
                LimitToActiveView = ProcessingScope.LimitToActiveView,
                TrimToBoundary = ProcessingScope.TrimToBoundary,
                RemoveEngulfedOnly = ProcessingScope.RemoveEngulfedOnly,
                MergePartialOverlaps = ProcessingScope.MergePartialOverlaps,
                JoinCollinearLines = ProcessingScope.JoinCollinearLines,
                LineJoinToleranceMm = ProcessingScope.LineJoinToleranceMm
            };
            snapshot.OuterLoopClosing.CloseOpenLoops = ProcessingScope.OuterLoopClosing.CloseOpenLoops;
            snapshot.OuterLoopClosing.CapOpenEnds = ProcessingScope.OuterLoopClosing.CapOpenEnds;
            snapshot.InnerLoopClosing.CloseOpenLoops = ProcessingScope.InnerLoopClosing.CloseOpenLoops;
            snapshot.InnerLoopClosing.CapOpenEnds = ProcessingScope.InnerLoopClosing.CapOpenEnds;
            return snapshot;
        }

        // ── Logging helper ──────────────────────────────────────────────

        private void AddLog(LogLevel level, string message)
        {
            LogEntries.Add(new LogEntry(level, message));
        }

        [RelayCommand]
        private void ClearLog() => LogEntries.Clear();

        // ── Section 3: Mapping grid commands ────────────────────────────

        [RelayCommand]
        private void SelectAllMappings()
        {
            foreach (var m in Mappings) m.IsEnabled = true;
        }

        [RelayCommand]
        private void ClearAllMappings()
        {
            foreach (var m in Mappings) m.IsEnabled = false;
        }

        [RelayCommand]
        private void RemoveSelectedMappings()
        {
            var toRemove = Mappings.Where(m => !m.IsEnabled).ToList();
            foreach (var m in toRemove) Mappings.Remove(m);
            AddLog(LogLevel.Info, $"Removed {toRemove.Count} mapping(s)");
        }

        // ── Footer commands ──────────────────────────────────────────────

        [RelayCommand(CanExecute = nameof(CanClear))]
        private void Clear()
        {
            Mappings.Clear();
            foreach (var link in LinkedModels) link.IsSelected = false;
            AddLog(LogLevel.Info, "Selections cleared");
        }
        private bool CanClear() => RunState != ToolRunState.Complete;

        [RelayCommand(CanExecute = nameof(CanRun))]
        private void CreateDetailLines()
        {
            RunState = ToolRunState.Running;

            var enabledMappings = Mappings.Where(m => m.IsEnabled).ToList();
            AddLog(LogLevel.Info, $"Run started — {enabledMappings.Count} mapping(s) enabled.");

            ElementsFound = 0;
            ElementsProcessed = 0;
            DetailLinesCreated = 0;
            FilledRegionsCreated = 0;
            ElementsSkipped = 0;
            CriticalErrors = 0;

            var (boundary, isExact) = GetActiveProcessingBoundary();

            if (!ProcessingScope.LimitToActiveView)
            {
                AddLog(LogLevel.Info, "'Limit to active view' is off — processing boundary still computed the same way per performance requirement (Section 32); this toggle governs candidate pre-filtering scope, not boundary shape.");
            }

            // Assign Detail Line Style default if a mapping has none set yet — falls
            // back to the first available host project line style, logged so it's
            // visible rather than silently applied.
            var availableStyles = _lineStyleService.GetAvailableLineStyles(_uiApp.ActiveUIDocument.Document);
            foreach (var m in enabledMappings.Where(m => string.IsNullOrWhiteSpace(m.DetailLineStyleName)))
            {
                if (availableStyles.Count > 0)
                {
                    m.DetailLineStyleName = availableStyles[0].Name;
                    AddLog(LogLevel.Info, $"Mapping '{m.TypeName}' had no Detail Line Style selected — defaulted to '{m.DetailLineStyleName}'.");
                }
            }

            _eventHandler.PendingRequest = new CreateDetailLinesRequest
            {
                EnabledMappings = enabledMappings,
                ProcessingBoundary = boundary,
                ProcessingScope = BuildProcessingScopeSnapshot(),
                ComplexCurveSettings = new ComplexCurveSettings
                {
                    ReplaceWithFallback = ComplexCurve.ReplaceWithFallback,
                    FallbackShape = ComplexCurve.FallbackShape
                },
                CircleMarkerSettings = new CircleMarkerSettings
                {
                    DiameterMm = CircleMarker.DiameterMm
                },
                RectangleMarkerSettings = new RectangleMarkerSettings
                {
                    WidthMm = RectangleMarker.WidthMm,
                    HeightMm = RectangleMarker.HeightMm,
                    AlignmentMode = RectangleMarker.AlignmentMode,
                    ManualAngleDegrees = RectangleMarker.ManualAngleDegrees
                },
                ActualProfileSettings = new ActualProfileSettings
                {
                    FallbackShape = ActualProfile.FallbackShape
                },
                GlobalOverride = new GlobalOverrideSettings
                {
                    IsEnabled = GlobalOverride.IsEnabled,
                    LineStyleName = GlobalOverride.LineStyleName,
                    ColorName = GlobalOverride.ColorName
                },
                OnLog = (msg, sev) => AddLog(MapSeverity(sev), msg)
            };

            _externalEvent.Raise();
        }

        private static LogLevel MapSeverity(Core.Engine.LogSeverity sev) => sev switch
        {
            Core.Engine.LogSeverity.Warning => LogLevel.Warning,
            Core.Engine.LogSeverity.Error => LogLevel.Error,
            Core.Engine.LogSeverity.Success => LogLevel.Success,
            Core.Engine.LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Info
        };

        private bool CanRun() => RunState == ToolRunState.Configuring && Mappings.Any(m => m.IsEnabled);

        /// <summary>VA007's boundary source switch: RestrictToBoundary picks the
        /// selected Floor/Roof's own outer edge (ElementBoundaryService); off falls
        /// back to VA006's view-crop boundary (ViewBoundaryService) unchanged. Used
        /// both here (final processing boundary) and by RebuildElementTree (tree
        /// candidate pre-filter) — same boundary, two use sites.</summary>
        private (System.Collections.Generic.List<XYZ> boundary, bool isExact) GetActiveProcessingBoundary()
        {
            if (RestrictToBoundary)
            {
                return _elementBoundaryService.GetProcessingBoundary(
                    _selectedBoundaryElement, ComplexCurve, msg => AddLog(LogLevel.Info, msg));
            }

            View activeView = _uiApp.ActiveUIDocument.ActiveView;
            return _viewBoundaryService.GetProcessingBoundary(activeView, msg => AddLog(LogLevel.Info, msg));
        }

        [RelayCommand]
        private void ResetSession()
        {
            RunState = ToolRunState.Configuring;
            ElementsFound = 0;
            ElementsProcessed = 0;
            DetailLinesCreated = 0;
            FilledRegionsCreated = 0;
            ElementsSkipped = 0;
            CriticalErrors = 0;
            AddLog(LogLevel.Info, "New session started");
        }

        partial void OnRunStateChanged(ToolRunState value)
        {
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsFormLocked));
            CreateDetailLinesCommand.NotifyCanExecuteChanged();
            ClearCommand.NotifyCanExecuteChanged();
        }

        // ── Live data (Phase 2): real LinkService queries, reactive tree→mapping ──

        private void LoadLiveData()
        {
            Document hostDoc = _uiApp.ActiveUIDocument.Document;
            View activeView = _uiApp.ActiveUIDocument.ActiveView;
            CurrentViewName = activeView.Name;

            AvailableLineStyleNames.Clear();
            foreach (var style in _lineStyleService.GetAvailableLineStyles(hostDoc))
                AvailableLineStyleNames.Add(style.Name);

            AvailableColorNames.Clear();
            AvailableColorNames.Add("None");
            foreach (var colorName in _graphicsOverrideService.AvailableColorNames)
                AvailableColorNames.Add(colorName);

            var links = _linkService.GetLinkedModels(hostDoc, msg => AddLog(LogLevel.Info, msg));
            foreach (var link in links)
            {
                link.PropertyChanged += OnLinkSelectionChanged;
                LinkedModels.Add(link);
            }

            // Auto-select loaded links so the tree has something to show on open
            // (matches the mockup's default state); user can uncheck freely.
            foreach (var link in LinkedModels.Where(l => l.IsLoaded))
                link.IsSelected = true;

            RebuildElementTree();
        }

        private void OnLinkSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LinkedModelItem.IsSelected))
            {
                OnPropertyChanged(nameof(SelectedLinkCount));
                RebuildElementTree();
            }
        }

        private void RebuildElementTree()
        {
            Document hostDoc = _uiApp.ActiveUIDocument.Document;
            var selectedIds = LinkedModels.Where(l => l.IsSelected).Select(l => l.LinkInstanceId).ToList();

            // Detach old TypeTreeItem handlers before rebuilding to avoid leaking
            // subscriptions across tree rebuilds.
            foreach (var node in ElementTree)
                foreach (var cat in node.Categories)
                    foreach (var fam in cat.Families)
                        foreach (var t in fam.Types)
                            t.PropertyChanged -= OnTypeCheckedChanged;

            ElementTree.Clear();
            ProfileTypeGroups.Clear();
            LinearTypeGroups.Clear();
            PointTypeGroups.Clear();

            List<XYZ>? scopeBoundary = null;
            if (RestrictToBoundary)
            {
                var (boundary, _) = GetActiveProcessingBoundary();
                scopeBoundary = boundary;
            }

            var nodes = _linkService.BuildElementTree(hostDoc, selectedIds, scopeBoundary, msg => AddLog(LogLevel.Info, msg));
            foreach (var node in nodes)
            {
                foreach (var cat in node.Categories)
                    foreach (var fam in cat.Families)
                        foreach (var t in fam.Types)
                        {
                            // Reflect current mapping state if a mapping already exists
                            // for this type (e.g. after a link is unchecked and rechecked).
                            t.IsChecked = Mappings.Any(m => m.LinkInstanceId == node.LinkInstanceId && m.TypeId == t.TypeId);
                            t.PropertyChanged += OnTypeCheckedChanged;
                        }

                ElementTree.Add(node);
            }

            BuildTypeGroups(nodes);
            RecomputeTreeVisibility();

            // Remove mappings whose link is no longer selected (link unchecked).
            var toRemove = Mappings.Where(m => !selectedIds.Contains(m.LinkInstanceId)).ToList();
            foreach (var m in toRemove) Mappings.Remove(m);

            AddLog(LogLevel.Info, $"{LinkedModels.Count(l => l.IsSelected)} linked instance(s) selected — element tree loaded ({nodes.Sum(n => n.Categories.Sum(c => c.Families.Sum(f => f.Types.Count)))} type(s) available).");
        }

        /// <summary>Builds the flat "Group by Type" views (one per RepresentationGroup)
        /// from the same TypeTreeItem instances the Category>Family>Type tree just
        /// got — grouped by Type NAME so the rare case of two different Types
        /// sharing a name still gets one expandable group with 2+ Members.</summary>
        private void BuildTypeGroups(System.Collections.Generic.List<LinkTreeNode> nodes)
        {
            var allTypes = nodes
                .SelectMany(n => n.Categories)
                .SelectMany(c => c.Families)
                .SelectMany(f => f.Types)
                .ToList();

            void Fill(ObservableCollection<TypeGroupItem> target, RepresentationGroup group)
            {
                var groups = allTypes.Where(t => t.Group == group)
                    .GroupBy(t => t.TypeName)
                    .OrderBy(g => g.Key);

                foreach (var g in groups)
                {
                    var item = new TypeGroupItem { TypeName = g.Key, Group = group };
                    foreach (var t in g) item.Members.Add(t);
                    target.Add(item);
                }
            }

            Fill(ProfileTypeGroups, RepresentationGroup.Profile);
            Fill(LinearTypeGroups, RepresentationGroup.Linear);
            Fill(PointTypeGroups, RepresentationGroup.Point);
        }

        /// <summary>Re-derives IsVisible on every Type/Family/Category node from the
        /// current TreeSearchText + Width/Height/Perimeter/Area filters. A Family or
        /// Category is visible whenever at least one descendant Type is — this is
        /// what lets the XAML collapse an entirely-filtered-out branch without a
        /// separate "empty state" template. Also refreshes each TypeGroupItem's own
        /// IsVisible the same way, so the "By Type" tab filters identically.</summary>
        private void RecomputeTreeVisibility()
        {
            bool TypeMatches(TypeTreeItem t)
            {
                if (!string.IsNullOrWhiteSpace(TreeSearchText))
                {
                    bool textMatch = t.TypeName.Contains(TreeSearchText, StringComparison.OrdinalIgnoreCase)
                        || t.FamilyName.Contains(TreeSearchText, StringComparison.OrdinalIgnoreCase)
                        || t.CategoryName.Contains(TreeSearchText, StringComparison.OrdinalIgnoreCase);
                    if (!textMatch) return false;
                }

                var m = t.Metrics;
                if (m == null) return true;

                if (WidthMinFt.HasValue && m.WidthFeet < WidthMinFt.Value) return false;
                if (WidthMaxFt.HasValue && m.WidthFeet > WidthMaxFt.Value) return false;
                if (HeightMinFt.HasValue && m.HeightFeet < HeightMinFt.Value) return false;
                if (HeightMaxFt.HasValue && m.HeightFeet > HeightMaxFt.Value) return false;

                // Perimeter/Area only exist for Profile-group Types (see ElementMetrics) —
                // a Linear/Point Type is never excluded by these two filters.
                if (PerimeterMinFt.HasValue && m.PerimeterFeet.HasValue && m.PerimeterFeet.Value < PerimeterMinFt.Value) return false;
                if (PerimeterMaxFt.HasValue && m.PerimeterFeet.HasValue && m.PerimeterFeet.Value > PerimeterMaxFt.Value) return false;
                if (AreaMinSqFt.HasValue && m.AreaSqFt.HasValue && m.AreaSqFt.Value < AreaMinSqFt.Value) return false;
                if (AreaMaxSqFt.HasValue && m.AreaSqFt.HasValue && m.AreaSqFt.Value > AreaMaxSqFt.Value) return false;

                return true;
            }

            foreach (var node in ElementTree)
                foreach (var cat in node.Categories)
                {
                    bool anyCatVisible = false;
                    foreach (var fam in cat.Families)
                    {
                        bool anyFamVisible = false;
                        foreach (var t in fam.Types)
                        {
                            t.IsVisible = TypeMatches(t);
                            anyFamVisible |= t.IsVisible;
                        }
                        fam.IsVisible = anyFamVisible;
                        anyCatVisible |= anyFamVisible;
                    }
                    cat.IsVisible = anyCatVisible;
                }

            foreach (var group in ProfileTypeGroups.Concat(LinearTypeGroups).Concat(PointTypeGroups))
                group.IsVisible = group.Members.Any(m => m.IsVisible);

            foreach (var node in ElementTree)
                node.RefreshVisibilityFlags();
        }

        /// <summary>Checking/unchecking a Type in Section 2's tree adds/removes the
        /// corresponding Mapping Grid row, per spec Section 7: "Only explicitly
        /// selected Types should be added to the Mapping Grid."</summary>
        private void OnTypeCheckedChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(TypeTreeItem.IsChecked)) return;
            if (sender is not TypeTreeItem typeItem) return;

            // Find which link/category/family this type belongs to by walking the tree.
            foreach (var node in ElementTree)
                foreach (var cat in node.Categories)
                    foreach (var fam in cat.Families)
                        if (fam.Types.Contains(typeItem))
                        {
                            if (typeItem.IsChecked)
                                AddMappingIfMissing(node, cat, fam, typeItem);
                            else
                                RemoveMapping(node.LinkInstanceId, typeItem.TypeId);
                            return;
                        }
        }

        /// <summary>Checks every Type under the given Category (all its Families) —
        /// each toggle cascades through OnTypeCheckedChanged exactly as a manual click
        /// would, so mappings are created the same way.</summary>
        [RelayCommand]
        private void SelectAllInCategory(CategoryTreeItem category)
        {
            foreach (var fam in category.Families)
                foreach (var t in fam.Types)
                    t.IsChecked = true;
        }

        /// <summary>Unchecks every Type under the given Category, removing their
        /// mappings via the same cascade OnTypeCheckedChanged already handles.</summary>
        [RelayCommand]
        private void SelectNoneInCategory(CategoryTreeItem category)
        {
            foreach (var fam in category.Families)
                foreach (var t in fam.Types)
                    t.IsChecked = false;
        }

        /// <summary>"By Type" tab equivalent of SelectAllInCategory — checks every
        /// Member of the group (same TypeTreeItem instances as the Category tree,
        /// so the cascade behaves identically either way).</summary>
        [RelayCommand]
        private void SelectAllInTypeGroup(TypeGroupItem group)
        {
            foreach (var t in group.Members) t.IsChecked = true;
        }

        [RelayCommand]
        private void SelectNoneInTypeGroup(TypeGroupItem group)
        {
            foreach (var t in group.Members) t.IsChecked = false;
        }

        private void AddMappingIfMissing(LinkTreeNode node, CategoryTreeItem cat, FamilyTreeItem fam, TypeTreeItem typeItem)
        {
            if (Mappings.Any(m => m.LinkInstanceId == node.LinkInstanceId && m.TypeId == typeItem.TypeId))
                return;

            // Columns default to their real footprint (circle/rectangle, true size and
            // rotation); other Point categories (e.g. Mechanical Equipment) keep the
            // fixed-size Circle marker, which is still selectable per row.
            bool isColumnCategory = cat.CategoryName is "Structural Columns" or "Columns";

            var defaultRepresentation = cat.Group switch
            {
                RepresentationGroup.Profile => RepresentationMode.Boundary,
                RepresentationGroup.Linear => RepresentationMode.Centerline,
                RepresentationGroup.Point => isColumnCategory ? RepresentationMode.ActualProfile : RepresentationMode.Circle,
                _ => RepresentationMode.Boundary
            };

            var mapping = new ElementMapping
            {
                LinkInstanceId = node.LinkInstanceId,
                LinkDisplayName = node.LinkDisplayName,
                CategoryName = cat.CategoryName.TrimEnd('s'), // "Floors" -> "Floor" to match existing display convention
                FamilyName = fam.FamilyName,
                TypeName = typeItem.TypeName,
                TypeId = typeItem.TypeId,
                Group = cat.Group,
                Representation = defaultRepresentation,
                DetailLineStyleName = string.Empty,
                ColorName = "None"
            };
            mapping.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ElementMapping.IsEnabled))
                    CreateDetailLinesCommand.NotifyCanExecuteChanged();
            };
            Mappings.Add(mapping);
            CreateDetailLinesCommand.NotifyCanExecuteChanged();
        }

        private void RemoveMapping(long linkInstanceId, long typeId)
        {
            var existing = Mappings.FirstOrDefault(m => m.LinkInstanceId == linkInstanceId && m.TypeId == typeId);
            if (existing != null)
            {
                Mappings.Remove(existing);
                CreateDetailLinesCommand.NotifyCanExecuteChanged();
            }
        }

        // ── Real processing (Phase 2: Profile group only) ──────────────────

        private void OnProcessingComplete(ProcessingResult result)
        {
            ElementsFound = result.ElementsFound;
            ElementsProcessed = result.ElementsProcessed;
            DetailLinesCreated = result.DetailLinesCreated;
            FilledRegionsCreated = result.FilledRegionsCreated;
            ElementsSkipped = result.ElementsSkipped;
            CriticalErrors = result.CriticalErrors;

            foreach (var err in result.Errors)
                AddLog(err.Level, $"Element {err.ElementId} ({err.CategoryName}): {err.Reason}");

            RunState = ToolRunState.Complete;
            SaveSettings();

            bool autoSaved = _logExportService.AutoSave(LogEntries, out var path);
            AddLog(autoSaved ? LogLevel.Success : LogLevel.Warning,
                autoSaved ? $"Log auto-saved to {path}" : "Log auto-save failed");
        }

        /// <summary>Releases the Pick Line external event. Called when the window closes.</summary>
        public void ReleaseRevitQueue() => _pickQueue.Dispose();
    }
}

