using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Models;
using Revit26_Plugin_Revised.RoofRidgeLines.V070.Services;

namespace Revit26_Plugin_Revised.RoofRidgeLines.V070.ViewModels
{
    public partial class RoofRidgeViewModel : ObservableObject
    {
        // â”€â”€ Services â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;

        /// <summary>
        /// Routes Revit API work into API context — the window is modeless, so command
        /// handlers here run outside it and cannot start transactions directly.
        /// </summary>
        private readonly RevitApiDispatcher _revitApi;

        /// <summary>Names of available projection line styles, for the "Detail line style" ComboBox.</summary>
        public ObservableCollection<string> LineStyleOptions { get; }
            = new ObservableCollection<string>();

        /// <summary>Filtered view over LineStyleOptions â€” narrows as the user types (search-as-you-type).</summary>
        public ICollectionView LineStyleView { get; }
        private readonly DrainGroupingService _groupingSvc;
        private readonly VoronoiComputationService _voronoiSvc;
        private readonly RoofBoundaryService _boundarySvc;
        private readonly VoronoiClippingService _clippingSvc;
        private readonly RidgeValidationService _validationSvc;
        private readonly RidgeCreationService _creationSvc;
        private readonly InnerLoopService _innerLoopSvc;
        private readonly InnerLoopIntersectionService _innerLoopIntersectionSvc;

        // â”€â”€ Internal state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private RoofBase _selectedRoof;
        private VoronoiRidgeResult _lastResult;
        private Window _ownerWindow;

        // â”€â”€ Pipeline log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<string> PipelineLog { get; }
            = new ObservableCollection<string>();

        [ObservableProperty]
        private string _pipelineLogText = string.Empty;

        // â”€â”€ Constructors â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Primary constructor. If a roof is provided, it loads the openings immediately.
        /// </summary>
        /// <summary>Tool version shown in the window header and footer.</summary>
        public string ToolName => "Ridge By Openings";
        public string ToolVersion => "V070";

        /// <summary>Plugin assembly version and build timestamp, for the version footer.</summary>
        public string VersionDetails { get; } = BuildVersionDetails();

        private static string BuildVersionDetails()
        {
            try
            {
                var asm = typeof(RoofRidgeViewModel).Assembly;
                string built = System.IO.File.GetLastWriteTime(asm.Location).ToString("yyyy-MM-dd HH:mm");
                return $"Ridge By Openings V070  |  assembly {asm.GetName().Version}  |  built {built}";
            }
            catch
            {
                return "Ridge By Openings V070";
            }
        }

        public RoofRidgeViewModel(UIDocument uiDoc, RevitApiDispatcher revitApi, RoofBase roof = null)
        {
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _doc = uiDoc.Document;
            _revitApi = revitApi ?? throw new ArgumentNullException(nameof(revitApi));
            _groupingSvc = new DrainGroupingService();
            _voronoiSvc = new VoronoiComputationService();
            _boundarySvc = new RoofBoundaryService();
            _clippingSvc = new VoronoiClippingService();
            _validationSvc = new RidgeValidationService();
            _creationSvc = new RidgeCreationService();
            _innerLoopSvc = new InnerLoopService();
            _innerLoopIntersectionSvc = new InnerLoopIntersectionService();

            // Monitor collection changes to wire up property changed notifications on items
            Openings.CollectionChanged += OnOpeningsCollectionChanged;

            // Populate the "Detail line style" ComboBox â€” same filter as TX-01's
            // fallback (GetFirstLineStyleId): projection-type styles with a subcategory.
            // Only names are kept here; the ElementId is looked up by name at Run time.
            foreach (var s in RidgeCreationService.GetAvailableLineStyles(_doc))
                LineStyleOptions.Add(s.Name);

            LineStyleView = CollectionViewSource.GetDefaultView(LineStyleOptions);
            LineStyleView.Filter = FilterLineStyles;

            if (LineStyleOptions.Count > 0)
                SelectedLineStyleName = LineStyleOptions[0];

            // If a roof was preâ€‘selected in the command, load its openings immediately.
            if (roof != null)
            {
                _selectedRoof = roof;
                RoofDescription = $"Roof: {_selectedRoof.Name}  (Id {_selectedRoof.Id})";
                LoadOpeningsForCurrentRoof(); // <-- populates the grid right away
                StatusMessage = $"Loaded {Openings.Count} inner loops. Select at least 2 as drainage seeds.";
            }
            else
            {
                RoofDescription = "No roof selected";
                StatusMessage = "Select a roof using the 'Select Roof' button.";
            }
        }

