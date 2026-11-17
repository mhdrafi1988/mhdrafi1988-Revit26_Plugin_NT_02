using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Models;
using Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Services;
using Revit26_Plugin_Revised.SheetViewArrange.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.SheetViewArrange.V001.UI.ViewModels
{
    /// <summary>A choice in the "Last row" combo box.</summary>
    public sealed record LastRowOption(LastRowMode Mode, string Label);

    /// <summary>A choice in the "Across the width" combo box.</summary>
    public sealed record HorizontalOption(HorizontalLayoutMode Mode, string Label);

    /// <summary>A choice in the "Down the height" combo box.</summary>
    public sealed record VerticalOption(VerticalLayoutMode Mode, string Label);

    /// <summary>A choice in the "Align views in row" combo box.</summary>
    public sealed record RowAlignOption(RowAlignMode Mode, string Label);

    /// <summary>A choice in the "Group by" combo box.</summary>
    public sealed record GroupByOption(ArrangeGroupBy Mode, string Label);

    /// <summary>A choice in the "Show" combo box.</summary>
    public sealed record ShowModeOption(ArrangeShowMode Mode, string Label);

    /// <summary>
    /// Window state for Sheet View Arrange. Holds the latest <see cref="SheetSnapshot"/> and
    /// rebuilds the <see cref="ArrangePlan"/> in memory on every settings or tick change, so the
    /// preview is live. All Revit access goes through <see cref="ISheetArrangeSession"/>.
    /// <para>
    /// The Views grid can be filtered, sorted, grouped and ticked. Filter, sort and group are
    /// display only (a ticked view hidden by a filter is still arranged; the layout order is
    /// always the detail-number order). Unticking a view leaves it where it is in Revit.
    /// </para>
    /// </summary>
    public partial class SheetViewArrangeViewModel : ObservableObject, IDisposable
    {
        private const string SettingsFolder = "SheetViewArrange";

        private readonly ISheetArrangeSession _session;
        private SheetSnapshot _snapshot;

        // Grid state. All of it is initialised here, before the constructor assigns any
        // settings-derived property whose On*Changed callback might touch it.
        private readonly HashSet<long> _unticked = new();
        private readonly Dictionary<long, GridRowViewModel> _rowsByKey = new();
        private readonly List<ArrangeSortKey> _sort = new();
        private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
        private readonly ListCollectionView _rowsView;
        private ArrangeGridFilter _filter = ArrangeGridFilter.None;
        private int _filterSuspend;
        private bool _viewReady;

        /// <summary>Raised whenever <see cref="Plan"/> is rebuilt (the window redraws its preview).</summary>
        public event EventHandler PlanChanged;

        /// <summary>Raised just before the grid view is rebuilt (the window remembers its scroll position).</summary>
        public event EventHandler ViewRefreshing;

        /// <summary>Raised after the grid view was rebuilt (the window restores its scroll position).</summary>
        public event EventHandler ViewRefreshed;

        /// <summary>Raised when the sort changed (the window updates the header arrows).</summary>
        public event EventHandler SortChanged;

        /// <summary>One line per view on the sheet. Instances live as long as the view is on the sheet.</summary>
        public ObservableCollection<GridRowViewModel> Rows { get; } = new();

        /// <summary>The filtered, sorted, grouped view of <see cref="Rows"/> the grid binds to.</summary>
        public ICollectionView RowsView => _rowsView;

        /// <summary>Activity log.</summary>
        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        /// <summary>View-type filter choices.</summary>
        public ObservableCollection<FilterOptionViewModel> TypeFilters { get; } = new();

        /// <summary>Status filter choices.</summary>
        public ObservableCollection<FilterOptionViewModel> StatusFilters { get; } = new();

        /// <summary>Layout-row filter choices.</summary>
        public ObservableCollection<FilterOptionViewModel> RowFilters { get; } = new();

        /// <summary>Removable tags for the filters that are on.</summary>
        public ObservableCollection<FilterChipViewModel> FilterChips { get; } = new();

        /// <summary>Choices for the last-row combo box.</summary>
        public IReadOnlyList<LastRowOption> LastRowOptions { get; } = new[]
        {
            new LastRowOption(LastRowMode.PackLeft, "Pack left (same gap as row above)"),
            new LastRowOption(LastRowMode.Justify, "Justify across full width"),
            new LastRowOption(LastRowMode.Center, "Centre (same gap as row above)")
        };

        /// <summary>Choices for the horizontal-layout combo box.</summary>
        public IReadOnlyList<HorizontalOption> HorizontalOptions { get; } = new[]
        {
            new HorizontalOption(HorizontalLayoutMode.Auto, "Auto (spread, last row as below)"),
            new HorizontalOption(HorizontalLayoutMode.Justify, "Justify — every row to full width"),
            new HorizontalOption(HorizontalLayoutMode.Left, "Left — packed on the left margin"),
            new HorizontalOption(HorizontalLayoutMode.Right, "Right — packed on the right margin"),
            new HorizontalOption(HorizontalLayoutMode.Center, "Centre — packed, centred")
        };

        /// <summary>Choices for the vertical-layout combo box.</summary>
        public IReadOnlyList<VerticalOption> VerticalOptions { get; } = new[]
        {
            new VerticalOption(VerticalLayoutMode.Auto, "Auto (spread to full height)"),
            new VerticalOption(VerticalLayoutMode.Spread, "Spread — rows fill the height"),
            new VerticalOption(VerticalLayoutMode.Top, "Top — packed on the top margin"),
            new VerticalOption(VerticalLayoutMode.Bottom, "Bottom — packed on the bottom margin"),
            new VerticalOption(VerticalLayoutMode.Center, "Centre — packed, centred")
        };

        /// <summary>Choices for the row-alignment combo box.</summary>
        public IReadOnlyList<RowAlignOption> RowAlignOptions { get; } = new[]
        {
            new RowAlignOption(RowAlignMode.Bottom, "Bottom line (titles line up)"),
            new RowAlignOption(RowAlignMode.Top, "Top line"),
            new RowAlignOption(RowAlignMode.Center, "Centre line")
        };

        /// <summary>Choices for the group-by combo box.</summary>
        public IReadOnlyList<GroupByOption> GroupByOptions { get; } = new[]
        {
            new GroupByOption(ArrangeGroupBy.None, "None"),
            new GroupByOption(ArrangeGroupBy.ViewType, "View type"),
            new GroupByOption(ArrangeGroupBy.Status, "Status"),
            new GroupByOption(ArrangeGroupBy.Row, "Layout row")
        };

        /// <summary>Choices for the show combo box.</summary>
        public IReadOnlyList<ShowModeOption> ShowModeOptions { get; } = new[]
        {
            new ShowModeOption(ArrangeShowMode.All, "All views"),
            new ShowModeOption(ArrangeShowMode.Ticked, "Ticked only"),
            new ShowModeOption(ArrangeShowMode.Unticked, "Unticked only")
        };

        /// <summary>Current plan; never null after construction.</summary>
        public ArrangePlan Plan { get; private set; }

        /// <summary>The sort keys in order (empty = reading order).</summary>
        public IReadOnlyList<ArrangeSortKey> SortKeys => _sort;

        [ObservableProperty] private string sheetLabel = "";
        [ObservableProperty] private double marginTopMm;
        [ObservableProperty] private double marginBottomMm;
        [ObservableProperty] private double marginLeftMm;
        [ObservableProperty] private double marginRightMm;
        [ObservableProperty] private double minHorizontalGapMm;
        [ObservableProperty] private double minVerticalGapMm;
        [ObservableProperty] private LastRowMode lastRowMode;
        [ObservableProperty] private HorizontalLayoutMode horizontal;
        [ObservableProperty] private VerticalLayoutMode vertical;
        [ObservableProperty] private RowAlignMode rowAlign;
        [ObservableProperty] private bool movePinned;

        [ObservableProperty] private int viewCount;
        [ObservableProperty] private int rowCount;
        [ObservableProperty] private int moveCount;
        [ObservableProperty] private int skippedCount;
        [ObservableProperty] private string statusText = "";
        [ObservableProperty] private bool hasBlocker;
        [ObservableProperty] private string warningsText = "";
        [ObservableProperty] private bool hasWarnings;

        // ── Grid: filter / group inputs ─────────────────────────────────────
        [ObservableProperty] private string searchText = "";
        [ObservableProperty] private ArrangeShowMode showMode = ArrangeShowMode.All;
        [ObservableProperty] private ArrangeGroupBy groupBy = ArrangeGroupBy.None;

        // ── Grid: summary, refreshed after every re-plan or filter change ───
        [ObservableProperty] private int shownCount;
        [ObservableProperty] private int totalCount;
        [ObservableProperty] private int tickedCount;
        [ObservableProperty] private bool hasTickableShown;
        [ObservableProperty] private bool? shownTickState = false;
        [ObservableProperty] private string tickSummaryText = "";
        [ObservableProperty] private string gridCountText = "";
        [ObservableProperty] private string tickShownLabel = "Tick all";
        [ObservableProperty] private string untickShownLabel = "Untick all";
        [ObservableProperty] private string invertShownLabel = "Invert";
        [ObservableProperty] private bool hasHiddenTicked;
        [ObservableProperty] private string hiddenTickedText = "";
        [ObservableProperty] private bool hasActiveFilters;
        [ObservableProperty] private string sortSummary = "Reading order";
        [ObservableProperty] private bool hasCustomSort;
        [ObservableProperty] private string typeFilterLabel = "Type ▾";
        [ObservableProperty] private string statusFilterLabel = "Status ▾";
        [ObservableProperty] private string rowFilterLabel = "Row ▾";
        [ObservableProperty] private bool isGrouped;

        /// <summary>Bumped on every tick change so group headers (bound to it) re-read their tick state.</summary>
        [ObservableProperty] private int tickRevision;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RefreshCommand))]
        private bool isBusy;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
        private bool canApply;

        /// <summary>Creates the view model over <paramref name="session"/> and plans the initial sheet.</summary>
        public SheetViewArrangeViewModel(ISheetArrangeSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _rowsView = new ListCollectionView(Rows);

            // Settings first; Recompute() ignores the On*Changed calls this fires (no snapshot yet).
            var s = SettingsService<SheetViewArrangeSettings>.Load(SettingsFolder);
            MarginTopMm = s.MarginTopMm;
            MarginBottomMm = s.MarginBottomMm;
            MarginLeftMm = s.MarginLeftMm;
            MarginRightMm = s.MarginRightMm;
            MinHorizontalGapMm = s.MinHorizontalGapMm;
            MinVerticalGapMm = s.MinVerticalGapMm;
            LastRowMode = Enum.IsDefined(typeof(LastRowMode), s.LastRowMode) ? s.LastRowMode : LastRowMode.PackLeft;
            Horizontal = Enum.IsDefined(typeof(HorizontalLayoutMode), s.Horizontal) ? s.Horizontal : HorizontalLayoutMode.Auto;
            Vertical = Enum.IsDefined(typeof(VerticalLayoutMode), s.Vertical) ? s.Vertical : VerticalLayoutMode.Auto;
            RowAlign = Enum.IsDefined(typeof(RowAlignMode), s.RowAlign) ? s.RowAlign : RowAlignMode.Bottom;
            MovePinned = s.MovePinned;
            GroupBy = Enum.IsDefined(typeof(ArrangeGroupBy), s.GroupBy) ? s.GroupBy : ArrangeGroupBy.None;

            ApplyViewConfiguration();
            _viewReady = true;

            LoadSnapshot(session.Initial, "Loaded");
        }

        /// <summary>Settings as currently shown in the window.</summary>
        public SheetViewArrangeSettings CurrentSettings() => new()
        {
            MarginTopMm = MarginTopMm,
            MarginBottomMm = MarginBottomMm,
            MarginLeftMm = MarginLeftMm,
            MarginRightMm = MarginRightMm,
            MinHorizontalGapMm = MinHorizontalGapMm,
            MinVerticalGapMm = MinVerticalGapMm,
            LastRowMode = LastRowMode,
            Horizontal = Horizontal,
            Vertical = Vertical,
            RowAlign = RowAlign,
            MovePinned = MovePinned,
            GroupBy = GroupBy
        };

        /// <summary>Writes the current settings to disk (called when the window closes).</summary>
        public void SaveSettings() => SettingsService<SheetViewArrangeSettings>.Save(SettingsFolder, CurrentSettings());

        // ── Settings changes → live re-plan ─────────────────────────────────
        partial void OnMarginTopMmChanged(double value) => ClampOrRecompute(value, v => MarginTopMm = v);
        partial void OnMarginBottomMmChanged(double value) => ClampOrRecompute(value, v => MarginBottomMm = v);
        partial void OnMarginLeftMmChanged(double value) => ClampOrRecompute(value, v => MarginLeftMm = v);
        partial void OnMarginRightMmChanged(double value) => ClampOrRecompute(value, v => MarginRightMm = v);
        partial void OnMinHorizontalGapMmChanged(double value) => ClampOrRecompute(value, v => MinHorizontalGapMm = v);
        partial void OnMinVerticalGapMmChanged(double value) => ClampOrRecompute(value, v => MinVerticalGapMm = v);
        partial void OnLastRowModeChanged(LastRowMode value) => Recompute();

        partial void OnHorizontalChanged(HorizontalLayoutMode value)
        {
            OnPropertyChanged(nameof(IsLastRowRelevant));
            Recompute();
        }

        partial void OnVerticalChanged(VerticalLayoutMode value) => Recompute();

        partial void OnRowAlignChanged(RowAlignMode value) => Recompute();

        /// <summary>
        /// True while the "Last row" choice has any effect — only
        /// <see cref="HorizontalLayoutMode.Auto"/> treats the last row differently from the rest.
        /// </summary>
        public bool IsLastRowRelevant => Horizontal == HorizontalLayoutMode.Auto;
        partial void OnMovePinnedChanged(bool value) => Recompute();

        // ── Grid inputs → re-filter / re-group ──────────────────────────────
        partial void OnSearchTextChanged(string value) => OnFilterInputChanged();
        partial void OnShowModeChanged(ArrangeShowMode value) => OnFilterInputChanged();

        partial void OnGroupByChanged(ArrangeGroupBy value)
        {
            IsGrouped = value != ArrangeGroupBy.None;
            if (!_viewReady)
                return;

            _collapsedGroups.Clear();
            ApplyViewConfiguration();
            Log(LogLevel.Info, value == ArrangeGroupBy.None
                ? "Grouping removed."
                : "Grouped by " + GroupByOptions.First(o => o.Mode == value).Label.ToLowerInvariant() + ".");
        }

        /// <summary>Negative or non-finite lengths are reset to 0 (which re-enters and recomputes).</summary>
        private void ClampOrRecompute(double value, Action<double> set)
        {
            if (!double.IsFinite(value) || value < 0)
                set(0);
            else
                Recompute();
        }

        /// <summary>Takes a fresh snapshot; <paramref name="verb"/> non-null logs it with the plan's warnings.</summary>
        private void LoadSnapshot(SheetSnapshot snapshot, string verb)
        {
            _snapshot = snapshot;
            SheetLabel = snapshot.SheetLabel;

            // Ticks follow the viewport across refreshes; forget views that are gone.
            _unticked.IntersectWith(snapshot.Viewports.Select(v => v.Key));
            Recompute();

            if (verb == null)
                return;
            Log(LogLevel.Info, $"{verb} sheet {snapshot.SheetLabel}: {snapshot.Viewports.Count} view(s).");
            foreach (var w in Plan.Warnings)
                Log(LogLevel.Warning, w);
            if (Plan.Blocker != null)
                Log(LogLevel.Warning, Plan.Blocker);
        }

        private void Recompute()
        {
            if (_snapshot == null)
                return;

            Plan = ArrangePlanner.Build(_snapshot, CurrentSettings(), _unticked);
            var change = SyncRows();

            ViewCount = Plan.Moves.Count;
            RowCount = Plan.RowCount;
            MoveCount = Plan.MoveCount;
            SkippedCount = Plan.Skipped.Count;
            HasBlocker = Plan.Blocker != null;
            CanApply = Plan.CanApply;
            WarningsText = string.Join(Environment.NewLine, Plan.Warnings.Select(w => "• " + w));
            HasWarnings = Plan.Warnings.Count > 0;
            StatusText = Plan.Blocker
                ?? (Plan.MoveCount == 0
                    ? "Already arranged — every view is in its place."
                    : $"Ready: {Plan.MoveCount} of {Plan.Moves.Count} view(s) move into {Plan.RowCount} row(s).");

            SyncFilterOptions();

            // Rebuild the grid only when something it sorts, groups or filters by actually changed;
            // otherwise the cells just repaint and the user keeps their place.
            if ((change & RowChange.Position) != 0 || ((change & RowChange.Values) != 0 && ViewDependsOnPlan()))
                RefreshGridView();
            else
                UpdateGridSummary();

            PlanChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>True when the current sort, group or filter reads columns the plan changes (#, Row, Status, tick).</summary>
        private bool ViewDependsOnPlan()
            => GroupBy is ArrangeGroupBy.Status or ArrangeGroupBy.Row
               || _sort.Any(k => k.Column is ArrangeSortColumn.Order or ArrangeSortColumn.Row or ArrangeSortColumn.Status)
               || _filter.Statuses.Count > 0 || _filter.Rows.Count > 0 || _filter.Show != ArrangeShowMode.All;

        /// <summary>Gives every plan row its grid line, reusing existing ones. Returns what changed.</summary>
        private RowChange SyncRows()
        {
            var change = RowChange.None;
            var seen = new HashSet<long>();

            foreach (var data in Plan.Rows)
            {
                seen.Add(data.ViewportKey);
                if (_rowsByKey.TryGetValue(data.ViewportKey, out var row))
                {
                    change |= row.Update(data);
                }
                else
                {
                    row = new GridRowViewModel(data, OnRowTickToggled, () => GroupBy);
                    _rowsByKey[data.ViewportKey] = row;
                    Rows.Add(row);
                    change |= RowChange.Position | RowChange.Values;
                }
            }

            foreach (long gone in _rowsByKey.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                Rows.Remove(_rowsByKey[gone]);
                _rowsByKey.Remove(gone);
                change |= RowChange.Position | RowChange.Values;
            }

            return change;
        }

        // ── Grid: view configuration ────────────────────────────────────────

        /// <summary>Applies sort, filter and grouping to the grid view with one rebuild.</summary>
        private void ApplyViewConfiguration()
        {
            ViewRefreshing?.Invoke(this, EventArgs.Empty);
            using (_rowsView.DeferRefresh())
            {
                _rowsView.CustomSort = new GridRowComparer(ArrangeGridSort.CreateComparison(GroupBy, _sort));
                _rowsView.Filter = o => o is GridRowViewModel r && _filter.Matches(r.Data);
                _rowsView.GroupDescriptions.Clear();
                if (GroupBy != ArrangeGroupBy.None)
                    _rowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GridRowViewModel.GroupName)));
            }
            AfterViewRefresh();
        }

        /// <summary>Re-applies filter, sort and grouping to the grid view (one rebuild).</summary>
        private void RefreshGridView()
        {
            ViewRefreshing?.Invoke(this, EventArgs.Empty);
            _rowsView.Refresh();
            AfterViewRefresh();
        }

        private void AfterViewRefresh()
        {
            UpdateGridSummary();
            ViewRefreshed?.Invoke(this, EventArgs.Empty);
        }

        private void OnFilterInputChanged()
        {
            if (!_viewReady || _filterSuspend > 0)
                return;

            RebuildFilter();
            RefreshGridView();
        }

        /// <summary>Builds <see cref="_filter"/> from the search box, the pop-ups and the show mode, and updates chips and labels.</summary>
        private void RebuildFilter()
        {
            _filter = new ArrangeGridFilter
            {
                Search = SearchText ?? "",
                ViewTypes = TypeFilters.Where(o => o.IsChecked).Select(o => o.Key).ToHashSet(StringComparer.Ordinal),
                Statuses = StatusFilters.Where(o => o.IsChecked).Select(o => Enum.Parse<ArrangeStatus>(o.Key)).ToHashSet(),
                Rows = RowFilters.Where(o => o.IsChecked).Select(o => o.Key).ToHashSet(StringComparer.Ordinal),
                Show = ShowMode
            };

            TypeFilterLabel = PopupLabel("Type", TypeFilters);
            StatusFilterLabel = PopupLabel("Status", StatusFilters);
            RowFilterLabel = PopupLabel("Row", RowFilters);

            var chips = new List<FilterChipViewModel>();
            if (!string.IsNullOrWhiteSpace(SearchText))
                chips.Add(new FilterChipViewModel($"Search: “{SearchText.Trim()}”", () => ChangeFilters(() => SearchText = "")));
            foreach (var o in TypeFilters.Where(o => o.IsChecked))
                chips.Add(new FilterChipViewModel("Type: " + o.Label, () => ChangeFilters(() => o.IsChecked = false)));
            foreach (var o in StatusFilters.Where(o => o.IsChecked))
                chips.Add(new FilterChipViewModel("Status: " + o.Label, () => ChangeFilters(() => o.IsChecked = false)));
            foreach (var o in RowFilters.Where(o => o.IsChecked))
                chips.Add(new FilterChipViewModel(o.Label, () => ChangeFilters(() => o.IsChecked = false)));
            if (ShowMode != ArrangeShowMode.All)
                chips.Add(new FilterChipViewModel("Show: " + ShowModeOptions.First(o => o.Mode == ShowMode).Label.ToLowerInvariant(),
                    () => ChangeFilters(() => ShowMode = ArrangeShowMode.All)));

            // Re-plans rebuild the filter on every tick; leave the chips alone unless their text changed.
            if (!FilterChips.Select(c => c.Text).SequenceEqual(chips.Select(c => c.Text)))
            {
                FilterChips.Clear();
                foreach (var chip in chips)
                    FilterChips.Add(chip);
            }
        }

        private static string PopupLabel(string name, IEnumerable<FilterOptionViewModel> options)
        {
            int n = options.Count(o => o.IsChecked);
            return n == 0 ? name + " ▾" : $"{name} ({n}) ▾";
        }

        /// <summary>Runs <paramref name="change"/> (any number of filter edits) and re-filters once afterwards.</summary>
        private void ChangeFilters(Action change)
        {
            _filterSuspend++;
            try { change(); }
            finally { _filterSuspend--; }
            OnFilterInputChanged();
        }

        /// <summary>Keeps the pop-up lists in step with the sheet (counts, new types) without losing ticked options.</summary>
        private void SyncFilterOptions()
        {
            _filterSuspend++;
            try
            {
                var data = Rows.Select(r => r.Data).ToList();

                SyncOptions(TypeFilters,
                    data.GroupBy(d => d.ViewTypeName ?? "")
                        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(g => (g.Key, g.Key.Length == 0 ? "(no type)" : g.Key, g.Count())));

                SyncOptions(StatusFilters,
                    Enum.GetValues<ArrangeStatus>()
                        .Select(s => (s.ToString(), new ArrangeRow { Status = s }.StatusLabel, data.Count(d => d.Status == s))));

                SyncOptions(RowFilters,
                    data.GroupBy(d => d.RowLabel)
                        .OrderBy(g => g.Key == ArrangeRow.NoValue ? int.MaxValue : int.Parse(g.Key))
                        .Select(g => (g.Key, g.Key == ArrangeRow.NoValue ? "No row" : "Row " + g.Key, g.Count())));
            }
            finally { _filterSuspend--; }

            // A ticked option may have gone (its count fell to 0 but it is kept); chips/labels follow.
            RebuildFilter();
        }

        /// <summary>
        /// Brings <paramref name="list"/> in line with <paramref name="wanted"/>: updates counts, adds
        /// new options, drops options with no views — except ones that are ticked, which stay (count 0)
        /// so the filter does not silently change under the user.
        /// </summary>
        private void SyncOptions(ObservableCollection<FilterOptionViewModel> list, IEnumerable<(string Key, string Label, int Count)> wanted)
        {
            var wantedList = wanted.ToList();

            foreach (var w in wantedList)
            {
                var existing = list.FirstOrDefault(o => o.Key == w.Key);
                if (existing == null)
                {
                    int at = wantedList.FindIndex(x => x.Key == w.Key);
                    list.Insert(Math.Min(at, list.Count), new FilterOptionViewModel(w.Key, w.Label, OnFilterInputChanged) { Count = w.Count });
                }
                else
                {
                    existing.Count = w.Count;
                    existing.SetLabel(w.Label);
                }
            }

            foreach (var o in list.Where(o => wantedList.All(w => w.Key != o.Key)).ToList())
            {
                if (o.IsChecked)
                    o.Count = 0;
                else
                    list.Remove(o);
            }
        }

        /// <summary>Recomputes counts, labels and the header tick state from the rows and the filter.</summary>
        private void UpdateGridSummary()
        {
            var shown = Rows.Where(r => _filter.Matches(r.Data)).ToList();
            var shownTickable = shown.Where(r => r.CanTick).ToList();
            int tickable = Rows.Count(r => r.CanTick);
            int ticked = Rows.Count(r => r.CanTick && r.IsTicked);
            int shownTicked = shownTickable.Count(r => r.IsTicked);
            bool filtered = _filter.IsActive;

            ShownCount = shown.Count;
            TotalCount = Rows.Count;
            TickedCount = ticked;
            HasTickableShown = shownTickable.Count > 0;
            ShownTickState = shownTickable.Count == 0 || shownTicked == 0 ? false
                           : shownTicked == shownTickable.Count ? true
                           : null;

            TickSummaryText = filtered
                ? $"{ticked} of {tickable} ticked · acting on the {shownTickable.Count} shown"
                : $"{ticked} of {tickable} ticked";
            GridCountText = $"{shown.Count} of {Rows.Count} shown · {ticked} ticked";
            TickShownLabel = filtered ? $"Tick shown ({shownTickable.Count})" : "Tick all";
            UntickShownLabel = filtered ? $"Untick shown ({shownTickable.Count})" : "Untick all";
            InvertShownLabel = filtered ? "Invert shown" : "Invert";

            int hidden = Rows.Count(r => r.CanTick && r.IsTicked && !_filter.Matches(r.Data));
            HasHiddenTicked = hidden > 0;
            HiddenTickedText = $"{hidden} ticked view(s) are hidden by the current filter. They will still be arranged.";
            HasActiveFilters = filtered;

            TickRevision++;
        }

        // ── Sort ────────────────────────────────────────────────────────────

        /// <summary>
        /// Header click: sorts by <paramref name="column"/> ascending, then descending, then back to
        /// reading order. With <paramref name="add"/> (Shift) the column is added as a further sort key.
        /// </summary>
        public void ToggleSort(ArrangeSortColumn column, bool add)
        {
            int i = _sort.FindIndex(k => k.Column == column);
            if (add)
            {
                if (i < 0) _sort.Add(new ArrangeSortKey(column, false));
                else if (!_sort[i].Descending) _sort[i] = _sort[i] with { Descending = true };
                else _sort.RemoveAt(i);
            }
            else if (i >= 0 && _sort.Count == 1)
            {
                if (!_sort[0].Descending) _sort[0] = _sort[0] with { Descending = true };
                else _sort.Clear();
            }
            else
            {
                _sort.Clear();
                _sort.Add(new ArrangeSortKey(column, false));
            }

            OnSortKeysChanged();
        }

        /// <summary>Goes back to reading order.</summary>
        [RelayCommand]
        private void ResetSort()
        {
            _sort.Clear();
            OnSortKeysChanged();
        }

        private void OnSortKeysChanged()
        {
            HasCustomSort = _sort.Count > 0;
            SortSummary = _sort.Count == 0
                ? "Reading order"
                : string.Join(", then ", _sort.Select(k => $"{ColumnName(k.Column)} {(k.Descending ? "▼" : "▲")}"));
            ApplyViewConfiguration();
            SortChanged?.Invoke(this, EventArgs.Empty);
        }

        private static string ColumnName(ArrangeSortColumn column) => column switch
        {
            ArrangeSortColumn.Order => "#",
            ArrangeSortColumn.DetailNumber => "Detail No.",
            ArrangeSortColumn.ViewName => "View",
            ArrangeSortColumn.ViewType => "Type",
            ArrangeSortColumn.Size => "Size",
            ArrangeSortColumn.Row => "Row",
            _ => "Status"
        };

        // ── Filter commands ─────────────────────────────────────────────────

        /// <summary>Removes every filter (search, pop-ups and show mode).</summary>
        [RelayCommand]
        private void ClearFilters() => ChangeFilters(() =>
        {
            SearchText = "";
            ShowMode = ArrangeShowMode.All;
            foreach (var o in TypeFilters.Concat(StatusFilters).Concat(RowFilters))
                o.IsChecked = false;
        });

        // ── Grouping ────────────────────────────────────────────────────────

        /// <summary>True when the group called <paramref name="name"/> is collapsed.</summary>
        public bool IsGroupCollapsed(string name) => name != null && _collapsedGroups.Contains(name);

        /// <summary>Remembers that the group called <paramref name="name"/> was collapsed or expanded by the user.</summary>
        public void SetGroupCollapsed(string name, bool collapsed)
        {
            if (name == null) return;
            if (collapsed) _collapsedGroups.Add(name);
            else _collapsedGroups.Remove(name);
        }

        /// <summary>Collapses every group.</summary>
        [RelayCommand]
        private void CollapseAll()
        {
            foreach (var name in Rows.Where(r => _filter.Matches(r.Data)).Select(r => r.GroupName).Distinct())
                _collapsedGroups.Add(name);
            RefreshGridView();
        }

        /// <summary>Expands every group.</summary>
        [RelayCommand]
        private void ExpandAll()
        {
            _collapsedGroups.Clear();
            RefreshGridView();
        }

        // ── Ticking ─────────────────────────────────────────────────────────

        private List<GridRowViewModel> ShownRows() => Rows.Where(r => _filter.Matches(r.Data)).ToList();

        private string ShownScope() => _filter.IsActive ? "the shown rows" : "the list";

        /// <summary>The user ticked or unticked one row's box.</summary>
        private void OnRowTickToggled(GridRowViewModel row)
        {
            if (!row.CanTick)
                return;
            if (row.IsTicked) _unticked.Remove(row.Key);
            else _unticked.Add(row.Key);
            Recompute();
        }

        /// <summary>
        /// Sets (<paramref name="value"/> true/false) or flips (null) the tick of every tickable row in
        /// <paramref name="rows"/> with a single re-plan, and logs it.
        /// </summary>
        private void SetTicked(IEnumerable<GridRowViewModel> rows, bool? value, string scope)
        {
            int changed = 0;
            foreach (var r in rows)
            {
                if (!r.CanTick)
                    continue;
                bool target = value ?? !r.Data.IsTicked;
                if (target == r.Data.IsTicked)
                    continue;
                if (target) _unticked.Remove(r.Key);
                else _unticked.Add(r.Key);
                changed++;
            }

            if (changed == 0)
                return;

            Log(LogLevel.Info, value switch
            {
                true => $"Ticked {changed} view(s) in {scope}.",
                false => $"Unticked {changed} view(s) in {scope}.",
                _ => $"Inverted {changed} view(s) in {scope}."
            });
            Recompute();
        }

        /// <summary>Ticks every shown view.</summary>
        [RelayCommand]
        private void TickShown() => SetTicked(ShownRows(), true, ShownScope());

        /// <summary>Unticks every shown view.</summary>
        [RelayCommand]
        private void UntickShown() => SetTicked(ShownRows(), false, ShownScope());

        /// <summary>Flips the tick of every shown view.</summary>
        [RelayCommand]
        private void InvertShown() => SetTicked(ShownRows(), null, ShownScope());

        /// <summary>Header box: unticks the shown views when all are ticked, otherwise ticks them all.</summary>
        [RelayCommand]
        private void ToggleShown()
        {
            var rows = ShownRows().Where(r => r.CanTick).ToList();
            SetTicked(rows, !rows.All(r => r.IsTicked), ShownScope());
        }

        /// <summary>Group header box: unticks the group's views when all are ticked, otherwise ticks them all.</summary>
        [RelayCommand]
        private void ToggleGroup(CollectionViewGroup group)
        {
            if (group == null)
                return;
            var rows = group.Items.OfType<GridRowViewModel>().Where(r => r.CanTick).ToList();
            SetTicked(rows, !rows.All(r => r.IsTicked), $"group “{group.Name}”");
        }

        /// <summary>Space bar: ticks the selected views, or unticks them when they are all ticked already.</summary>
        public void ToggleSelected(IEnumerable<GridRowViewModel> selected)
        {
            var rows = (selected ?? Enumerable.Empty<GridRowViewModel>()).Where(r => r.CanTick).ToList();
            if (rows.Count == 0)
                return;
            SetTicked(rows, !rows.All(r => r.IsTicked), rows.Count == 1 ? "the selection" : "the selected rows");
        }

        // ── Commands ────────────────────────────────────────────────────────

        /// <summary>Re-reads the sheet from the model (after edits made in Revit).</summary>
        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private void Refresh() => Run(done => _session.Refresh(done), "Refreshed");

        private bool CanRefresh() => !IsBusy;

        /// <summary>Moves the views as previewed — refused by the session if the sheet changed meanwhile.</summary>
        [RelayCommand(CanExecute = nameof(CanRunApply))]
        private void Apply()
        {
            string signature = Plan.Signature;
            var settings = CurrentSettings();
            var unticked = new HashSet<long>(_unticked); // a copy: the request runs later, in Revit's context
            Run(done => _session.Apply(settings, unticked, signature, done), null);
        }

        private bool CanRunApply() => !IsBusy && CanApply;

        /// <summary>Copies every log line to the clipboard.</summary>
        [RelayCommand]
        private void CopyAllLogs() => LogClipboardService.CopyAll(LogEntries);

        /// <summary>Sends one request to Revit, showing the busy state until it comes back.</summary>
        private void Run(Func<Action<SessionResult>, bool> request, string loadVerb)
        {
            IsBusy = true;
            bool accepted = request(result =>
            {
                IsBusy = false;
                if (result.Message != null)
                    Log(result.Level, result.Message);
                if (result.Snapshot != null)
                    LoadSnapshot(result.Snapshot, loadVerb);
            });

            if (!accepted)
            {
                IsBusy = false;
                Log(LogLevel.Error, "Revit did not accept the request. Finish any active command and try again.");
            }
        }

        private void Log(LogLevel level, string message) => LogEntries.Add(new LogEntry(level, message));

        /// <summary>Releases the Revit session.</summary>
        public void Dispose() => _session.Dispose();
    }
}
