using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Models;
using Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Services;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.ViewModels
{
    public partial class FloorRidgeViewModel : ObservableObject
    {
        // â”€â”€ Services â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        /// <summary>
        /// Routes Revit API work into API context — the window is modeless, so command
        /// handlers here run outside it and cannot start transactions directly.
        /// </summary>
        private readonly RevitApiDispatcher _revitApi;
        private readonly DrainGroupingService _groupingSvc;
        private readonly VoronoiComputationService _voronoiSvc;
        private readonly FloorBoundaryService _boundarySvc;
        private readonly VoronoiClippingService _clippingSvc;
        private readonly RidgeValidationService _validationSvc;
        private readonly RidgeCreationService _creationSvc;
        private readonly InnerLoopService _innerLoopSvc;
        private readonly InnerLoopIntersectionService _innerLoopIntersectionSvc;

        // â”€â”€ Internal state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private Floor _selectedFloor;
        private List<XYZ> _selectedDrainLocations = new List<XYZ>();
        private VoronoiRidgeResult _lastResult;
        private Window _ownerWindow;

        // â”€â”€ Pipeline log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<string> PipelineLog { get; }
            = new ObservableCollection<string>();

        [ObservableProperty]
        private string _pipelineLogText = string.Empty;

        // â”€â”€ Constructors â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public FloorRidgeViewModel(UIDocument uiDoc, RevitApiDispatcher revitApi, Floor floor, List<XYZ> drainLocations)
            : this(uiDoc, revitApi)
        {
            _selectedFloor = floor ?? throw new ArgumentNullException(nameof(floor));
            _selectedDrainLocations = drainLocations ?? new List<XYZ>();
            FloorDescription = $"Floor: {_selectedFloor.Name}  (Id {_selectedFloor.Id})";
            DrainCount = _selectedDrainLocations.Count;
            StatusMessage = $"Pre-selected {DrainCount} drain point(s). You may re-select if needed.";
        }

        public FloorRidgeViewModel(UIDocument uiDoc, RevitApiDispatcher revitApi)
        {
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _doc = uiDoc.Document;
            _revitApi = revitApi ?? throw new ArgumentNullException(nameof(revitApi));
            _groupingSvc = new DrainGroupingService();
            _voronoiSvc = new VoronoiComputationService();
            _boundarySvc = new FloorBoundaryService();
            _clippingSvc = new VoronoiClippingService();
            _validationSvc = new RidgeValidationService();
            _creationSvc = new RidgeCreationService();
            _innerLoopSvc = new InnerLoopService();
            _innerLoopIntersectionSvc = new InnerLoopIntersectionService();
        }

        public void SetOwnerWindow(Window window) => _ownerWindow = window;

        // â”€â”€ Standard observable properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private string _floorDescription = "No floor selected";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private int _drainCount;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private double _proximityDistanceMm = 50.0;

        [ObservableProperty]
        private double _validationToleranceMm = 50.0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool _addDetailLines = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool _addShapePoints = true;

        [ObservableProperty]
        private bool _isBusy;

        // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _statusMessage = "Ready.";

        [ObservableProperty]
        private string _progressDetail = string.Empty;

        [ObservableProperty]
        private int _passCount;

        [ObservableProperty]
        private int _failCount;

        [ObservableProperty]
        private string _groupCountDisplay = "0";

        // â”€â”€ 5 Status Cards â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [ObservableProperty]
        private string _firstResetStatus = "â€”";

        [ObservableProperty]
        private int _totalCalculatedPoints;

        [ObservableProperty]
        private int _totalAddedPoints;

        [ObservableProperty]
        private int _totalFailedPoints;

        [ObservableProperty]
        private int _totalSkippedPoints;

        [ObservableProperty]
        private int _detailLinesAddedCount;

        [ObservableProperty]
        private string _lastResetStatus = "â€”";

        // â”€â”€ Validation log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public ObservableCollection<ValidationEntry> ValidationLog { get; }
            = new ObservableCollection<ValidationEntry>();

        // â”€â”€ Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [RelayCommand]
        private async Task SelectFloor()
        {
            try
            {
                _ownerWindow?.Hide();
                StatusMessage = "Click on a Floor element in the viewâ€¦";

                // PickObject needs API context — run it through the ExternalEvent.
                await RunInRevitAsync(() =>
                {
                    var ref_ = _uiDoc.Selection.PickObject(
                        ObjectType.Element,
                        new FloorSelectionFilter(),
                        "Select a Floor");

                    _selectedFloor = _doc.GetElement(ref_) as Floor;
                    if (_selectedFloor == null)
                    {
                        FloorDescription = "Selection failed â€” please retry";
                        StatusMessage = "Floor selection cancelled or invalid.";
                        return;
                    }

                    // Floor port: refuse floors that can't be changed.
                    string blockReason = FloorShapeEditChecks.GetBlockReason(_selectedFloor);
                    if (blockReason != null)
                    {
                        StatusMessage = $"Floor {_selectedFloor.Name} (Id {_selectedFloor.Id.Value}) can't be changed: {blockReason}.";
                        FloorDescription = "Pick another floor";
                        _selectedFloor = null;
                        return;
                    }
                    FloorDescription = $"Floor: {_selectedFloor.Name}  (Id {_selectedFloor.Id})";
                    StatusMessage = "Floor selected. Now select drain points.";
                });
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                StatusMessage = "Floor selection cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error selecting floor: {ex.Message}";
            }
            finally
            {
                _ownerWindow?.Show();
            }
        }

        [RelayCommand]
        private async Task SelectDrains()
        {
            if (_selectedFloor == null)
            {
                StatusMessage = "Please select a floor first.";
                return;
            }
            try
            {
                _ownerWindow?.Hide();

                // PickDrainPoints opens a TransactionGroup (temporary shape-editing enable)
                // and calls PickObjects — both need API context.
                List<XYZ> newDrains = null;
                await RunInRevitAsync(() =>
                {
                    EnsureSelectedFloorValid();
                    newDrains = DrainPointPicker.PickDrainPoints(_uiDoc, _selectedFloor);
                });
                _selectedDrainLocations = newDrains;
                DrainCount = _selectedDrainLocations.Count;
                StatusMessage = $"{DrainCount} drain point(s) selected.";
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                StatusMessage = "Drain selection cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during drain selection: {ex.Message}";
            }
            finally
            {
                _ownerWindow?.Show();
            }
        }

        [RelayCommand]
        private void CopyLog()
        {
            if (PipelineLog.Count == 0 && ValidationLog.Count == 0)
            {
                StatusMessage = "No log data to copy. Run the command first.";
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            sb.AppendLine("  FLOOR CREASE LINES BY POINTS V001 â€“ PIPELINE LOG");
            sb.AppendLine($"  Copied: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            foreach (var line in PipelineLog) sb.AppendLine(line);

            if (ValidationLog.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
                sb.AppendLine("  VALIDATION TABLE  (tab-separated â€” paste into Excel)");
                sb.AppendLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
                sb.AppendLine("Pt#\tX (ft)\tY (ft)\tX (mm)\tY (mm)\tMaxDev (ft)\tMaxDev (mm)\tTol (ft)\tPassed\tGroups\tNote");
                foreach (var e in ValidationLog)
                {
                    double xMm = e.X * 304.8;
                    double yMm = e.Y * 304.8;
                    double devMm = e.MaxDeviation * 304.8;
                    double tolMm = e.Tolerance * 304.8;
                    string groups = string.Join("+", e.DistancesToGroups.Select(
                        kv => $"G{kv.Key}={kv.Value * 304.8:F1}mm"));
                    sb.AppendLine(
                        $"{e.PointIndex}\t{e.X:F4}\t{e.Y:F4}\t{xMm:F1}\t{yMm:F1}" +
                        $"\t{e.MaxDeviation:F6}\t{devMm:F2}\t{tolMm:F2}" +
                        $"\t{(e.Passed ? "PASS" : "FAIL")}\t{groups}\t{e.Note}");
                }
                sb.AppendLine();
                sb.AppendLine($"SUMMARY\tPass: {PassCount}\tFail: {FailCount}\tTotal: {ValidationLog.Count}");
            }
            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                StatusMessage = $"Full log copied to clipboard ({PipelineLog.Count} lines, {ValidationLog.Count} validation rows).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Copy failed: {ex.Message}";
            }
        }

        // â”€â”€ Run â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [RelayCommand(CanExecute = nameof(CanRun))]
        private async Task Run()
        {
            IsBusy = true;
            ValidationLog.Clear();
            PipelineLog.Clear();
            PipelineLogText = string.Empty;
            StatusMessage = "Running Voronoi ridge generationâ€¦";

            // Reset cards
            FirstResetStatus = "Pendingâ€¦";
            TotalCalculatedPoints = 0;
            TotalAddedPoints = 0;
            TotalFailedPoints = 0;
            TotalSkippedPoints = 0;
            DetailLinesAddedCount = 0;
            LastResetStatus = "Pendingâ€¦";

            var runStart = DateTime.Now;
            Log($"[START]  {runStart:HH:mm:ss}");
            Log($"[INPUT]  {FloorDescription}");  // cached — the floor may have been deleted since it was picked
            Log($"[INPUT]  Raw drain points: {_selectedDrainLocations.Count}");
            Log($"[INPUT]  Proximity distance: {ProximityDistanceMm} mm");
            Log($"[INPUT]  Validation tolerance: {ValidationToleranceMm} mm");

            for (int i = 0; i < _selectedDrainLocations.Count; i++)
            {
                var p = _selectedDrainLocations[i];
                Log($"  Drain[{i}]  X={p.X:F4} ft ({p.X * 304.8:F1} mm)  Y={p.Y:F4} ft ({p.Y * 304.8:F1} mm)  Z={p.Z:F4} ft");
            }

            try
            {
                // â”€â”€ Step 1: Drain grouping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                UpdateProgress("Grouping drains by proximityâ€¦");
                Log("[STEP 1] Grouping drains by proximityâ€¦");

                double proxFeet = DrainGroupingService.MmToFeet(ProximityDistanceMm);
                var drainGroups = _groupingSvc.GroupDrains(_selectedDrainLocations, proxFeet);
                int groupCount = drainGroups.Count;
                GroupCountDisplay = groupCount.ToString();

                Log($"  Groups formed: {groupCount}");
                foreach (var g in drainGroups)
                {
                    Log($"  Group[{g.GroupIndex}]  Drains={g.DrainLocations.Count}" +
                        $"  Centroid X={g.Centroid.X:F4} ft  Y={g.Centroid.Y:F4} ft");
                }

                if (groupCount < 2)
                    throw new InvalidOperationException(
                        "At least two distinct drain groups are required.");

                if (groupCount < 3)
                    Log($"  [WARN] Only {groupCount} groups â€” midline bisector mode will be used.");

                // â”€â”€ Step 2: Boundary + inner-loop extraction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                // These call into the Revit API (get_Geometry, GetDependentElements,
                // GetElement) and therefore MUST run in API context (via the
                // ExternalEvent), before Task.Run rather than inside it.
                var result = new VoronoiRidgeResult();
                result.DrainGroups = drainGroups;

                UpdateProgress("Extracting floor boundaryâ€¦");
                Log("[STEP 2] Extracting floor boundaryâ€¦");
                List<XYZ> boundary = null;
                List<List<XYZ>> innerLoops = null;
                await RunInRevitAsync(() =>
                {
                    EnsureSelectedFloorValid();
                    boundary = _boundarySvc.ExtractBoundary(_selectedFloor);
                    innerLoops = _innerLoopSvc.ExtractInnerLoops(_selectedFloor);
                });

                // Store boundary + centroid for AddPoint fallback in RidgeCreationService
                result.BoundaryPolygon = boundary;
                result.FloorCentroid = new XYZ(
                    boundary.Average(p => p.X),
                    boundary.Average(p => p.Y),
                    0.0);

                Log($"  Boundary vertices: {boundary.Count}");
                {
                    double minX = boundary.Min(p => p.X), maxX = boundary.Max(p => p.X);
                    double minY = boundary.Min(p => p.Y), maxY = boundary.Max(p => p.Y);
                    Log($"  Boundary BBox  X=[{minX * 304.8:F1}, {maxX * 304.8:F1}] mm  Y=[{minY * 304.8:F1}, {maxY * 304.8:F1}] mm");
                }

                UpdateProgress("Detecting inner loopsâ€¦");
                Log("[STEP 2a] Detecting inner loopsâ€¦");
                result.InnerLoops = innerLoops;
                Log($"  Inner loops found: {result.InnerLoops.Count}");

                // â”€â”€ Steps 3â€“5: pure geometry/math â€” no Revit API calls, safe to run
                //              on a background thread for UI responsiveness â”€â”€â”€â”€â”€â”€â”€â”€â”€
                await Task.Run(() =>
                {
                    // Step 3: Voronoi
                    Dispatch(() => { UpdateProgress("Computing Voronoi diagramâ€¦"); Log("[STEP 3] Computing Voronoi diagramâ€¦"); });
                    _voronoiSvc.Compute(result.DrainGroups, result);
                    Dispatch(() =>
                    {
                        Log($"  Raw Voronoi edges:    {result.RawVoronoiEdges.Count}");
                        Log($"  Raw Voronoi vertices: {result.RawVoronoiVertices.Count}");
                    });

                    // Step 4: Clipping
                    Dispatch(() => { UpdateProgress("Clipping Voronoi to floor boundaryâ€¦"); Log("[STEP 4] Clipping Voronoi to floor boundaryâ€¦"); });
                    _clippingSvc.ClipAndCollect(result, boundary, result.DrainGroups);
                    Dispatch(() =>
                    {
                        Log($"  Clipped edges:  {result.ClippedEdges.Count}");
                        Log($"  Shape points:   {result.ShapePoints.Count}");
                    });

                    // Step 4a: Ridge edge mid-points (one per clipped Voronoi edge)
                    result.RidgeEdgeMidPoints = result.ClippedEdges
                        .Select(e => new XYZ((e.Start.X + e.End.X) / 2.0,
                                             (e.Start.Y + e.End.Y) / 2.0, 0.0))
                        .ToList();
                    Dispatch(() => Log($"  Ridge mid-points: {result.RidgeEdgeMidPoints.Count}"));

                    // Step 4b: Inner-loop Ã— ridge-line intersections (pure math â€” the
                    //          loops themselves were already extracted on the UI thread above)
                    if (result.InnerLoops.Count > 0)
                    {
                        Dispatch(() => Log("[STEP 4b] Computing inner-loop intersectionsâ€¦"));
                        int innerPtCount = _innerLoopIntersectionSvc.ComputeInnerLoopIntersections(result, boundary);
                        Dispatch(() => Log($"  Inner loop intersection points: {innerPtCount}"));
                    }

                    // Step 5: Validation
                    Dispatch(() => { UpdateProgress("Validating equidistanceâ€¦"); Log("[STEP 5] Validating equidistanceâ€¦"); });
                    _validationSvc.ToleranceFeet = DrainGroupingService.MmToFeet(ValidationToleranceMm);
                    _validationSvc.Validate(result, result.DrainGroups);
                    Dispatch(() =>
                    {
                        Log($"  Pass: {result.PassCount}   Fail: {result.FailCount}   Total: {result.ValidationLog.Count}");
                    });

                    _lastResult = result;
                });

                // â”€â”€ Step 6: Revit element creation (UI thread) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                UpdateProgress("Creating Revit elementsâ€¦");
                Log("[STEP 6] Creating Revit elementsâ€¦");

                // Wire up per-TX card callbacks before calling CreateAll
                _creationSvc.OnFirstResetDone = () =>
                {
                    FirstResetStatus = _creationSvc.FirstResetStatus;
                    Log($"  [TX-1] First Reset: {FirstResetStatus}");
                };

                _creationSvc.OnPointsDone = () =>
                {
                    TotalCalculatedPoints = _creationSvc.TotalCalculated;
                    TotalAddedPoints = _creationSvc.TotalAdded;
                    TotalFailedPoints = _creationSvc.TotalFailed;
                    TotalSkippedPoints = _creationSvc.TotalSkipped;
                    Log($"  [TX-2] Points â€” Calculated: {TotalCalculatedPoints}  Added: {TotalAddedPoints}  " +
                        $"Failed: {TotalFailedPoints}  Skipped: {TotalSkippedPoints}");
                };

                _creationSvc.OnLastResetDone = () =>
                {
                    LastResetStatus = _creationSvc.LastResetStatus;
                    Log($"  [TX-3] Last Reset: {LastResetStatus}");
                };

                // Transactions TX-0..3 — must run in API context via the ExternalEvent.
                await RunInRevitAsync(() =>
                    _creationSvc.CreateAll(_doc, _uiDoc.ActiveView, _selectedFloor, _lastResult,
                        AddDetailLines, AddShapePoints));

                DetailLinesAddedCount = _creationSvc.DetailLinesAdded;
                Log($"  Detail lines created: {DetailLinesAddedCount}");

                // â”€â”€ Populate validation log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                foreach (var entry in _lastResult.ValidationLog)
                    ValidationLog.Add(entry);

                PassCount = _lastResult.PassCount;
                FailCount = _lastResult.FailCount;

                var elapsed = DateTime.Now - runStart;
                Log($"[END]  Elapsed: {elapsed.TotalSeconds:F2}s");

                StatusMessage =
                    $"Done. {DetailLinesAddedCount} detail lines, " +
                    $"{TotalAddedPoints}/{TotalCalculatedPoints} shape points added" +
                    (TotalSkippedPoints > 0 ? $" ({TotalSkippedPoints} skipped)" : "") + ". " +
                    $"Validation: {PassCount} PASS / {FailCount} FAIL. ({elapsed.TotalSeconds:F1}s)";
            }
            catch (Exception ex)
            {
                Log($"[ERROR]  {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Log($"  Inner: {ex.InnerException.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                ProgressDetail = string.Empty;
            }
        }

        private bool CanRun() => _selectedFloor != null && DrainCount >= 2 && ProximityDistanceMm > 0
            && (AddDetailLines || AddShapePoints);

        // â”€â”€ Log helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void Log(string message)
        {
            void Append()
            {
                PipelineLog.Add(message);
                PipelineLogText = PipelineLogText.Length == 0
                    ? message
                    : PipelineLogText + "\r\n" + message;
            }
            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
                Append();
            else
                System.Windows.Application.Current?.Dispatcher?.Invoke(Append);
        }

        /// <summary>
        /// Runs <paramref name="work"/> inside Revit's API context (via the ExternalEvent)
        /// and completes when it has run. Fails fast if the tool's document was closed or
        /// is no longer the active one, since PickObject / ActiveView depend on it.
        /// </summary>
        private Task RunInRevitAsync(Action work) => _revitApi.InvokeAsync(app =>
        {
            if (!_doc.IsValidObject)
                throw new InvalidOperationException("The document this tool was opened on has been closed.");
            var activeDoc = app.ActiveUIDocument?.Document;
            if (activeDoc == null || !activeDoc.Equals(_doc))
                throw new InvalidOperationException($"Switch back to '{_doc.Title}' — this tool is working on that document.");
            work();
        });

        /// <summary>
        /// The window is modeless, so the floor may have been deleted since it was picked.
        /// Call from inside <see cref="RunInRevitAsync"/>.
        /// </summary>
        private void EnsureSelectedFloorValid()
        {
            if (_selectedFloor.IsValidObject) return;
            _selectedFloor = null;
            FloorDescription = "No floor selected";
            throw new InvalidOperationException("The selected floor no longer exists. Select a floor again.");
        }

        private static void Dispatch(Action action)
            => System.Windows.Application.Current?.Dispatcher?.Invoke(action);

        private void UpdateProgress(string message) => ProgressDetail = message;

        // â”€â”€ Floor selection filter â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private class FloorSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) =>
                elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