        public void SetOwnerWindow(Window window) => _ownerWindow = window;

        // â”€â”€ Observable properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private string _roofDescription = "No roof selected";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private int _selectedOpeningsCount;

        [ObservableProperty]
        private double _proximityDistanceMm = 50.0;

        [ObservableProperty]
        private double _validationToleranceMm = 50.0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool _addDetailLines = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool _addShapePoints = false;

        [ObservableProperty]
        private string _selectedLineStyleName;

        [ObservableProperty]
        private string _lineStyleSearchText;

        partial void OnLineStyleSearchTextChanged(string value) => LineStyleView?.Refresh();

        private bool FilterLineStyles(object obj)
            => string.IsNullOrEmpty(LineStyleSearchText)
               || ((string)obj).IndexOf(LineStyleSearchText, StringComparison.OrdinalIgnoreCase) >= 0;

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

        // â”€â”€ 3 Status Cards â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [ObservableProperty]
        private int _totalCalculatedPoints;

        [ObservableProperty]
        private int _totalAddedPoints;

        [ObservableProperty]
        private int _totalFailedPoints;

        // â”€â”€ Validation log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public ObservableCollection<ValidationEntry> ValidationLog { get; }
            = new ObservableCollection<ValidationEntry>();

        // â”€â”€ Openings collection â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// List of all inner loops (openings) analysed and displayed in the UI grid.
        /// </summary>
        public ObservableCollection<OpeningData> Openings { get; }
            = new ObservableCollection<OpeningData>();

        // â”€â”€ Grouped opening collections (UI grouping only) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // These are views over the SAME OpeningData instances held in Openings.
        // Selection state lives on each OpeningData.IsSelected, so the Run() pipeline
        // (which filters Openings.Where(o => o.IsSelected)) is unaffected.

        /// <summary>Inner loops detected as circles.</summary>
        public ObservableCollection<OpeningData> CircleOpenings { get; }
            = new ObservableCollection<OpeningData>();

        /// <summary>Inner loops detected as rectangles.</summary>
        public ObservableCollection<OpeningData> RectangleOpenings { get; }
            = new ObservableCollection<OpeningData>();

        /// <summary>Inner loops that are neither circles nor rectangles.</summary>
        public ObservableCollection<OpeningData> OtherOpenings { get; }
            = new ObservableCollection<OpeningData>();

        // â”€â”€ Per-group live counts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Number of selected circle seeds (for the group header).</summary>
        [ObservableProperty]
        private int _circleSelectedCount;

        /// <summary>Total number of circle openings.</summary>
        [ObservableProperty]
        private int _circleTotalCount;

        /// <summary>Number of selected rectangle seeds (for the group header).</summary>
        [ObservableProperty]
        private int _rectangleSelectedCount;

        /// <summary>Total number of rectangle openings.</summary>
        [ObservableProperty]
        private int _rectangleTotalCount;

        /// <summary>Number of selected "other" seeds (for the group header).</summary>
        [ObservableProperty]
        private int _otherSelectedCount;

        /// <summary>Total number of "other" openings.</summary>
        [ObservableProperty]
        private int _otherTotalCount;

        // â”€â”€ Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [RelayCommand]
        private async Task SelectRoof()
        {
            try
            {
                _ownerWindow?.Hide();
                StatusMessage = "Click on a Roof element in the viewâ€¦";

                // PickObject needs API context — run it through the ExternalEvent.
                await RunInRevitAsync(() =>
                {
                    var ref_ = _uiDoc.Selection.PickObject(
                        ObjectType.Element,
                        new RoofSelectionFilter(),
                        "Select a Roof");

                    _selectedRoof = _doc.GetElement(ref_) as RoofBase;
                    if (_selectedRoof == null)
                    {
                        RoofDescription = "Selection failed â€” please retry";
                        StatusMessage = "Roof selection cancelled or invalid.";
                        return;
                    }
                    RoofDescription = $"Roof: {_selectedRoof.Name}  (Id {_selectedRoof.Id})";

                    // Autoâ€‘load openings for the newly selected roof
                    LoadOpeningsForCurrentRoof();
                });
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                StatusMessage = "Roof selection cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error selecting roof: {ex.Message}";
            }
            finally
            {
                _ownerWindow?.Show();
            }
        }

