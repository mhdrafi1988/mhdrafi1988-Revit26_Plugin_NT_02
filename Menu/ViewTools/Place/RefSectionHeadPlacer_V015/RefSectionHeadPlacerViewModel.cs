using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Core.Engine;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Core.Models;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Core.Services;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.RefSectionHeadPlacer.V015.UI.ViewModels
{
    /// <summary>
    /// ViewModel for RefSectionHeadPlacerWindow.
    ///
    /// MEMORY: implements IDisposable. The ViewModel subscribes to the long-lived
    /// ExternalEvent handler's events; if it never unsubscribed, the handler would
    /// keep the ViewModel (and every collection/LogEntry) alive after the window
    /// closed â€” a classic WPF/Revit leak. Dispose() unsubscribes and disposes the
    /// ExternalEvent. The window calls Dispose() in OnClosed.
    ///
    /// THREADING: engine callbacks arrive on Revit's API thread. We marshal to the
    /// UI thread with the dispatcher captured at construction (the VM is built on
    /// the UI thread). System.Windows.Application.Current is NOT used â€” it is frequently null in a
    /// Revit add-in, which would NRE.
    /// </summary>
    public partial class RefSectionHeadPlacerViewModel : ObservableObject, IDisposable
    {
        private const int MaxLogEntries = 2000; // cap so an all-day modeless session can't grow unbounded

        private readonly Document _doc;
        private readonly ElementCollectorService _collectorService;
        private readonly PlaceSectionsEventHandler _eventHandler;
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _uiDispatcher;

        private List<PlanViewRow> _allPlanViews = new();
        private bool _disposed;

        /// <summary>Raised when the Close command runs; the window subscribes and closes itself.</summary>
        public event Action CloseRequested;

        // â”€â”€ Selectors â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<ViewFamilyType> SectionTypes { get; } = new();
        [ObservableProperty] private ViewFamilyType selectedSectionType;

        // â”€â”€ Reference section tail length (head->tail marker length) â”€â”€
        private const double DefaultTailLengthMm = 2000.0;
        private const double MmPerFoot = 304.8;

        [ObservableProperty]
        private string tailLengthMmText = DefaultTailLengthMm.ToString("F0");

        /// <summary>Parsed, validated tail length in mm. Falls back to the 2000 mm default
        /// on empty/invalid/non-positive input rather than blocking Run â€” flagged via log.</summary>
        public double TailLengthMm =>
            double.TryParse(TailLengthMmText, out var v) && v > 0 ? v : DefaultTailLengthMm;

        // â”€â”€ Boundary buffer (roof bounding-box expansion for Grid 2 link scoping) â”€â”€
        private const double DefaultBoundaryBufferMm = 100.0;

        [ObservableProperty]
        private string boundaryBufferMmText = DefaultBoundaryBufferMm.ToString("F0");

        /// <summary>Parsed, validated boundary buffer in mm. Falls back to the 100 mm default
        /// on empty/invalid/non-positive input â€” flagged via log, doesn't block Run.</summary>
        public double BoundaryBufferMm =>
            double.TryParse(BoundaryBufferMmText, out var v) && v > 0 ? v : DefaultBoundaryBufferMm;

        /// <summary>Grid 2 header meta text â€” reflects the live buffer value.</summary>
        public string ElementScopeSummary => $"roofs + linked items within {BoundaryBufferMm:F0}mm";

        partial void OnBoundaryBufferMmTextChanged(string value)
        {
            OnPropertyChanged(nameof(ElementScopeSummary));
            RefreshElementTypes();
        }

        // â”€â”€ Roof type filter (popover) â€” narrows which roofs anchor the boundary buffer â”€â”€
        private List<RoofTypeOption> _allRoofTypeOptions = new();
        private bool _suppressRoofTypeRecalc;

        /// <summary>Visible rows in the popover â€” narrowed by RoofTypeSearchText.</summary>
        public ObservableCollection<RoofTypeOption> RoofTypeOptions { get; } = new();

        [ObservableProperty]
        private string roofTypeSearchText = string.Empty;

        [ObservableProperty]
        private bool isRoofTypePopoverOpen;

        /// <summary>Button label â€” "All types" / "No types" / "2 of 3".</summary>
        public string RoofTypeSelectionSummary
        {
            get
            {
                var total = _allRoofTypeOptions.Count;
                if (total == 0) return "No types";
                var checkedCount = _allRoofTypeOptions.Count(t => t.IsChecked);
                if (checkedCount == total) return "All types";
                if (checkedCount == 0) return "No types";
                return $"{checkedCount} of {total}";
            }
        }

        public ObservableCollection<LinkRow> Links { get; } = new();
        public string LinkSelectionSummary =>
            $"{Links.Count(l => l.IsSelected)} of {Links.Count} selected";

        // â”€â”€ Grid 1: Plan views â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<PlanViewRow> PlanViews { get; } = new();
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlanViewSelectionSummary))]
        private string planViewFilter = string.Empty;
        public string PlanViewSelectionSummary =>
            $"{_allPlanViews.Count(v => v.IsSelected)} of {_allPlanViews.Count} selected";

        // â”€â”€ Grid 1: Plan Type filter (popover) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private List<PlanTypeOption> _allPlanTypeOptions = new();
        private bool _suppressPlanTypeRecalc;

        // BUGFIX (V003): without this, toggling N plan-view checkboxes in a batch
        // (initial load's default-select-all, or SelectAllPlanViews/ClearPlanViews)
        // fired RefreshElementTypes() once PER row instead of once for the whole
        // batch. Each of those rescans rebuilds _allCategoryMappingRows from
        // scratch (see RebuildMappings), replacing every CategoryMappingRow
        // instance. If a rescan lands after the user has already picked a
        // drafting view in Grid 3 but before Run is clicked, the pick is silently
        // dropped because it lives on an object that's already been replaced.
        // This mirrors the existing _suppressPlanTypeRecalc pattern below.
        private bool _suppressPlanViewRecalc;

        /// <summary>Visible rows in the popover â€” narrowed by PlanTypeSearchText.</summary>
        public ObservableCollection<PlanTypeOption> PlanTypeOptions { get; } = new();

        [ObservableProperty]
        private string planTypeSearchText = string.Empty;

        [ObservableProperty]
        private bool isPlanTypePopoverOpen;

        /// <summary>Button label â€” "All types" / "No types" / "2 of 4".</summary>
        public string PlanTypeSelectionSummary
        {
            get
            {
                var total = _allPlanTypeOptions.Count;
                if (total == 0) return "No types";
                var checkedCount = _allPlanTypeOptions.Count(t => t.IsChecked);
                if (checkedCount == total) return "All types";
                if (checkedCount == 0) return "No types";
                return $"{checkedCount} of {total}";
            }
        }

        // â”€â”€ Grid 2: Element categories & types â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private List<ElementTypeRow> _allElementTypeRows = new();
        public ObservableCollection<ElementTypeRow> ElementTypes { get; } = new();

        [ObservableProperty]
        private string elementTypeFilter = string.Empty;

        partial void OnElementTypeFilterChanged(string value) => ApplyElementTypeFilter();

        // â”€â”€ Grid 3: Type -> drafting view mapping (built from Grid 2 selections) â”€â”€
        // V005: session-level pick memory. Keyed by (SourceLabel, Bic, TypeName),
        // it outlives the mapping-row instances themselves. Before V005, a Grid 2
        // untick (or a type dropping out of a rescan) discarded its Grid 3 row;
        // when the type came back it was treated as brand-new. Now a returning
        // type restores exactly the view it was last mapped to. Cleared only when
        // the window closes.
        // V006: dropped the auto/manual flag â€” auto-match no longer exists, so
        // every entry here is by definition a user pick.
        private readonly Dictionary<(string SourceLabel, BuiltInCategory Bic, string TypeName),
            DraftingViewOption> _sessionPickMemory = new();

        private List<CategoryMappingRow> _allCategoryMappingRows = new();
        public ObservableCollection<CategoryMappingRow> CategoryMappings { get; } = new();
        public ObservableCollection<DraftingViewOption> DraftingViews { get; } = new();

        [ObservableProperty]
        private string categoryMappingFilter = string.Empty;

        partial void OnCategoryMappingFilterChanged(string value) => ApplyCategoryMappingFilter();

        // â”€â”€ Metrics â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty] private int elementCount;
        [ObservableProperty] private int mappedCount;
        [ObservableProperty] private int placedCount;
        [ObservableProperty] private int skippedCount;

        // â”€â”€ Run state / progress â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RunCommand))]
        private bool isRunning;
        [ObservableProperty] private string progressStatusText = string.Empty;
        [ObservableProperty] private string progressPercentText = "0%";
        [ObservableProperty] private double progressPercent;

        // â”€â”€ Log â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        public RefSectionHeadPlacerViewModel(Document doc)
        {
            _doc = doc;
            _uiDispatcher = Dispatcher.CurrentDispatcher;
            _collectorService = new ElementCollectorService(doc);

            _eventHandler = new PlaceSectionsEventHandler();
            _eventHandler.LogEmitted += OnEngineLog;
            _eventHandler.ProgressChanged += OnEngineProgress;
            _eventHandler.RunCompleted += OnRunCompleted;
            _eventHandler.RunFaulted += OnRunFaulted;
            _externalEvent = ExternalEvent.Create(_eventHandler);

            LoadInitialData();
        }

        // V003 CHANGE (confirmed): Grid 2/3 now populate immediately on window open
        // instead of waiting for a manual Grid 1 selection. Achieved by defaulting
        // every loaded link AND every loaded plan view to IsSelected = true before
        // the first RefreshElementTypes() call below. V002 left both defaulted to
        // false, which is why Grid 2/3 started empty.
        private void LoadInitialData()
        {
            foreach (var vft in GeometryHelper.GetSectionViewFamilyTypes(_doc))
                SectionTypes.Add(vft);
            SelectedSectionType = SectionTypes.FirstOrDefault();

            foreach (var link in _collectorService.LoadLinkInstances())
            {
                link.IsSelected = true; // V003: default-on so linked elements load without a manual step
                link.PropertyChanged += OnLinkRowChanged;
                Links.Add(link);
            }

            _allPlanViews = _collectorService.LoadPlanViews();
            foreach (var view in _allPlanViews)
                view.IsSelected = true; // V003: default-on so Grid 2/3 populate on load

            _allPlanTypeOptions = _allPlanViews
                .Select(v => v.ViewType)
                .Distinct()
                .OrderBy(t => t)
                .Select(t => new PlanTypeOption(t, FriendlyPlanType(t)))
                .ToList();
            foreach (var opt in _allPlanTypeOptions)
                opt.PropertyChanged += OnPlanTypeOptionChanged;
            ApplyPlanTypeSearch();

            ApplyPlanViewFilter();

            foreach (var dv in _collectorService.LoadDraftingViews())
                DraftingViews.Add(dv);

            Log(LogLevel.Info, Links.Count == 0
                ? "Ready. No loaded links found â€” link a model to target doors/plumbing/walls."
                : "Ready. All plan views and links selected by default â€” scanning now.");

            // V003: trigger the same pipeline SelectAllPlanViews would, so Grid 2/3
            // (element types + mapping) are populated before the window is shown,
            // not only after the user touches a checkbox.
            RefreshElementTypes();
        }

        partial void OnPlanViewFilterChanged(string value) => ApplyPlanViewFilter();
        private void OnLinkRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LinkRow.IsSelected))
            {
                OnPropertyChanged(nameof(LinkSelectionSummary));
                RefreshElementTypes();
            }
        }

        [RelayCommand]
        private void SelectAllLinks()
        {
            foreach (var l in Links) l.IsSelected = true;
        }

        [RelayCommand]
        private void ClearLinks()
        {
            foreach (var l in Links) l.IsSelected = false;
        }

        private void ApplyPlanViewFilter()
        {
            // Detach handlers from rows leaving the visible set, attach to incoming.
            foreach (var row in PlanViews) row.PropertyChanged -= OnPlanViewRowChanged;
            PlanViews.Clear();

            var nameFilter = PlanViewFilter?.Trim() ?? string.Empty;
            var checkedTypes = new HashSet<string>(
                _allPlanTypeOptions.Where(t => t.IsChecked).Select(t => t.RawViewType));

            foreach (var row in _allPlanViews)
            {
                bool matchesName = nameFilter.Length == 0 ||
                    row.ViewName.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchesType = checkedTypes.Contains(row.ViewType);

                if (matchesName && matchesType)
                {
                    row.PropertyChanged += OnPlanViewRowChanged;
                    PlanViews.Add(row);
                }
            }
            OnPropertyChanged(nameof(PlanViewSelectionSummary));
        }

        partial void OnPlanTypeSearchTextChanged(string value) => ApplyPlanTypeSearch();

        /// <summary>Narrows the popover's checkbox list by DisplayName â€” does NOT touch the main grid.</summary>
        private void ApplyPlanTypeSearch()
        {
            PlanTypeOptions.Clear();
            var search = PlanTypeSearchText?.Trim() ?? string.Empty;
            foreach (var opt in _allPlanTypeOptions)
            {
                if (search.Length == 0 || opt.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    PlanTypeOptions.Add(opt);
            }
        }

        private void OnPlanTypeOptionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlanTypeOption.IsChecked) && !_suppressPlanTypeRecalc)
            {
                OnPropertyChanged(nameof(PlanTypeSelectionSummary));
                ApplyPlanViewFilter();
            }
        }

        [RelayCommand]
        private void SelectAllPlanTypes() => SetAllPlanTypes(true);

        [RelayCommand]
        private void ClearPlanTypes() => SetAllPlanTypes(false);

        private void SetAllPlanTypes(bool value)
        {
            _suppressPlanTypeRecalc = true;
            foreach (var opt in _allPlanTypeOptions) opt.IsChecked = value;
            _suppressPlanTypeRecalc = false;
            OnPropertyChanged(nameof(PlanTypeSelectionSummary));
            ApplyPlanViewFilter();
        }

        /// <summary>Maps a raw Revit ViewType to a friendlier popover label. Falls back to
        /// inserting spaces before capitals for any ViewType not explicitly listed.</summary>
        private static string FriendlyPlanType(string rawViewType) => rawViewType switch
        {
            "FloorPlan" => "Floor Plan",
            "CeilingPlan" => "Ceiling Plan",
            "AreaPlan" => "Area Plan",
            "EngineeringPlan" => "Engineering Plan",
            _ => System.Text.RegularExpressions.Regex.Replace(rawViewType, "(?<!^)([A-Z])", " $1")
        };

        private void OnPlanViewRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlanViewRow.IsSelected) && !_suppressPlanViewRecalc)
                RefreshElementTypes();
        }

        [RelayCommand]
        private void SelectAllPlanViews()
        {
            _suppressPlanViewRecalc = true;
            foreach (var row in PlanViews) row.IsSelected = true;
            _suppressPlanViewRecalc = false;
            RefreshElementTypes();
        }

        [RelayCommand]
        private void ClearPlanViews()
        {
            _suppressPlanViewRecalc = true;
            foreach (var row in PlanViews) row.IsSelected = false;
            _suppressPlanViewRecalc = false;
            RefreshElementTypes();
        }

        /// <summary>Rebuilds the Roof Type popover from roofs found in currently selected
        /// plan views. Prior ticked/unticked state is preserved by matching on type name;
        /// new type names default to ticked (no filtering) like the Plan Type filter.</summary>
        private void RefreshRoofTypeOptions()
        {
            var previousChecked = _allRoofTypeOptions.ToDictionary(o => o.RawTypeName, o => o.IsChecked);
            foreach (var opt in _allRoofTypeOptions) opt.PropertyChanged -= OnRoofTypeOptionChanged;

            var selectedViews = _allPlanViews.Where(v => v.IsSelected).ToList();
            var names = _collectorService.GetRoofTypeNames(selectedViews);

            _allRoofTypeOptions = names
                .Select(n => new RoofTypeOption(n, !previousChecked.TryGetValue(n, out var wasChecked) || wasChecked))
                .ToList();
            foreach (var opt in _allRoofTypeOptions) opt.PropertyChanged += OnRoofTypeOptionChanged;

            ApplyRoofTypeSearch();
            OnPropertyChanged(nameof(RoofTypeSelectionSummary));
        }

        partial void OnRoofTypeSearchTextChanged(string value) => ApplyRoofTypeSearch();

        /// <summary>Narrows the popover's checkbox list by DisplayName â€” does NOT touch Grid 2.</summary>
        private void ApplyRoofTypeSearch()
        {
            RoofTypeOptions.Clear();
            var search = RoofTypeSearchText?.Trim() ?? string.Empty;
            foreach (var opt in _allRoofTypeOptions)
            {
                if (search.Length == 0 || opt.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    RoofTypeOptions.Add(opt);
            }
        }

        private void OnRoofTypeOptionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RoofTypeOption.IsChecked) && !_suppressRoofTypeRecalc)
            {
                OnPropertyChanged(nameof(RoofTypeSelectionSummary));
                RefreshElementTypes();
            }
        }

        [RelayCommand]
        private void SelectAllRoofTypes() => SetAllRoofTypes(true);

        [RelayCommand]
        private void ClearRoofTypes() => SetAllRoofTypes(false);

        private void SetAllRoofTypes(bool value)
        {
            _suppressRoofTypeRecalc = true;
            foreach (var opt in _allRoofTypeOptions) opt.IsChecked = value;
            _suppressRoofTypeRecalc = false;
            OnPropertyChanged(nameof(RoofTypeSelectionSummary));
            RefreshElementTypes();
        }

        /// <summary>Rebuilds Grid 2 from current view + link + roof-type selection, then Grid 3.
        /// Logs diagnostic Info/Warning entries at each stage so a "nothing collected" or
        /// "nothing placed" report can be traced back to its actual cause.</summary>
        public void RefreshElementTypes()
        {
            RefreshRoofTypeOptions();

            foreach (var row in _allElementTypeRows) row.PropertyChanged -= OnElementTypeRowChanged;

            var selectedViews = _allPlanViews.Where(v => v.IsSelected).ToList();
            var selectedLinks = Links.Where(l => l.IsSelected).ToList();

            if (selectedViews.Count == 0)
            {
                _allElementTypeRows = new List<ElementTypeRow>();
                ApplyElementTypeFilter();
                ElementCount = 0;
                RebuildMappings();
                OnPropertyChanged(nameof(PlanViewSelectionSummary));
                return;
            }

            var rawRoofCount = _collectorService.CountRoofsInViews(selectedViews);
            var allowedRoofTypeNames = new HashSet<string>(
                _allRoofTypeOptions.Where(t => t.IsChecked).Select(t => t.RawTypeName));

            Log(LogLevel.Info,
                $"Scan Â· {selectedViews.Count} view(s), {selectedLinks.Count} link(s) selected Â· " +
                $"{rawRoofCount} roof(s) found, {_allRoofTypeOptions.Count} distinct type(s), " +
                $"{allowedRoofTypeNames.Count} ticked Â· buffer {BoundaryBufferMm:F0}mm.");

            if (rawRoofCount == 0)
                Log(LogLevel.Warning, "No roofs found in the selected plan view(s) â€” check the view's crop region/view range, or select a different view.");
            else if (allowedRoofTypeNames.Count == 0)
                Log(LogLevel.Warning, "All roof types are unticked in the Roof type filter â€” nothing will anchor the boundary buffer.");

            // BUGFIX: CollectElementTypes() always returns brand-new ElementTypeRow
            // instances, which default IsSelected to false. Rebuilding this list on
            // every rescan (view/link/roof-type toggle) was therefore silently
            // un-checking every Grid 2 row â€” which in turn made RebuildMappings()
            // below see zero selected types and wipe Grid 3 back to empty, discarding
            // any drafting-view picks the user had already made. Fix: carry forward
            // each row's prior IsSelected by the same (SourceLabel, Bic, TypeName)
            // key RebuildMappings() already uses for Grid 3. Rows never seen before
            // default to selected=true, matching V003's "populate on load" intent.
            var previousSelection = _allElementTypeRows.ToDictionary(
                r => (r.SourceLabel, r.Bic, r.TypeName), r => r.IsSelected);

            _allElementTypeRows = _collectorService.CollectElementTypes(
                selectedViews, selectedLinks, allowedRoofTypeNames, BoundaryBufferMm);

            foreach (var row in _allElementTypeRows)
            {
                row.IsSelected = previousSelection.TryGetValue(
                    (row.SourceLabel, row.Bic, row.TypeName), out var wasSelected)
                    ? wasSelected
                    : true;
                row.PropertyChanged += OnElementTypeRowChanged;
            }

            var qualifyingRoofCount = _allElementTypeRows
                .Where(r => r.Bic == BuiltInCategory.OST_Roofs).Sum(r => r.Count);
            if (rawRoofCount > 0 && allowedRoofTypeNames.Count > 0 && qualifyingRoofCount == 0)
                Log(LogLevel.Warning, "Roof Type filter excluded every roof found â€” Grid 2 will be empty.");

            foreach (var linkRow in selectedLinks)
            {
                var linkRows = _allElementTypeRows.Where(r => r.SourceLabel == linkRow.Name).ToList();
                if (linkRows.Count == 0)
                {
                    Log(LogLevel.Info, $"Link '{linkRow.Name}' Â· 0 items collected (nothing within {BoundaryBufferMm:F0}mm of a qualifying roof).");
                    continue;
                }
                var byCategory = string.Join(", ", linkRows
                    .GroupBy(r => r.Category)
                    .Select(g => $"{g.Sum(r => r.Count)} {g.Key}"));
                Log(LogLevel.Info, $"Link '{linkRow.Name}' Â· {byCategory}.");
            }

            ApplyElementTypeFilter();

            ElementCount = _allElementTypeRows.Sum(t => t.Count);
            RebuildMappings();
            OnPropertyChanged(nameof(PlanViewSelectionSummary));
        }

        /// <summary>Narrows Grid 2's visible rows by ElementTypeFilter â€” does NOT touch selection state.</summary>
        private void ApplyElementTypeFilter()
        {
            ElementTypes.Clear();
            var filter = ElementTypeFilter?.Trim() ?? string.Empty;
            foreach (var row in _allElementTypeRows)
            {
                if (filter.Length == 0 ||
                    row.TypeName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.Category.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    ElementTypes.Add(row);
            }
        }

        [RelayCommand]
        private void SelectAllElementTypes()
        {
            foreach (var row in ElementTypes) row.IsSelected = true;
        }

        [RelayCommand]
        private void ClearElementTypes()
        {
            foreach (var row in ElementTypes) row.IsSelected = false;
        }

        private void OnElementTypeRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ElementTypeRow.IsSelected))
                RebuildMappings();
        }

        /// <summary>
        /// Grid 3 = one row per SELECTED Grid 2 type (across the FULL set, not just
        /// currently filtered/visible rows). Existing drafting-view picks are
        /// preserved across rebuilds by matching on (SourceLabel, Bic, TypeName),
        /// via _sessionPickMemory â€” so a rebuild never re-opens a row the user
        /// already mapped, and a pick survives its row being dropped (Grid 2
        /// untick / rescan) and restores when the type returns.
        ///
        /// V006 (confirmed, Rafi): auto-match removed entirely. A type never seen
        /// this session simply starts BLANK â€” the user must map it manually in the
        /// Grid 3 ComboBox. Nothing is guessed from Category/TypeName any more.
        /// </summary>
        private void RebuildMappings()
        {
            // Fold the outgoing rows' current picks into session memory FIRST, so
            // the newest user picks win, then rebuild against the memory â€” not
            // against only the rows that happened to survive the last rebuild.
            foreach (var m in _allCategoryMappingRows)
                _sessionPickMemory[(m.SourceLabel, m.Bic, m.TypeName)] = m.MappedDraftingView;

            foreach (var m in _allCategoryMappingRows) m.PropertyChanged -= OnMappingRowChanged;

            _allCategoryMappingRows = _allElementTypeRows.Where(t => t.IsSelected).Select(t =>
            {
                // Type mapped before this session (even if its row was dropped in
                // between) -> restore exactly what it last had. Otherwise blank â€”
                // no auto-match; the user maps it manually.
                _sessionPickMemory.TryGetValue((t.SourceLabel, t.Bic, t.TypeName), out var assigned);

                var row = new CategoryMappingRow(t.SourceLabel, t.Category, t.TypeName, t.Bic, assigned);
                row.SetAvailableDraftingViews(DraftingViews); // V010: gives the row's ComboBox drop-down something to search/filter (was the popover's ListBox in V008)
                row.PropertyChanged += OnMappingRowChanged;
                return row;
            }).ToList();

            ApplyCategoryMappingFilter();
            RecomputeMappedCount();
        }

        /// <summary>Narrows Grid 3's visible rows by CategoryMappingFilter â€” does NOT touch selection state.</summary>
        private void ApplyCategoryMappingFilter()
        {
            CategoryMappings.Clear();
            var filter = CategoryMappingFilter?.Trim() ?? string.Empty;
            foreach (var row in _allCategoryMappingRows)
            {
                if (filter.Length == 0 ||
                    row.TypeName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    row.Category.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    CategoryMappings.Add(row);
            }
        }

        [RelayCommand]
        private void SelectAllMappings()
        {
            foreach (var row in CategoryMappings) row.IsSelected = true;
            RecomputeMappedCount();
        }

        [RelayCommand]
        private void ClearMappings()
        {
            foreach (var row in CategoryMappings) row.IsSelected = false;
            RecomputeMappedCount();
        }

        // Keeps the "Mapped" metric live as the user assigns drafting views in Grid 3.
        private void OnMappingRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CategoryMappingRow.MappedDraftingView) ||
                e.PropertyName == nameof(CategoryMappingRow.IsSelected))
                RecomputeMappedCount();
        }

        private void RecomputeMappedCount()
            => MappedCount = _allCategoryMappingRows.Count(m => m.IsSelected && m.MappedDraftingView != null);

        // â”€â”€ Log commands (Copy Selected is in code-behind, per convention) â”€â”€
        [RelayCommand]
        private void CopyAllLogs()
            => System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, LogEntries.Select(e => e.ToString())));

        [RelayCommand]
        private void ExportLogs()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text file (*.txt)|*.txt",
                FileName = $"RefSectionHeadPlacer_Logs_{DateTime.Now:yyyy-MM-dd_HH-mm}.txt"
            };
            if (dialog.ShowDialog() == true)
                System.IO.File.WriteAllText(dialog.FileName,
                    string.Join(Environment.NewLine, LogEntries.Select(e => e.ToString())));
        }

        [RelayCommand]
        private void ClearLogs() => LogEntries.Clear();

        // â”€â”€ Run / Cancel / Close â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        [RelayCommand(CanExecute = nameof(CanRun))]
        private void Run()
        {
            var selectedTypes = _allElementTypeRows.Where(t => t.IsSelected).ToList();
            if (selectedTypes.Count == 0) { Log(LogLevel.Warning, "No elements selected â€” nothing to run."); return; }

            // V006 (confirmed, Rafi): Run no longer blocks when some selected types
            // are unmapped. The engine already skips-and-logs per item independently
            // (RefSectionHeadEngine.Run), so a type left unmapped simply skips while
            // every mapped type still places â€” matching "place 1 mapped, place 2
            // mapped, and so on" rather than an all-or-nothing gate. We still warn
            // up front so the user knows how many types will be skipped this run.
            var unmappedCount = _allCategoryMappingRows.Count(m => m.IsSelected && m.MappedDraftingView == null);
            if (unmappedCount > 0)
                Log(LogLevel.Warning, $"{unmappedCount} selected type(s) have no drafting view mapped â€” those will be skipped.");

            if (!double.TryParse(TailLengthMmText, out var parsedTailMm) || parsedTailMm <= 0)
                Log(LogLevel.Warning, $"Tail length \"{TailLengthMmText}\" is invalid â€” using default {DefaultTailLengthMm:F0} mm.");
            if (!double.TryParse(BoundaryBufferMmText, out var parsedBufferMm) || parsedBufferMm <= 0)
                Log(LogLevel.Warning, $"Boundary buffer \"{BoundaryBufferMmText}\" is invalid â€” using default {DefaultBoundaryBufferMm:F0} mm.");

            Log(LogLevel.Info,
                $"Run Â· {selectedTypes.Count} type(s) selected, {selectedTypes.Sum(t => t.Count)} element(s) total, " +
                $"section type '{SelectedSectionType?.Name ?? "(none)"}', tail {TailLengthMm:F0}mm.");

            IsRunning = true;
            PlacedCount = 0; SkippedCount = 0; ProgressPercent = 0;
            ProgressStatusText = "Startingâ€¦";

            _eventHandler.PendingRequest = new PlaceSectionsEventHandler.RequestArgs
            {
                SelectedTypes = selectedTypes,
                Mappings = _allCategoryMappingRows.ToList(),
                SectionViewFamilyTypeId = SelectedSectionType?.Id ?? ElementId.InvalidElementId,
                TailLengthFeet = TailLengthMm / MmPerFoot
            };
            _externalEvent.Raise();
        }

        private bool CanRun() => !IsRunning;

        [RelayCommand]
        private void Close() => CloseRequested?.Invoke();

        // â”€â”€ Engine callbacks (API thread -> marshal to UI thread) â”€â”€â”€â”€â”€â”€
        private void OnEngineLog(LogEntry entry)
            => _uiDispatcher.BeginInvoke(new Action(() =>
            {
                LogEntries.Add(entry);
                while (LogEntries.Count > MaxLogEntries) LogEntries.RemoveAt(0);
            }));

        private void OnEngineProgress(int current, int total)
            => _uiDispatcher.BeginInvoke(new Action(() =>
            {
                ProgressStatusText = $"Processingâ€¦ {current} of {total}";
                ProgressPercent = total == 0 ? 0 : (double)current / total * 100;
                ProgressPercentText = $"{ProgressPercent:F0}%";
            }));

        private void OnRunCompleted(RunSummary summary)
            => _uiDispatcher.BeginInvoke(new Action(() =>
            {
                PlacedCount = summary.PlacedCount;
                SkippedCount = summary.SkippedCount;
                IsRunning = false;
                ProgressStatusText =
                    $"Completed â€” {summary.PlacedCount} placed | {summary.SkippedCount} skipped | {summary.FailedCount} failed";
            }));

        private void OnRunFaulted(Exception ex)
            => _uiDispatcher.BeginInvoke(new Action(() =>
            {
                IsRunning = false;
                var detail = ex.InnerException != null
                    ? $"{ex.GetType().Name}: {ex.Message} | Inner: {ex.InnerException.Message}"
                    : $"{ex.GetType().Name}: {ex.Message}";
                Log(LogLevel.Error, $"Run failed: {detail}");
            }));

        private void Log(LogLevel level, string message) => LogEntries.Add(new LogEntry(level, message));

        // â”€â”€ Cleanup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _eventHandler.LogEmitted -= OnEngineLog;
            _eventHandler.ProgressChanged -= OnEngineProgress;
            _eventHandler.RunCompleted -= OnRunCompleted;
            _eventHandler.RunFaulted -= OnRunFaulted;

            foreach (var row in PlanViews) row.PropertyChanged -= OnPlanViewRowChanged;
            foreach (var row in _allElementTypeRows) row.PropertyChanged -= OnElementTypeRowChanged;
            foreach (var row in Links) row.PropertyChanged -= OnLinkRowChanged;
            foreach (var row in _allCategoryMappingRows) row.PropertyChanged -= OnMappingRowChanged;
            foreach (var opt in _allPlanTypeOptions) opt.PropertyChanged -= OnPlanTypeOptionChanged;
            foreach (var opt in _allRoofTypeOptions) opt.PropertyChanged -= OnRoofTypeOptionChanged;

            _externalEvent?.Dispose();
        }
    }
}

