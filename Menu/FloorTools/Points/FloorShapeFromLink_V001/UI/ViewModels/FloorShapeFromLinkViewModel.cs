using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.FloorShapeFromLink.V001.Core.Models;
using Revit26_Plugin_Revised.FloorShapeFromLink.V001.Core.Services;
using Revit26_Plugin_Revised.FloorShapeFromLink.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.FloorShapeFromLink.V001.UI.ViewModels
{
    /// <summary>
    /// State of the Floor Shape From Link window. It never calls the Revit API itself: every action
    /// that reads or changes the model raises the external event (see <see cref="FloorShapeFromLinkHandler"/>).
    /// </summary>
    public partial class FloorShapeFromLinkViewModel : ToolViewModelBase
    {
        private readonly FloorShapeFromLinkHandler _handler;
        private readonly ExternalEvent _event;
        private const string AllChoice = "All";
        private bool _loading;
        private bool _refreshQueued;

        private List<ElementId> _floorIds;

        /// <summary>Floors the tool works on; replaced by "Change selection".</summary>
        public List<ElementId> FloorIds
        {
            get => _floorIds;
            set
            {
                _floorIds = value;
                FloorCount = value?.Count ?? 0;
            }
        }

        /// <summary>Number of floors in <see cref="FloorIds"/>.</summary>
        [ObservableProperty] private int _floorCount;

        /// <summary>Project length unit, e.g. "mm".</summary>
        public string UnitLabel { get; }

        /// <summary>Converts feet to the project length unit.</summary>
        public Func<double, double> ToDisplay { get; }

        /// <summary>Converts the project length unit to feet.</summary>
        public Func<double, double> ToFeet { get; }

        /// <summary>Choices for a row's Source column.</summary>
        public IReadOnlyList<string> SourceChoices { get; } =
            new[] { ShapePointRow.SourceSurface, ShapePointRow.SourceNearest, ShapePointRow.SourceManual };

        /// <summary>Choices for the global source switch.</summary>
        public IReadOnlyList<string> GlobalSourceChoices { get; } =
            new[] { ShapePointRow.SourceSurface, ShapePointRow.SourceNearest };

        /// <summary>Loaded links in the host model.</summary>
        public ObservableCollection<LinkItem> Links { get; } = new();

        /// <summary>Links filtered by the search box.</summary>
        public ICollectionView LinksView { get; }

        /// <summary>One row per shape-editing point of the selected floors.</summary>
        public ObservableCollection<ShapePointRow> Rows { get; } = new();

        /// <summary>The grid's view of <see cref="Rows"/>: filtered, sorted (by the column headers) and grouped by floor.</summary>
        public ICollectionView RowsView { get; }

        /// <summary>Choices for the status filter.</summary>
        public IReadOnlyList<string> StatusChoices { get; } = new[] { AllChoice, "OK", "Snapped", "Below", "No link" };

        /// <summary>Choices for the source filter.</summary>
        public IReadOnlyList<string> SourceFilterChoices { get; } =
            new[] { AllChoice, ShapePointRow.SourceSurface, ShapePointRow.SourceNearest, ShapePointRow.SourceManual };

        /// <summary>Text in the grid search box (matches floor, linked floor, note, status and source).</summary>
        [ObservableProperty] private string _searchText = "";

        /// <summary>Floor name filter (contains).</summary>
        [ObservableProperty] private string _floorFilter = "";

        /// <summary>Status filter; "All" shows everything.</summary>
        [ObservableProperty] private string _statusFilter = AllChoice;

        /// <summary>Source filter; "All" shows everything.</summary>
        [ObservableProperty] private string _sourceFilter = AllChoice;

        /// <summary>Show only ticked rows.</summary>
        [ObservableProperty] private bool _tickedOnly;

        /// <summary>Group the grid by floor.</summary>
        [ObservableProperty] private bool _groupByFloor = true;

        /// <summary>Use shorter rows.</summary>
        [ObservableProperty] private bool _compactRows;

        /// <summary>Row height for the grid.</summary>
        public double RowHeight => CompactRows ? 26 : 34;

        /// <summary>Source applied to the visible rows by the bulk bar.</summary>
        [ObservableProperty] private string _bulkSource = ShapePointRow.SourceSurface;

        /// <summary>Subtract value applied to the visible rows by the bulk bar (project length unit).</summary>
        [ObservableProperty] private double _bulkOffset;

        /// <summary>Number of rows that pass the current filters.</summary>
        [ObservableProperty] private int _visibleCount;

        /// <summary>One line under the grid: counts and min, average and max change.</summary>
        [ObservableProperty] private string _gridSummary = "";

        /// <summary>Raised by Reset view so the window can clear column sorting.</summary>
        public event Action ResetViewRequested;

        /// <summary>Text in the link search box.</summary>
        [ObservableProperty] private string _linkSearch = "";

        /// <summary>Fixed number subtracted from every linked elevation (project length unit).</summary>
        [ObservableProperty] private double _globalOffset;

        /// <summary>Source applied to every row: Surface or Nearest.</summary>
        [ObservableProperty] private string _globalSource = ShapePointRow.SourceSurface;

        /// <summary>
        /// When on, the host floor type's thickness is subtracted from every linked elevation. Off by default
        /// on floors: a floor's level + offset is its top surface, so the linked top is matched directly
        /// (the roof tool defaults to on because a roof's base is its bottom).
        /// </summary>
        [ObservableProperty] private bool _subtractThickness = false;

        /// <summary>When on, "nearest point" only uses corner and edge points of the linked floor.</summary>
        [ObservableProperty] private bool _boundaryPointsOnly;

        /// <summary>One-line description of the floors in use.</summary>
        [ObservableProperty] private string _floorSummary = "";

        /// <summary>Number of rows that Apply will write.</summary>
        [ObservableProperty] private int _includedCount;

        /// <summary>Number of rows with a linked elevation.</summary>
        [ObservableProperty] private int _matchedCount;

        /// <summary>Raised before the window hides for a pick; the window subscribes.</summary>
        public event Action PickStarting;

        /// <summary>Raised after a pick ends; the window subscribes.</summary>
        public event Action PickEnded;

        /// <summary>Window caption, "Floor Shape From Link — vX.Y.Z".</summary>
        public string WindowTitle => ToolCatalog.FloorShapeFromLink.Title;

        /// <summary>Creates the view model for the given floors and project length unit.</summary>
        public FloorShapeFromLinkViewModel(List<ElementId> floorIds, ForgeTypeId lengthUnit)
        {
            FloorIds = floorIds;
            UnitLabel = LabelUtils.GetLabelForUnit(lengthUnit);
            ToDisplay = ft => UnitUtils.ConvertFromInternalUnits(ft, lengthUnit);
            ToFeet = v => UnitUtils.ConvertToInternalUnits(v, lengthUnit);

            _handler = new FloorShapeFromLinkHandler(this);
            _event = ExternalEvent.Create(_handler);

            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = RowPasses;
            RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShapePointRow.FloorLabel)));

            LinksView = CollectionViewSource.GetDefaultView(Links);
            LinksView.Filter = o => o is LinkItem l &&
                (string.IsNullOrWhiteSpace(LinkSearch) || l.Name.IndexOf(LinkSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── Called by the handler (Revit thread == UI thread) ─────────────────

        /// <summary>Writes a log line.</summary>
        public void Log(LogLevel level, string message) => AddLog(level, message);

        /// <summary>Replaces the link list.</summary>
        public void SetLinks(IEnumerable<LinkItem> links)
        {
            Links.Clear();
            foreach (LinkItem l in links) Links.Add(l);
        }

        /// <summary>Replaces the grid rows and refreshes the counters.</summary>
        public void SetRows(IEnumerable<ShapePointRow> rows)
        {
            _loading = true;
            // No DeferRefresh here: the view is grouped, and a deferred ListCollectionView throws
            // InvalidOperationException on every CollectionChanged raised by Clear()/Add().
            try
            {
                foreach (ShapePointRow old in Rows) old.PropertyChanged -= OnRowChanged;
                Rows.Clear();
                foreach (ShapePointRow r in rows)
                {
                    r.PropertyChanged += OnRowChanged;
                    Rows.Add(r);
                }
            }
            finally { _loading = false; }
            UpdateCounts();
        }

        /// <summary>Marks the start or end of a long step so the status strip animates.</summary>
        public void SetBusy(bool busy, string text)
        {
            IsRunning = busy;
            if (!string.IsNullOrEmpty(text)) SummaryText = text;
        }

        /// <summary>Hides the window while the user picks floors.</summary>
        public void BeginPick() => PickStarting?.Invoke();

        /// <summary>Shows the window again after a pick.</summary>
        public void EndPick() => PickEnded?.Invoke();

        /// <summary>Recounts matched and included rows.</summary>
        public void UpdateCounts()
        {
            MatchedCount = Rows.Count(r => r.SurfaceZ.HasValue || r.NearestZ.HasValue);
            IncludedCount = Rows.Count(r => r.Include && r.NewZ.HasValue);

            // One scale for every delta bar: the largest change in the grid.
            ShapePointRow.DeltaScale = Math.Max(1e-9, Rows.Select(r => Math.Abs(r.Delta ?? 0)).DefaultIfEmpty(0).Max());
            foreach (ShapePointRow r in Rows) r.RefreshDeltaBar();

            VisibleCount = RowsView.Cast<object>().Count();
            var deltas = Rows.Where(r => r.Include && r.NewZ.HasValue).Select(r => r.Delta.Value).ToList();
            int noLink = Rows.Count(r => r.Status == "No link");
            GridSummary = Rows.Count == 0
                ? ""
                : $"Showing {VisibleCount} of {Rows.Count} · ticked {IncludedCount} · no linked floor {noLink}" +
                  (deltas.Count == 0 ? "" : $" · change min {deltas.Min():+0.0;-0.0;0.0} · avg {deltas.Average():+0.0;-0.0;0.0} · max {deltas.Max():+0.0;-0.0;0.0} {UnitLabel}");
        }

        private bool RowPasses(object item)
        {
            if (item is not ShapePointRow r) return false;
            if (TickedOnly && !r.Include) return false;
            if (StatusFilter != AllChoice && r.Status != StatusFilter) return false;
            if (SourceFilter != AllChoice && r.Source != SourceFilter) return false;
            if (!string.IsNullOrWhiteSpace(FloorFilter) &&
                r.FloorLabel.IndexOf(FloorFilter.Trim(), StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string hay = $"{r.FloorLabel} {r.Index} {r.LinkedFloor} {r.Note} {r.Status} {r.Source}";
                if (hay.IndexOf(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) < 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Re-applies the grid filters. Deferred and guarded because the grid refuses a refresh while a
        /// cell is being edited; the next refresh picks the change up.
        /// </summary>
        private void RefreshView()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshQueued = false;
                try { RowsView.Refresh(); }
                catch (InvalidOperationException) { /* a cell edit is still open */ }
                UpdateCounts();
            }));
        }

        private bool FilterActive =>
            TickedOnly || StatusFilter != AllChoice || SourceFilter != AllChoice ||
            !string.IsNullOrWhiteSpace(FloorFilter) || !string.IsNullOrWhiteSpace(SearchText);

        // ── Reacting to the controls ──────────────────────────────────────────

        partial void OnLinkSearchChanged(string value) => LinksView.Refresh();

        partial void OnSearchTextChanged(string value) => RefreshView();

        partial void OnFloorFilterChanged(string value) => RefreshView();

        partial void OnStatusFilterChanged(string value) => RefreshView();

        partial void OnSourceFilterChanged(string value) => RefreshView();

        partial void OnTickedOnlyChanged(bool value) => RefreshView();

        partial void OnCompactRowsChanged(bool value) => OnPropertyChanged(nameof(RowHeight));

        partial void OnGroupByFloorChanged(bool value)
        {
            using (RowsView.DeferRefresh())
            {
                RowsView.GroupDescriptions.Clear();
                if (value) RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShapePointRow.FloorLabel)));
            }
        }

        partial void OnGlobalOffsetChanged(double value)
        {
            foreach (ShapePointRow r in Rows) r.Offset = value;
        }

        partial void OnSubtractThicknessChanged(bool value)
        {
            foreach (ShapePointRow r in Rows) r.UseThickness = value;
        }

        partial void OnGlobalSourceChanged(string value)
        {
            foreach (ShapePointRow r in Rows) r.UseSource(value);
        }

        private void OnRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_loading) return;
            if (e.PropertyName is nameof(ShapePointRow.Include) or nameof(ShapePointRow.NewZ) or nameof(ShapePointRow.CurrentZ))
                UpdateCounts();
            if (FilterActive && e.PropertyName is nameof(ShapePointRow.Include) or nameof(ShapePointRow.Source))
                RefreshView();
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>Reads the linked elevations for the current floors and ticked links.</summary>
        [RelayCommand]
        private void ReadElevations() => Raise(FloorShapeFromLinkHandler.Job.Compute);

        /// <summary>Lets the user pick other floors, then reads them.</summary>
        [RelayCommand]
        private void ChangeSelection() => Raise(FloorShapeFromLinkHandler.Job.PickFloors);

        /// <summary>Writes the included rows to the floors.</summary>
        [RelayCommand]
        private void Apply()
        {
            if (IncludedCount == 0)
            {
                AddLog(LogLevel.Warning, "Nothing to apply: tick at least one row that has a New Z.");
                return;
            }
            Raise(FloorShapeFromLinkHandler.Job.Apply);
        }

        /// <summary>Ticks every link that is visible in the search result.</summary>
        [RelayCommand]
        private void SelectAllLinks()
        {
            foreach (LinkItem l in LinksView.Cast<LinkItem>()) l.IsSelected = true;
        }

        /// <summary>Unticks every link.</summary>
        [RelayCommand]
        private void SelectNoLinks()
        {
            foreach (LinkItem l in Links) l.IsSelected = false;
        }

        /// <summary>Ticks every visible row that has a New Z.</summary>
        [RelayCommand]
        private void TickVisible()
        {
            foreach (ShapePointRow r in VisibleRows()) r.Include = r.NewZ.HasValue;
        }

        /// <summary>Unticks every visible row.</summary>
        [RelayCommand]
        private void UntickVisible()
        {
            foreach (ShapePointRow r in VisibleRows()) r.Include = false;
        }

        /// <summary>Sets the bulk source on every visible row that has that candidate.</summary>
        [RelayCommand]
        private void SetVisibleSource()
        {
            foreach (ShapePointRow r in VisibleRows()) r.UseSource(BulkSource);
        }

        /// <summary>Sets the bulk subtract value on every visible row.</summary>
        [RelayCommand]
        private void SetVisibleOffset()
        {
            foreach (ShapePointRow r in VisibleRows()) r.Offset = BulkOffset;
        }

        /// <summary>Clears filters, sorting, grouping and row height back to the defaults.</summary>
        [RelayCommand]
        private void ResetView()
        {
            SearchText = "";
            FloorFilter = "";
            StatusFilter = AllChoice;
            SourceFilter = AllChoice;
            TickedOnly = false;
            CompactRows = false;
            GroupByFloor = true;
            RowsView.SortDescriptions.Clear();
            ResetViewRequested?.Invoke();
        }

        private List<ShapePointRow> VisibleRows() => RowsView.Cast<ShapePointRow>().ToList();

        private void Raise(FloorShapeFromLinkHandler.Job job)
        {
            if (IsRunning) return;
            _handler.Pending = job;
            _event.Raise();
        }
    }
}