        [RelayCommand]
        private void SelectAllOpenings()
        {
            foreach (var opening in Openings)
            {
                opening.IsSelected = true;
            }
            UpdateSelectedCount();
        }

        [RelayCommand]
        private void SelectNoOpenings()
        {
            foreach (var opening in Openings)
            {
                opening.IsSelected = false;
            }
            UpdateSelectedCount();
        }

        [RelayCommand]
        private void InverseOpeningSelection()
        {
            foreach (var opening in Openings)
            {
                opening.IsSelected = !opening.IsSelected;
            }
            UpdateSelectedCount();
        }

        // â”€â”€ Per-group selection commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Selects every circle opening as a seed.</summary>
        [RelayCommand]
        private void SelectAllCircles() => SetGroupSelection(CircleOpenings, true);

        /// <summary>Deselects every circle opening.</summary>
        [RelayCommand]
        private void SelectNoneCircles() => SetGroupSelection(CircleOpenings, false);

        /// <summary>Selects every rectangle opening as a seed.</summary>
        [RelayCommand]
        private void SelectAllRectangles() => SetGroupSelection(RectangleOpenings, true);

        /// <summary>Deselects every rectangle opening.</summary>
        [RelayCommand]
        private void SelectNoneRectangles() => SetGroupSelection(RectangleOpenings, false);

        /// <summary>Selects every "other" opening as a seed.</summary>
        [RelayCommand]
        private void SelectAllOthers() => SetGroupSelection(OtherOpenings, true);

        /// <summary>Deselects every "other" opening.</summary>
        [RelayCommand]
        private void SelectNoneOthers() => SetGroupSelection(OtherOpenings, false);

        /// <summary>Sets <see cref="OpeningData.IsSelected"/> for every item in a group.</summary>
        private void SetGroupSelection(ObservableCollection<OpeningData> group, bool isSelected)
        {
            foreach (var opening in group)
                opening.IsSelected = isSelected;
            UpdateSelectedCount();
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
            sb.AppendLine("  ROOF RIDGE LINES V60 â€“ PIPELINE LOG");
            sb.AppendLine($"  Copied: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
            foreach (var line in PipelineLog) sb.AppendLine(line);

            if (ValidationLog.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
                sb.AppendLine("  VALIDATION TABLE  (tabâ€‘separated â€” paste into Excel)");
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

            // Reset outcome tracking cards
            TotalAddedPoints = 0;
            TotalFailedPoints = 0;

            var runStart = DateTime.Now;
            Log($"[START]  {runStart:HH:mm:ss}");
            Log($"[INPUT]  {RoofDescription}");  // cached — the roof may have been deleted since it was picked
            Log($"[INPUT]  Proximity distance: {ProximityDistanceMm} mm");
            Log($"[INPUT]  Validation tolerance: {ValidationToleranceMm} mm");

            try
            {
                // â”€â”€ Step 1: Extract boundary and inner loops â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                // These call Revit API and must run in API context (via the ExternalEvent).
                UpdateProgress("Extracting roof boundaryâ€¦");
                Log("[STEP 1] Extracting roof boundaryâ€¦");
                List<XYZ> boundary = null;
                List<List<XYZ>> innerLoops = null;
                await RunInRevitAsync(() =>
                {
                    // The window is modeless, so the roof may have been deleted since it was picked.
                    if (!_selectedRoof.IsValidObject)
                    {
                        _selectedRoof = null;
                        RoofDescription = "No roof selected";
                        throw new InvalidOperationException("The selected roof no longer exists. Select a roof again.");
                    }
                    boundary = _boundarySvc.ExtractBoundary(_selectedRoof);
                    innerLoops = _innerLoopSvc.ExtractInnerLoops(_selectedRoof);
                });

                // Store boundary + centroid for AddPoint fallback
                var result = new VoronoiRidgeResult();
                result.BoundaryPolygon = boundary;
                result.RoofCentroid = new XYZ(
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
                Log("[STEP 2] Detecting inner loopsâ€¦");
                result.InnerLoops = innerLoops;
                Log($"  Inner loops found: {result.InnerLoops.Count}");

                // â”€â”€ Get user selection â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                var selected = Openings.Where(o => o.IsSelected).ToList();
                if (selected.Count < 2)
                {
                    StatusMessage = "Please select at least 2 inner loops as drainage seeds.";
                    IsBusy = false;
                    return;
                }
                SelectedOpeningsCount = selected.Count;
                Log($"  Selected {selected.Count} loops as seeds.");

                // Extract centroids â†’ these become the Voronoi sites
                var drainPoints = selected.Select(o => o.Centroid).ToList();

                // Build the list of loops that ridges should terminate on (unselected loops)
                var loopsToIntersect = result.InnerLoops
                    .Where(loop => !selected.Any(sel => AreLoopsEqual(loop, sel.Vertices)))
                    .ToList();
                Log($"  {loopsToIntersect.Count} unselected loops will terminate ridges.");

                // â”€â”€ Steps 3â€“5: Voronoi, Clipping, Validation (background thread) â”€â”€
                await Task.Run(() =>
                {
                    // Step 3: Drain grouping (using the selected centroids)
                    Dispatch(() => { UpdateProgress("Grouping drain seedsâ€¦"); Log("[STEP 3] Grouping drain seedsâ€¦"); });
                    double proxFeet = DrainGroupingService.MmToFeet(ProximityDistanceMm);
                    var drainGroups = _groupingSvc.GroupDrains(drainPoints, proxFeet);
                    result.DrainGroups = drainGroups;
                    Dispatch(() =>
                    {
                        GroupCountDisplay = drainGroups.Count.ToString();
                        Log($"  Groups formed: {drainGroups.Count}");
                        foreach (var g in drainGroups)
                            Log($"  Group[{g.GroupIndex}]  Drains={g.DrainLocations.Count}  Centroid X={g.Centroid.X:F4} ft  Y={g.Centroid.Y:F4} ft");
                        if (drainGroups.Count < 2)
                            throw new InvalidOperationException("At least two distinct drain groups are required.");
                    });

                    // Step 4: Voronoi
                    Dispatch(() => { UpdateProgress("Computing Voronoi diagramâ€¦"); Log("[STEP 4] Computing Voronoi diagramâ€¦"); });
                    _voronoiSvc.Compute(result.DrainGroups, result);
                    Dispatch(() =>
                    {
                        Log($"  Raw Voronoi edges:    {result.RawVoronoiEdges.Count}");
                        Log($"  Raw Voronoi vertices: {result.RawVoronoiVertices.Count}");
                    });

                    // Step 5: Clipping
                    Dispatch(() => { UpdateProgress("Clipping Voronoi to roof boundaryâ€¦"); Log("[STEP 5] Clipping Voronoi to roof boundaryâ€¦"); });
                    _clippingSvc.ClipAndCollect(result, boundary, result.DrainGroups);
                    Dispatch(() =>
                    {
                        Log($"  Clipped edges:  {result.ClippedEdges.Count}");
                        Log($"  Shape points:   {result.ShapePoints.Count}");
                    });

                    // Step 5a: Ridge edge midâ€‘points
                    result.RidgeEdgeMidPoints = result.ClippedEdges
                        .Select(e => new XYZ((e.Start.X + e.End.X) / 2.0,
                                             (e.Start.Y + e.End.Y) / 2.0, 0.0))
                        .ToList();
                    Dispatch(() => Log($"  Ridge mid-points: {result.RidgeEdgeMidPoints.Count}"));

                    // Step 5b: Innerâ€‘loop intersections (only on unselected loops â€“ crossâ€‘over)
                    if (loopsToIntersect.Count > 0)
                    {
                        Dispatch(() => Log("[STEP 5b] Computing inner-loop intersections (unselected only)â€¦"));
                        int innerPtCount = _innerLoopIntersectionSvc.ComputeInnerLoopIntersections(
                            result, boundary, loopsToIntersect);
                        Dispatch(() => Log($"  Inner loop intersection points: {innerPtCount}"));
                    }
                    else
                    {
                        Dispatch(() => Log("[STEP 5b] All loops selected â€” no termination points added."));
                    }

                    // Step 6: Validation
                    Dispatch(() => { UpdateProgress("Validating equidistanceâ€¦"); Log("[STEP 6] Validating equidistanceâ€¦"); });
                    _validationSvc.ToleranceFeet = DrainGroupingService.MmToFeet(ValidationToleranceMm);
                    _validationSvc.Validate(result, result.DrainGroups);
                    Dispatch(() =>
                    {
                        Log($"  Pass: {result.PassCount}   Fail: {result.FailCount}   Total: {result.ValidationLog.Count}");
                    });

                    _lastResult = result;
                });

                // â”€â”€ Step 7: Revit element creation (UI thread) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                UpdateProgress("Creating Revit elementsâ€¦");
                Log("[STEP 7] Creating Revit elementsâ€¦");

                _creationSvc.OnPointsDone = () =>
                {
                    TotalAddedPoints = _creationSvc.TotalAdded;
                    TotalFailedPoints = _creationSvc.TotalFailed;
                    Log($"  [TX-02] Points â€” Calculated: {_creationSvc.TotalCalculated}  Added: {TotalAddedPoints}  Failed: {TotalFailedPoints}");
                };

                // Transactions TX-01..03 — must run in API context via the ExternalEvent.
                await RunInRevitAsync(() =>
                {
                    ElementId selectedLineStyleId = RidgeCreationService.GetLineStyleIdByName(_doc, SelectedLineStyleName);

                    // One group around TX-01..03, so a cancel undoes the whole run.
                    using var group = new TransactionGroup(_doc, "Ridge By Openings");
                    group.Start();
                    try
                    {
                        _creationSvc.CreateAll(_doc, _uiDoc.ActiveView, _selectedRoof, _lastResult, AddDetailLines, AddShapePoints, selectedLineStyleId);
                        ToolCancellation.CheckCurrent();
                    }
                    catch
                    {
                        if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                        throw;
                    }
                    group.Assimilate();
                });

                Log($"  Detail lines created: {_lastResult.CreatedDetailLineIds.Count}");

                // â”€â”€ Populate validation log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                foreach (var entry in _lastResult.ValidationLog)
                    ValidationLog.Add(entry);

                PassCount = _lastResult.PassCount;
                FailCount = _lastResult.FailCount;

                var elapsed = DateTime.Now - runStart;
                Log($"[END]  Elapsed: {elapsed.TotalSeconds:F2}s");

                StatusMessage =
                    $"Done. {_lastResult.ClippedEdges.Count} ridge lines, " +
                    $"{TotalAddedPoints}/{_creationSvc.TotalCalculated} shape points added. " +
                    $"Validation: {PassCount} PASS / {FailCount} FAIL. ({elapsed.TotalSeconds:F1}s)";
            }
            catch (OperationCanceledException)
            {
                // Esc / Cancel confirmed: the transaction group inside CreateAll was rolled back.
                Log("[CANCELLED]  Cancelled by user. Everything this run changed was undone.");
                StatusMessage = "Cancelled by user. Nothing was changed.";
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

        private bool CanRun() => _selectedRoof != null && SelectedOpeningsCount >= 2 && ProximityDistanceMm > 0 && (AddDetailLines || AddShapePoints);

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static bool AreLoopsEqual(List<XYZ> a, List<XYZ> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].DistanceTo(b[i]) > 1e-6) return false;
            return true;
        }

        /// <summary>
        /// Extracts inner loops from the current roof, analyses them,
        /// and populates the Openings collection. Updates log and status.
        /// </summary>
        private void LoadOpeningsForCurrentRoof()
        {
            if (_selectedRoof == null)
            {
                StatusMessage = "No roof selected. Please select a roof first.";
                return;
            }

            try
            {
                StatusMessage = "Extracting inner loops from roof...";
                var loops = _innerLoopSvc.ExtractInnerLoops(_selectedRoof);

                Openings.Clear();
                for (int i = 0; i < loops.Count; i++)
                {
                    var loop = loops[i];
                    var data = OpeningAnalyzerService.AnalyzeLoop(loop);
                    data.Index = i;
                    Openings.Add(data);
                }

                SelectSmallestCirclesOnly();

                RebuildGroupedCollections();

                // Log to Pipeline Log
                string logMsg = $"[OPENINGS] Loaded {Openings.Count} inner loop(s).";
                PipelineLog.Add(logMsg);
                PipelineLogText = PipelineLogText.Length == 0
                    ? logMsg
                    : PipelineLogText + "\r\n" + logMsg;

                UpdateSelectedCount();
                StatusMessage = $"Loaded {Openings.Count} inner loops. Smallest-radius circles are pre-selected; tick at least 2 to use as drainage seeds.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load openings: {ex.Message}";
                PipelineLog.Add($"[ERROR] {ex.Message}");
            }
        }

        /// <summary>
        /// Default selection: only the circular openings with the lowest radius are ticked
        /// (within 0.5 mm); rectangles and other shapes start unticked.
        /// </summary>
        private void SelectSmallestCirclesOnly()
        {
            const double toleranceFt = 0.5 / 304.8;
            var circles = Openings.Where(o => o.ShapeType == OpeningShapeType.Circle).ToList();
            double minRadius = circles.Count > 0 ? circles.Min(o => o.Dim1) : 0;
            foreach (var o in Openings)
                o.IsSelected = o.ShapeType == OpeningShapeType.Circle && o.Dim1 <= minRadius + toleranceFt;
        }

        private void OnOpeningsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (INotifyPropertyChanged item in e.NewItems.OfType<INotifyPropertyChanged>())
                    item.PropertyChanged += OnOpeningItemPropertyChanged;
            }
            if (e.OldItems != null)
            {
                foreach (INotifyPropertyChanged item in e.OldItems.OfType<INotifyPropertyChanged>())
                    item.PropertyChanged -= OnOpeningItemPropertyChanged;
            }
            UpdateSelectedCount();
        }

        private void OnOpeningItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OpeningData.IsSelected))
            {
                UpdateSelectedCount();
            }
        }

        /// <summary>
        /// Re-partitions <see cref="Openings"/> into the three grouped collections by
        /// <see cref="OpeningData.ShapeType"/>. The grouped collections reference the same
        /// <see cref="OpeningData"/> instances, so selection state stays shared.
        /// </summary>
        private void RebuildGroupedCollections()
        {
            CircleOpenings.Clear();
            RectangleOpenings.Clear();
            OtherOpenings.Clear();

            foreach (var opening in Openings)
            {
                switch (opening.ShapeType)
                {
                    case OpeningShapeType.Circle:
                        CircleOpenings.Add(opening);
                        break;
                    case OpeningShapeType.Rectangle:
                        RectangleOpenings.Add(opening);
                        break;
                    default:
                        OtherOpenings.Add(opening);
                        break;
                }
            }

            CircleTotalCount = CircleOpenings.Count;
            RectangleTotalCount = RectangleOpenings.Count;
            OtherTotalCount = OtherOpenings.Count;
        }

        private void UpdateSelectedCount()
        {
            SelectedOpeningsCount = Openings.Count(o => o.IsSelected);
            // Repurpose the first status card to track the live selection state directly
            TotalCalculatedPoints = SelectedOpeningsCount;

            // Per-group header counts
            CircleSelectedCount = CircleOpenings.Count(o => o.IsSelected);
            RectangleSelectedCount = RectangleOpenings.Count(o => o.IsSelected);
            OtherSelectedCount = OtherOpenings.Count(o => o.IsSelected);
        }

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

        private static void Dispatch(Action action)
            => System.Windows.Application.Current?.Dispatcher?.Invoke(action);

        private void UpdateProgress(string message) => ProgressDetail = message;

        // â”€â”€ Roof selection filter â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private class RoofSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is RoofBase;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
