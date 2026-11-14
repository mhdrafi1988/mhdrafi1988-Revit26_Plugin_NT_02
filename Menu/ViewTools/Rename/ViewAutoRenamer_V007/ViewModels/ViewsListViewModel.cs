using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.ViewAutoRenamer.V007.ViewModels;

/// <summary>
/// One row per distinct Autodesk.Revit.DB.ViewType present in the loaded
/// set — used to build the popover's "View type" checklist, grouped by
/// ViewTypeGroup category header.
/// </summary>
public partial class ViewTypeFilterRow : ObservableObject
{
    public Autodesk.Revit.DB.ViewType ViewType { get; }
    public string Label { get; }
    public int Count { get; }

    [ObservableProperty] private bool isChecked;

    public ViewTypeFilterRow(Autodesk.Revit.DB.ViewType viewType, string label, int count, bool isChecked)
    {
        ViewType = viewType;
        Label = label;
        Count = count;
        this.isChecked = isChecked;
    }
}

/// <summary>Category header row in the popover (e.g. "Plans", "Sections &amp; Callouts").</summary>
public partial class ViewTypeFilterGroup : ObservableObject
{
    public ViewTypeGroup Group { get; }
    public string Label { get; }
    public ObservableCollection<ViewTypeFilterRow> Rows { get; }
    public int TotalCount => Rows.Sum(r => r.Count);

    [ObservableProperty] private bool isExpanded = true;
    [ObservableProperty] private bool isChecked;

    private bool _settingRows;

    public ViewTypeFilterGroup(ViewTypeGroup group, string label, IEnumerable<ViewTypeFilterRow> rows)
    {
        Group = group;
        Label = label;
        Rows = new ObservableCollection<ViewTypeFilterRow>(rows);
        isChecked = Rows.All(r => r.IsChecked);

        foreach (var r in Rows)
            r.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewTypeFilterRow.IsChecked) && !_settingRows)
                    IsChecked = Rows.All(x => x.IsChecked);
            };
    }

    /// <summary>Sets every child row's IsChecked without re-triggering group-level recompute storms.</summary>
    public void SetAllRows(bool value)
    {
        _settingRows = true;
        foreach (var r in Rows) r.IsChecked = value;
        _settingRows = false;
    }

    partial void OnIsCheckedChanged(bool value) => SetAllRows(value);
}

/// <summary>
/// One group of the views grid (7.5.0): its header label, whether it is unfolded, and the
/// header checkbox. The grid groups rows by this object, so it sorts in view-type order.
/// </summary>
public partial class ViewGroupState : ObservableObject, IComparable
{
    /// <summary>The view-type bucket this group lists.</summary>
    public ViewTypeGroup Group { get; }

    /// <summary>Header text, e.g. "Floor Plans".</summary>
    public string Label { get; }

    /// <summary>True: the group's rows are shown. False: only the header row.</summary>
    [ObservableProperty] private bool isExpanded = true;

    /// <summary>Header checkbox: true = every listed row ticked, false = none, null = some.</summary>
    [ObservableProperty] private bool? isChecked = false;

    /// <summary>Creates the state for one view-type group.</summary>
    public ViewGroupState(ViewTypeGroup group, string label)
    {
        Group = group;
        Label = label;
    }

    /// <summary>Orders groups like the filter popover (the enum's declaration order).</summary>
    public int CompareTo(object? obj) =>
        obj is ViewGroupState other ? Group.CompareTo(other.Group) : 1;

    /// <summary>The label, so UI automation and debuggers show a readable name.</summary>
    public override string ToString() => Label;
}

/// <summary>One tick box in the "Load by type" list: a view-type group that can be loaded on its own.</summary>
public partial class LoadTypeOption : ObservableObject
{
    /// <summary>The view-type group this row loads.</summary>
    public ViewTypeGroup Group { get; }
    /// <summary>Text shown next to the tick box, e.g. "Floor Plans".</summary>
    public string Label { get; }

    /// <summary>Ticked by the user to include the group in the next load.</summary>
    [ObservableProperty] private bool isChecked;
    /// <summary>True once the whole group has been loaded from the project.</summary>
    [ObservableProperty] private bool isLoaded;

    /// <summary>Creates the row for <paramref name="group"/>.</summary>
    public LoadTypeOption(ViewTypeGroup group, string label)
    {
        Group = group;
        Label = label;
    }
}

/// <summary>ObservableCollection that can take many items with one change notification.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Creates the collection with its first items.</summary>
    public BulkObservableCollection(IEnumerable<T> items) : base(items) { }

    /// <summary>Adds all <paramref name="items"/> and raises a single Reset, instead of one event per item.</summary>
    public void AddRange(IReadOnlyCollection<T> items)
    {
        if (items.Count == 0) return;
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}

public partial class ViewsListViewModel : ObservableObject
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex StripPunctuation = new(@"[.:#'""]", RegexOptions.Compiled);
    private readonly BulkObservableCollection<ViewItemViewModel> _views;

    // ── Collections ─────────────────────────────────────────────────────────
    public ObservableCollection<ViewItemViewModel> Views     { get; }
    public ICollectionView                          ViewsGrid { get; }
    public ObservableCollection<string>             SheetFilters { get; } = new();
    public ObservableCollection<LogEntry>           Logs         { get; } = new();

    public ObservableCollection<ViewTypeFilterGroup> ViewTypeFilterGroups { get; } = new();

    public IEnumerable<DuplicateFixStrategy> DuplicateFixStrategies { get; } = Enum.GetValues<DuplicateFixStrategy>();
    public IEnumerable<StandardizeCaseOption> StandardizeCaseOptions { get; } = Enum.GetValues<StandardizeCaseOption>();

    // ── Filter bar / popover state ──────────────────────────────────────────
    [ObservableProperty] private string selectedSheetFilter = "All";
    [ObservableProperty] private string sheetSearchText     = "";
    [ObservableProperty] private string viewNameSearchText  = "";
    [ObservableProperty] private bool   isFilterPopoverOpen;
    [ObservableProperty] private bool   showPlacedOnSheet = true;
    [ObservableProperty] private bool   showNotPlaced     = true;
    [ObservableProperty] private int    activeFilterCount;
    public bool HasActiveFilters => ActiveFilterCount > 0;

    // ── Quick filter chips (one-click triage, separate from popover) ────────
    [ObservableProperty] private QuickFilterMode activeQuickFilter = QuickFilterMode.All;
    [ObservableProperty] private int allCount;
    [ObservableProperty] private int unplacedCount;
    [ObservableProperty] private int thisSheetCount;
    [ObservableProperty] private int duplicatesCount;
    [ObservableProperty] private int selectedChipCount;

    // ── Rename panel — Prefix/Postfix, Find/Replace, Options ─────────────────
    [ObservableProperty] private string prefix        = "";
    [ObservableProperty] private string postfix       = "";
    [ObservableProperty] private string findText      = "";
    [ObservableProperty] private string replaceText   = "";
    [ObservableProperty] private bool   addSerial;
    [ObservableProperty] private string serialFormat        = "00";
    [ObservableProperty] private bool   includeDetailNumber;
    [ObservableProperty] private string commonEditName      = "";

    // ── Standardize row ────────────────────────────────────────────────────
    [ObservableProperty] private bool                  standardizeEnabled = true;
    [ObservableProperty] private StandardizeCaseOption  standardizeCase    = StandardizeCaseOption.TitleCase;
    [ObservableProperty] private bool                   cleanWhitespacePunctuation = true;

    // ── Action bar ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool   isDryRun = true;
    [ObservableProperty] private DuplicateFixStrategy duplicateStrategy = DuplicateFixStrategy.NumberedBrackets;

    // ToolWindowShell status strip (CLAUDE.md v1.1)
    [ObservableProperty] private bool   isRunning;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string summaryText = "";

    // ── Grid grouping and log panel (7.3.0) ──────────────────────────────────
    /// <summary>True: the grid lists the views under a header per view-type group. On by default; saved in settings.</summary>
    [ObservableProperty] private bool groupByViewType = true;

    /// <summary>False (default): the log shows as one bar with the latest line. Not saved: every open starts minimised.</summary>
    [ObservableProperty] private bool isLogExpanded;

    /// <summary>The newest log line, shown in the bar while the log is minimised.</summary>
    [ObservableProperty] private LogEntry? latestLog;

    /// <summary>True (default): the Prefix / Find / Options / Standardize panel is shown. False: one bar with a summary. Saved in settings.</summary>
    [ObservableProperty] private bool isRenamePanelExpanded = true;

    /// <summary>The rename options in use, shown in the bar while the rename panel is folded.</summary>
    [ObservableProperty] private string renameSummary = "";

    // One state object per view-type group, shared by that group's rows.
    private readonly Dictionary<ViewTypeGroup, ViewGroupState> _groupStates = new();
    private bool _groupChecksPending;

    partial void OnGroupByViewTypeChanged(bool value) => ApplyGrouping();

    private ViewGroupState GroupStateFor(ViewTypeGroup group)
    {
        if (!_groupStates.TryGetValue(group, out var state))
            _groupStates[group] = state = new ViewGroupState(group, GroupLabel(group));
        return state;
    }

    // Wires a row into the list: its group object and the selection listener.
    private void Attach(ViewItemViewModel item)
    {
        item.GroupState = GroupStateFor(item.TypeGroup);
        item.PropertyChanged += OnItemPropertyChanged;
    }

    /// <summary>Group header checkbox: ticks every listed row of the group, or unticks them when all were ticked.</summary>
    [RelayCommand]
    private void ToggleGroupSelection(ViewGroupState? group)
    {
        if (group == null) return;
        bool select = group.IsChecked != true;

        _suppressSelectionPreview = true;
        foreach (var v in Views.Where(x => x.TypeGroup == group.Group && PassesAllFilters(x)))
            v.IsSelected = select;
        _suppressSelectionPreview = false;

        group.IsChecked = select;
        UpdateSelectedCount();
        RunPreview();
    }

    /// <summary>Unfolds every group of the grid.</summary>
    [RelayCommand]
    private void ExpandAllGroups()
    {
        foreach (var g in _groupStates.Values) g.IsExpanded = true;
    }

    /// <summary>Folds every group of the grid down to its header row.</summary>
    [RelayCommand]
    private void CollapseAllGroups()
    {
        foreach (var g in _groupStates.Values) g.IsExpanded = false;
    }

    // Header ticks follow the listed (filtered) rows. Coalesced: a bulk select changes
    // thousands of rows, and one pass after it is enough.
    private void ScheduleGroupChecks()
    {
        if (_groupChecksPending) return;
        _groupChecksPending = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateGroupChecks));
    }

    private void UpdateGroupChecks()
    {
        _groupChecksPending = false;
        var total = new Dictionary<ViewTypeGroup, int>();
        var ticked = new Dictionary<ViewTypeGroup, int>();
        foreach (var v in Views)
        {
            if (!PassesAllFilters(v)) continue;
            total[v.TypeGroup] = total.GetValueOrDefault(v.TypeGroup) + 1;
            if (v.IsSelected) ticked[v.TypeGroup] = ticked.GetValueOrDefault(v.TypeGroup) + 1;
        }
        foreach (var g in _groupStates.Values)
        {
            int n = total.GetValueOrDefault(g.Group), on = ticked.GetValueOrDefault(g.Group);
            g.IsChecked = on == 0 ? false : on == n ? true : null;
        }
    }

    // Text for the folded rename bar: only the options that currently do something.
    private void UpdateRenameSummary()
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(Prefix))   parts.Add($"Prefix \"{Prefix}\"");
        if (!string.IsNullOrEmpty(Postfix))  parts.Add($"Postfix \"{Postfix}\"");
        if (!string.IsNullOrEmpty(FindText)) parts.Add($"Find \"{FindText}\" → \"{ReplaceText}\"");
        if (AddSerial)                       parts.Add($"Serial {SerialFormat}");
        if (IncludeDetailNumber)             parts.Add("Detail no.");
        if (StandardizeEnabled)
            parts.Add(CleanWhitespacePunctuation ? $"Standardize: {StandardizeCase}, clean" : $"Standardize: {StandardizeCase}");
        RenameSummary = parts.Count == 0 ? "No rename options set" : string.Join("  ·  ", parts);
    }

    /// <summary>Header text for a view-type group, e.g. "Floor Plans".</summary>
    public static string GroupLabel(ViewTypeGroup group)
    {
        foreach (var (g, label) in GroupOrder)
            if (g == group) return label;
        return group.ToString();
    }

    // Groups the grid by ViewTypeGroup (the same buckets as the filter popover and the
    // duplicate check), in the popover's order, or removes the grouping.
    private void ApplyGrouping()
    {
        using (ViewsGrid.DeferRefresh())
        {
            ViewsGrid.GroupDescriptions.Clear();
            if (!GroupByViewType) return;

            // The group name is the row's ViewGroupState, which compares in view-type order.
            var byGroup = new PropertyGroupDescription(nameof(ViewItemViewModel.GroupState));
            byGroup.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            ViewsGrid.GroupDescriptions.Add(byGroup);
        }
    }

    // ── Cancellable run (Esc / Cancel button) ────────────────────────────────
    private ToolCancellation? _activeRun;

    /// <summary>The tool window; set by its code-behind so the Yes/No dialog and the Escape key use it.</summary>
    public System.Windows.Window? OwnerWindow { get; set; }

    /// <summary>
    /// Starts a cancellable run on the Revit thread: shows the Cancel button, reports into
    /// <see cref="Progress"/> / <see cref="SummaryText"/>. Pair with <see cref="EndRun"/> in a finally block.
    /// </summary>
    public ToolCancellation BeginRun(ToolInfo tool)
    {
        _activeRun = ToolCancellation.Begin(tool, OwnerWindow, (percent, text) =>
        {
            Progress = percent;
            SummaryText = text;
        });
        Progress = 0;
        IsRunning = true;
        return _activeRun;
    }

    /// <summary>Ends the run started by <see cref="BeginRun"/>.</summary>
    public void EndRun(ToolCancellation run)
    {
        IsRunning = false;
        SummaryText = "";
        run?.Dispose();
        if (ReferenceEquals(_activeRun, run)) _activeRun = null;
    }

    /// <summary>Cancel button: asks the running operation to cancel (a Yes/No confirmation follows).</summary>
    [RelayCommand]
    private void CancelRun() => _activeRun?.RequestCancel();

    /// <summary>Called when the user cancelled "Load all views". Nothing in the model was changed.</summary>
    public void OnLoadAllCancelled(int done, int total)
    {
        IsLoadingViews = false;
        LogWarning(total > 0
            ? $"Cancelled by user: loading stopped after {done} of {total} views. No views were added."
            : "Cancelled by user: loading stopped. No views were added.");
    }

    /// <summary>Called when the user cancelled a rename run. Every rename of the run was undone.</summary>
    public void OnRenameCancelled(int done, int total) =>
        LogWarning($"Cancelled by user after {done} of {total} views: all renames of this run were undone.");

    // ── Derived summary ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TruncateLastNumberCommand))]
    [NotifyCanExecuteChangedFor(nameof(TruncateLastTextCommand))]
    private int selectedCount;

    // 7.1.0: false until "Load all views" has brought in the rest of the project.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadAllViewsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadTickedTypesCommand))]
    private bool allViewsLoaded;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadAllViewsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadTickedTypesCommand))]
    private bool isLoadingViews;

    private string _activeSheetNumber;
    private ViewAutoRenamerSettings _openSettings = new();

    // ── Load by type (7.4.0) ────────────────────────────────────────────────
    /// <summary>The view-type groups offered in the "Load by type" list.</summary>
    public ObservableCollection<LoadTypeOption> LoadTypeOptions { get; } = new();
    /// <summary>True while the "Load by type" list is open.</summary>
    [ObservableProperty] private bool isLoadTypePopupOpen;
    private readonly DispatcherTimer _previewTimer;
    private bool _suppressSelectionPreview;

    // Filter changes are debounced so typing or a group tick doesn't re-filter thousands of rows per event.
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    // ViewTypes whose popover tick is off. Looked up per row, so kept as a set instead of searching the groups.
    private readonly HashSet<Autodesk.Revit.DB.ViewType> _uncheckedTypes = new();

    // ── Constructor ─────────────────────────────────────────────────────────
    /// <param name="views">Views loaded before the window opens (the current sheet's).</param>
    /// <param name="activeSheetNumber">Sheet the tool was started on; empty when none.</param>
    /// <param name="allViewsLoaded">False while the rest of the project is still to be loaded.</param>
    /// <param name="loadTime">How long reading <paramref name="views"/> from Revit took; shown in the log.</param>
    /// <param name="initialLoadPending">True when the window opens empty and <see cref="SetInitialViews"/> follows (7.4.0).</param>
    public ViewsListViewModel(IEnumerable<ViewItemViewModel> views, string activeSheetNumber, bool allViewsLoaded = true, TimeSpan? loadTime = null, bool initialLoadPending = false)
    {
        isLoadingViews = initialLoadPending;
        foreach (var (group, label) in GroupOrder)
            LoadTypeOptions.Add(new LoadTypeOption(group, label) { IsLoaded = allViewsLoaded });
        this.allViewsLoaded = allViewsLoaded;
        _activeSheetNumber = activeSheetNumber;
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); ViewsGrid.Refresh(); RecalculateActiveFilterCount(); ScheduleGroupChecks(); };

        _views = new BulkObservableCollection<ViewItemViewModel>(views);
        Views = _views;
        foreach (var v in Views)
            Attach(v);

        ViewsGrid        = CollectionViewSource.GetDefaultView(Views);
        ViewsGrid.Filter = PassesAllFilters;
        Logs.CollectionChanged += (_, _) => LatestLog = Logs.Count > 0 ? Logs[^1] : null;
        // Any re-filter changes which rows a group header's tick stands for.
        ((System.Collections.Specialized.INotifyCollectionChanged)ViewsGrid).CollectionChanged += (_, _) => ScheduleGroupChecks();

        BuildSheetFilters();

        // Must exist before any property setter below can fire — several of the
        // settings-driven assignments (StandardizeEnabled/-Case,
        // CleanWhitespacePunctuation) trigger OnXxxChanged -> SchedulePreview(),
        // which dereferences _previewTimer. A persisted settings value that
        // differs from the field's compile-time default flips the property and
        // fires that callback while still inside this constructor, so building
        // the timer any later throws a NullReferenceException on every open
        // once the user has saved a non-default value for one of these.
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RunPreview(); };

        var settings = ViewAutoRenamerSettingsService.Load();
        _openSettings = settings;
        BuildViewTypeFilterGroups(settings);

        ShowPlacedOnSheet          = settings.ShowPlacedOnSheet;
        ShowNotPlaced              = settings.ShowNotPlaced;
        SheetSearchText            = settings.SheetNumberContains ?? "";
        DuplicateStrategy          = settings.DuplicateStrategy;
        IsDryRun                   = settings.IsDryRun;
        StandardizeEnabled         = settings.StandardizeEnabled;
        StandardizeCase            = settings.StandardizeCase;
        CleanWhitespacePunctuation = settings.CleanWhitespacePunctuation;
        GroupByViewType            = settings.GroupByViewType;
        IsRenamePanelExpanded      = settings.IsRenamePanelExpanded;
        UpdateRenameSummary();
        ApplyGrouping(); // the setter above does not fire when the saved value equals the default

        SelectedSheetFilter =
            !string.IsNullOrWhiteSpace(activeSheetNumber) && SheetFilters.Contains(activeSheetNumber)
                ? activeSheetNumber
                : "All";

        RecalculateActiveFilterCount();

        UpdateSelectedCount();
        RecalculateQuickFilterCounts();
        if (initialLoadPending)
            LogInfo("Reading the current sheet's views…");
        else if (allViewsLoaded)
            LogInfo($"Loaded {Views.Count} views across {ViewTypeFilterGroups.Count} categories. Active sheet: {SelectedSheetFilter}");
        else if (string.IsNullOrWhiteSpace(activeSheetNumber))
            LogInfo("The active view is not on a sheet — no views loaded yet. Click \"Load all views\" to load the project's views.");
        else
            LogInfo($"Loaded {Views.Count} views from sheet {activeSheetNumber}{Took(loadTime)}. Click \"Load all views\" to load the rest of the project.");
    }

    private static string Took(TimeSpan? t) =>
        t == null ? "" : string.Format(CultureInfo.CurrentCulture, " in {0:0.0} s", t.Value.TotalSeconds);

    private bool CanLoadAllViews() => !AllViewsLoaded && !IsLoadingViews;

    /// <summary>
    /// Fills the grid with the current sheet's views. Called once, right after the window is
    /// shown (7.4.0: the window opens first, the views follow).
    /// </summary>
    /// <param name="items">In-scope views on the sheet; empty when the tool was not started on a sheet.</param>
    /// <param name="activeSheetNumber">Number of that sheet; empty when none.</param>
    /// <param name="timer">Started when the load began; its elapsed time is written to the log.</param>
    public void SetInitialViews(IReadOnlyCollection<ViewItemViewModel> items, string activeSheetNumber, System.Diagnostics.Stopwatch? timer = null)
    {
        _activeSheetNumber = activeSheetNumber ?? "";
        foreach (var item in items)
            Attach(item);

        AddSheetFilters(items);
        using (ViewsGrid.DeferRefresh())
        {
            _views.AddRange(items);
            BuildViewTypeFilterGroups(_openSettings); // the saved view-type ticks apply to the first load
            SelectedSheetFilter =
                !string.IsNullOrWhiteSpace(_activeSheetNumber) && SheetFilters.Contains(_activeSheetNumber)
                    ? _activeSheetNumber
                    : "All";
            _refreshTimer.Stop(); // the deferred refresh below already covers it
            RecalculateActiveFilterCount();
            UpdateSelectedCount();
            RecalculateQuickFilterCounts();
            RunPreview();
        }

        IsLoadingViews = false;
        if (string.IsNullOrWhiteSpace(_activeSheetNumber))
            LogInfo("The active view is not on a sheet — no views loaded yet. Click \"Load all views\" or \"Load by type\" to load the project's views.");
        else
            LogInfo($"Loaded {Views.Count} views from sheet {_activeSheetNumber}{Took(timer?.Elapsed)}. Click \"Load all views\" or \"Load by type\" to load the rest of the project.");
    }

    // Adds new sheet numbers in place — clearing the list would null the combo's selection.
    private void AddSheetFilters(IEnumerable<ViewItemViewModel> items)
    {
        var knownSheets = new HashSet<string>(SheetFilters);
        foreach (var item in items)
        {
            if (!item.IsPlaced || !knownSheets.Add(item.SheetNumber!)) continue;
            int at = 2;
            while (at < SheetFilters.Count && string.CompareOrdinal(SheetFilters[at], item.SheetNumber) < 0) at++;
            SheetFilters.Insert(at, item.SheetNumber!);
        }
    }

    /// <summary>Opens or closes the "Load by type" list.</summary>
    [RelayCommand]
    private void ToggleLoadTypePopup() => IsLoadTypePopupOpen = !IsLoadTypePopupOpen;

    /// <summary>Asks Revit to load only the view-type groups ticked in the "Load by type" list.</summary>
    [RelayCommand(CanExecute = nameof(CanLoadAllViews))]
    private void LoadTickedTypes()
    {
        var groups = new HashSet<ViewTypeGroup>(
            LoadTypeOptions.Where(o => o.IsChecked && !o.IsLoaded).Select(o => o.Group));
        if (groups.Count == 0)
        {
            LogWarning("Load by type: tick at least one view type that is not loaded yet.");
            return;
        }

        IsLoadTypePopupOpen = false;
        IsLoadingViews = true;
        LogInfo($"Loading {string.Join(", ", groups.Select(GroupLabel))}…");
        try { RevitEventManager.RequestLoadAll(this, groups); }
        catch (Exception ex) { OnLoadAllFailed(ex.Message); }
    }

    /// <summary>Asks Revit (via an external event) to load every remaining view.</summary>
    [RelayCommand(CanExecute = nameof(CanLoadAllViews))]
    private void LoadAllViews()
    {
        IsLoadTypePopupOpen = false;
        IsLoadingViews = true;
        LogInfo("Loading all views…");
        try { RevitEventManager.RequestLoadAll(this); }
        catch (Exception ex) { OnLoadAllFailed(ex.Message); }
    }

    /// <summary>Called on the UI thread when the background load failed.</summary>
    public void OnLoadAllFailed(string message)
    {
        IsLoadingViews = false;
        LogError($"Could not load all views: {message}");
    }

    /// <summary>
    /// Merges the full project view list into the grid: new views are appended,
    /// existing rows keep their edits and selection but get complete sheet placements.
    /// </summary>
    /// <param name="loaded">The loaded views: every in-scope view, or those of <paramref name="groups"/>.</param>
    /// <param name="timer">Started when the load began; its elapsed time is written to the log.</param>
    /// <param name="groups">The view-type groups that were loaded; null when the whole project was.</param>
    public void MergeLoadedViews(IEnumerable<ViewItemViewModel> loaded, System.Diagnostics.Stopwatch? timer = null, ISet<ViewTypeGroup>? groups = null)
    {
        var existing = Views.ToDictionary(v => v.ElementId);
        var added = new List<ViewItemViewModel>();

        foreach (var item in loaded)
        {
            if (existing.TryGetValue(item.ElementId, out var current))
            {
                current.UpdatePlacement(item.PlacedSheetNumbers);
                continue;
            }
            Attach(item);
            added.Add(item);
        }

        AddSheetFilters(added);

        // One grid rebuild for the whole load: the rows arrive as a single change, and the
        // filter / preview updates below all land in the same deferred refresh.
        using (ViewsGrid.DeferRefresh())
        {
            _views.AddRange(added);
            RebuildViewTypeFilterGroups();
            // The user asked for everything, so stop narrowing to the first sheet.
            SelectedSheetFilter = "All";
            _refreshTimer.Stop(); // the deferred refresh below already covers it
            RecalculateActiveFilterCount();
            UpdateSelectedCount();
            RecalculateQuickFilterCounts();
            RunPreview();
        }

        foreach (var option in LoadTypeOptions)
        {
            if (groups != null && !groups.Contains(option.Group)) continue;
            option.IsLoaded = true;
            option.IsChecked = false;
        }
        AllViewsLoaded = LoadTypeOptions.All(o => o.IsLoaded);
        IsLoadingViews = false;
        string what = groups == null ? "" : $" ({string.Join(", ", groups.Select(GroupLabel))})";
        LogSuccess($"Loaded {added.Count} more views{what}{Took(timer?.Elapsed)} — {Views.Count} in total.");
    }

    // Keeps the user's current ticks; view types seen for the first time start ticked.
    private void RebuildViewTypeFilterGroups()
    {
        var current = ViewTypeFilterGroups
            .SelectMany(g => g.Rows)
            .ToDictionary(r => r.ViewType.ToString(), r => r.IsChecked);
        var snapshot = new ViewAutoRenamerSettings
        {
            CheckedViewTypeNames = Views
                .Select(v => v.ViewType.ToString())
                .Distinct()
                .Where(n => !current.TryGetValue(n, out var on) || on)
                .ToList()
        };
        BuildViewTypeFilterGroups(snapshot);
    }

    private void RecalculateQuickFilterCounts()
    {
        AllCount          = Views.Count;
        UnplacedCount     = Views.Count(v => !v.IsPlaced);
        ThisSheetCount    = Views.Count(v => v.PlacedSheetNumbers.Contains(_activeSheetNumber));
        DuplicatesCount   = Views.Count(v => v.IsDuplicate);
        SelectedChipCount = Views.Count(v => v.IsSelected);
    }

    // ── View Type popover construction ──────────────────────────────────────
    private static readonly (ViewTypeGroup Group, string Label)[] GroupOrder =
    {
        (ViewTypeGroup.SectionOrCallout, "Sections & Callouts"),
        (ViewTypeGroup.FloorPlan,        "Floor Plans"),
        (ViewTypeGroup.CeilingPlan,      "Ceiling Plans"),
        (ViewTypeGroup.StructuralPlan,   "Structural Plans"),
        (ViewTypeGroup.AreaPlan,         "Area Plans"),
        (ViewTypeGroup.Elevation,        "Elevations"),
        (ViewTypeGroup.Drafting,         "Drafting Views"),
        (ViewTypeGroup.Legend,           "Legends"),
        (ViewTypeGroup.Schedule,         "Schedules"),
    };

    private void BuildViewTypeFilterGroups(ViewAutoRenamerSettings settings)
    {
        ViewTypeFilterGroups.Clear();

        // First run (no persisted state) -> default to all checked, per confirmed decision.
        bool hasPersistedState = settings.CheckedViewTypeNames is { Count: > 0 };
        var checkedSet = hasPersistedState
            ? new HashSet<string>(settings.CheckedViewTypeNames)
            : null;

        foreach (var (group, label) in GroupOrder)
        {
            var rows = Views
                .Where(v => v.TypeGroup == group)
                .GroupBy(v => v.ViewType)
                .Select(g => new ViewTypeFilterRow(
                    g.Key,
                    g.First().ViewTypeDisplay,
                    g.Count(),
                    isChecked: checkedSet == null || checkedSet.Contains(g.Key.ToString())))
                .OrderBy(r => r.Label)
                .ToList();

            if (rows.Count == 0) continue;

            var groupRow = new ViewTypeFilterGroup(group, label, rows);
            foreach (var r in rows)
                r.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(ViewTypeFilterRow.IsChecked)) return;
                    UpdateUncheckedTypes();
                    ScheduleGridRefresh();
                };

            ViewTypeFilterGroups.Add(groupRow);
        }
        UpdateUncheckedTypes();
    }

    private void UpdateUncheckedTypes()
    {
        _uncheckedTypes.Clear();
        foreach (var r in ViewTypeFilterGroups.SelectMany(g => g.Rows))
            if (!r.IsChecked) _uncheckedTypes.Add(r.ViewType);
    }

    private void ScheduleGridRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    // ── Filter helpers ───────────────────────────────────────────────────────
    private void BuildSheetFilters()
    {
        SheetFilters.Clear();
        SheetFilters.Add("All");
        SheetFilters.Add("None");
        foreach (var n in Views
            .Where(x => x.IsPlaced)
            .Select(x => x.SheetNumber)
            .Distinct()
            .OrderBy(x => x))
            SheetFilters.Add(n!);
    }

    private bool PassesAllFilters(object obj)
    {
        if (obj is not ViewItemViewModel v) return false;

        // ── View name search ──
        if (!string.IsNullOrWhiteSpace(ViewNameSearchText))
        {
            bool nameMatch = v.OriginalName.Contains(ViewNameSearchText, StringComparison.OrdinalIgnoreCase)
                          || (v.EditableName ?? "").Contains(ViewNameSearchText, StringComparison.OrdinalIgnoreCase);
            if (!nameMatch) return false;
        }

        // ── View Type popover checklist ──
        if (_uncheckedTypes.Contains(v.ViewType)) return false;

        // ── Placed / Not placed ──
        if (v.IsPlaced && !ShowPlacedOnSheet) return false;
        if (!v.IsPlaced && !ShowNotPlaced) return false;

        // ── Sheet-number-contains text (popover) ──
        if (!string.IsNullOrWhiteSpace(SheetSearchText))
        {
            bool anySheetMatches = v.PlacedSheetNumbers.Any(s => s.Contains(SheetSearchText, StringComparison.OrdinalIgnoreCase));
            if (!anySheetMatches) return false;
        }

        // ── Sheet ComboBox (single-select, alongside popover) ──
        bool sheetComboPasses = SelectedSheetFilter switch
        {
            "All"  => true,
            "None" => !v.IsPlaced,
            _      => v.PlacedSheetNumbers.Contains(SelectedSheetFilter)
        };
        if (!sheetComboPasses) return false;

        // ── Quick filter chip ──
        switch (ActiveQuickFilter)
        {
            case QuickFilterMode.Unplaced:
                if (v.IsPlaced) return false;
                break;
            case QuickFilterMode.ThisSheet:
                if (!v.PlacedSheetNumbers.Contains(_activeSheetNumber)) return false;
                break;
            case QuickFilterMode.DuplicatesOnly:
                if (!v.IsDuplicate) return false;
                break;
            case QuickFilterMode.SelectedOnly:
                if (!v.IsSelected) return false;
                break;
        }

        return true;
    }

    partial void OnSelectedSheetFilterChanged(string value)  => ScheduleGridRefresh();
    partial void OnSheetSearchTextChanged(string value)      => ScheduleGridRefresh();
    partial void OnViewNameSearchTextChanged(string value)   => ScheduleGridRefresh();
    partial void OnShowPlacedOnSheetChanged(bool value)      => ScheduleGridRefresh();
    partial void OnShowNotPlacedChanged(bool value)          => ScheduleGridRefresh();
    partial void OnActiveQuickFilterChanged(QuickFilterMode value) { ViewsGrid.Refresh(); ScheduleGroupChecks(); }

    private void RecalculateActiveFilterCount()
    {
        int count = 0;
        var allRows = ViewTypeFilterGroups.SelectMany(g => g.Rows).ToList();
        if (allRows.Count > 0 && allRows.Any(r => !r.IsChecked)) count++;
        if (!ShowPlacedOnSheet || !ShowNotPlaced) count++;
        if (!string.IsNullOrWhiteSpace(SheetSearchText)) count++;
        if (SelectedSheetFilter != "All") count++;
        if (!string.IsNullOrWhiteSpace(ViewNameSearchText)) count++;
        ActiveFilterCount = count;
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    [RelayCommand]
    private void ToggleFilterPopover() => IsFilterPopoverOpen = !IsFilterPopoverOpen;

    [RelayCommand]
    private void ApplyFilterPopover()
    {
        IsFilterPopoverOpen = false;
        RecalculateActiveFilterCount();
        SaveSettings();
        ViewsGrid.Refresh();
    }

    [RelayCommand]
    private void ResetFilterPopover()
    {
        foreach (var g in ViewTypeFilterGroups)
            g.SetAllRows(true);
        ShowPlacedOnSheet  = true;
        ShowNotPlaced      = true;
        SheetSearchText    = "";
        ViewNameSearchText = "";
        RecalculateActiveFilterCount();
        ViewsGrid.Refresh();
    }

    [RelayCommand]
    private void SetQuickFilter(string mode)
    {
        if (Enum.TryParse<QuickFilterMode>(mode, out var parsed))
            ActiveQuickFilter = parsed;
    }

    // ── Selection commands ───────────────────────────────────────────────────
    // Bulk toggles suppress the per-item RunPreview normally triggered by
    // OnItemPropertyChanged (each toggle would otherwise re-scan every row) and
    // run it once after the loop instead.
    [RelayCommand]
    private void SelectAll() => SetSelection(_ => true);

    [RelayCommand]
    private void SelectNone() => SetSelection(_ => false);

    [RelayCommand]
    private void InvertSelection() => SetSelection(v => !v.IsSelected);

    /// <summary>
    /// Sets the check mark of every row currently shown in the grid, then updates the counts,
    /// the "Selected" filter and the previews once (not once per row).
    /// </summary>
    public void SetSelection(Func<ViewItemViewModel, bool> newValue)
    {
        _suppressSelectionPreview = true;
        try
        {
            // Snapshot first: with the "Selected" chip active, a refresh would change the list being walked.
            foreach (var v in ViewsGrid.Cast<ViewItemViewModel>().ToList())
                v.IsSelected = newValue(v);
        }
        finally { _suppressSelectionPreview = false; }

        UpdateSelectedCount();
        SelectedChipCount = SelectedCount;
        if (ActiveQuickFilter == QuickFilterMode.SelectedOnly)
            ViewsGrid.Refresh();
        RunPreview();
    }

    [RelayCommand]
    private void ResetSelectedNames()
    {
        foreach (var v in Views.Where(x => x.IsSelected))
        {
            v.EditableName = v.OriginalName;
            v.PreviewName  = v.OriginalName;
        }
        LogInfo("Reset editable names for selected rows.");
        // Recompute across all rows rather than just clearing IsDuplicate on the
        // reset ones — a row that stays duplicated against one we just reset
        // needs its flag re-evaluated too, not left stale.
        DetectDuplicates();
    }

    /// <summary>
    /// Footer "Reset all names": puts every loaded row's edit name back to its original name,
    /// ticked or not, filtered out or not. Until 7.5.0 this button reset only the ticked rows.
    /// </summary>
    [RelayCommand]
    private void ResetAllNames()
    {
        int changed = 0;
        foreach (var v in Views)
        {
            if (v.EditableName == v.OriginalName) continue;
            v.EditableName = v.OriginalName;
            changed++;
        }
        LogInfo(changed == 0
            ? "Reset all names: no edited names to reset."
            : $"Reset all names: {changed} edited name(s) put back to the original.");
        RunPreview();
    }

    private bool HasSelection() => SelectedCount > 0;

    // Truncate edits EditableName (the pipeline's base name), not PreviewName —
    // PreviewName is fully recomputed by RunPreview on every subsequent change
    // (Prefix/Postfix/Standardize/...), so writing to it directly here meant the
    // truncation was silently discarded the next time anything else changed.
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void TruncateLastNumber()
    {
        int changed = 0;
        foreach (var v in Views.Where(x => x.IsSelected))
        {
            var name = v.EditableName;
            if (string.IsNullOrEmpty(name) || !char.IsDigit(name[^1])) continue;
            v.EditableName = name[..^1].TrimEnd();
            changed++;
        }
        if (changed == 0) LogWarning("No selected rows end in a number.");
        RunPreview();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void TruncateLastText()
    {
        int changed = 0;
        foreach (var v in Views.Where(x => x.IsSelected))
        {
            var name = v.EditableName;
            if (string.IsNullOrEmpty(name) || !char.IsLetter(name[^1])) continue;
            v.EditableName = name[..^1].TrimEnd();
            changed++;
        }
        if (changed == 0) LogWarning("No selected rows end in a letter.");
        RunPreview();
    }

    partial void OnCommonEditNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var v in Views.Where(x => x.IsSelected))
            v.EditableName = value;
        SchedulePreview();
    }

    // Parameter names must match the generator's "value" exactly — a differing
    // name (e.g. "_") makes this a distinct overload from the generated
    // partial declaration, so it silently never gets called (CS8826).
    partial void OnPrefixChanged(string value)              => SchedulePreview();
    partial void OnPostfixChanged(string value)             => SchedulePreview();
    partial void OnFindTextChanged(string value)            => SchedulePreview();
    partial void OnReplaceTextChanged(string value)         => SchedulePreview();
    partial void OnAddSerialChanged(bool value)             => SchedulePreview();
    partial void OnSerialFormatChanged(string value)        => SchedulePreview();
    partial void OnIncludeDetailNumberChanged(bool value)   => SchedulePreview();
    partial void OnStandardizeEnabledChanged(bool value)            => SchedulePreview();
    partial void OnStandardizeCaseChanged(StandardizeCaseOption value) => SchedulePreview();
    partial void OnCleanWhitespacePunctuationChanged(bool value)    => SchedulePreview();

    private void SchedulePreview()
    {
        UpdateRenameSummary();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    [RelayCommand]
    private void Preview() => RunPreview(logResult: true);

    /// <summary>
    /// Recomputes PreviewName for every row. The batch transforms (Standardize,
    /// Find/Replace, Prefix/Postfix, Detail number, Serial) only apply to
    /// currently-SELECTED rows — they're the rename panel's bulk-edit controls,
    /// scoped the same way Commit/Truncate/Reset already are. An unselected
    /// row's Preview simply mirrors its EditableName, so a direct in-grid edit
    /// still shows up without pulling in batch transforms meant for the checked
    /// rows only.
    /// Runs silently (no log line) on every debounced keystroke via
    /// SchedulePreview — logging there would spam the log panel while the user
    /// is still typing. A log entry is only written for an explicit Preview
    /// click or right before commit, where "preview updated" is actually
    /// meaningful information.
    /// </summary>
    private void RunPreview(bool logResult = false)
    {
        foreach (var v in Views)
        {
            var baseName = v.EditableName ?? v.OriginalName;

            if (!v.IsSelected)
            {
                v.PreviewName = baseName;
                continue;
            }

            if (StandardizeEnabled)
                baseName = ApplyStandardize(baseName);

            if (!string.IsNullOrEmpty(FindText))
                baseName = baseName.Replace(FindText, ReplaceText ?? "");

            var detail = IncludeDetailNumber && !string.IsNullOrEmpty(v.DetailNumber)
                ? $" {v.DetailNumber}" : "";

            var raw = $"{Prefix}{baseName}{Postfix}{detail}";
            raw = WhitespaceRun.Replace(raw, " ").Trim();

            v.PreviewName = raw;
        }

        if (AddSerial)
        {
            // Serial applies across the FULL selected set in stable row order
            // (not just the currently-filtered view) — otherwise changing a
            // grid filter after preview would leave newly-revealed rows with
            // stale/missing serial suffixes.
            //
            // SerialFormat is free-form user text handed to int.ToString(format);
            // an invalid custom numeric format (e.g. stray letters) throws
            // FormatException, which used to crash the debounced preview tick.
            // Validate it once per pass and fall back to a plain number instead.
            bool formatValid = true;
            try { _ = 1.ToString(SerialFormat); }
            catch (FormatException) { formatValid = false; }

            int i = 1;
            foreach (var v in Views.Where(x => x.IsSelected))
            {
                var serialText = formatValid ? i.ToString(SerialFormat) : i.ToString(CultureInfo.InvariantCulture);
                var raw = WhitespaceRun.Replace($"{v.PreviewName} {serialText}", " ").Trim();
                v.PreviewName = raw;
                i++;
            }

            if (!formatValid)
                LogWarning($"Serial format \"{SerialFormat}\" is invalid — using plain numbering instead.");
        }

        DetectDuplicates();

        if (logResult)
            LogInfo($"Preview updated — {SelectedCount} rows selected");
    }

    private string ApplyStandardize(string name)
    {
        if (CleanWhitespacePunctuation)
        {
            name = StripPunctuation.Replace(name, "");
            name = WhitespaceRun.Replace(name, " ").Trim();
        }

        name = StandardizeCase switch
        {
            StandardizeCaseOption.UpperCase    => name.ToUpper(CultureInfo.CurrentCulture),
            StandardizeCaseOption.TitleCase    => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower(CultureInfo.CurrentCulture)),
            StandardizeCaseOption.SentenceCase => ToSentenceCase(name),
            _                                   => name
        };

        return name;
    }

    private static string ToSentenceCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var lower = name.ToLower(CultureInfo.CurrentCulture);
        return char.ToUpper(lower[0], CultureInfo.CurrentCulture) + lower.Substring(1);
    }

    /// <summary>
    /// Duplicate detection is scoped PER ViewTypeGroup, matching Revit's
    /// actual name-uniqueness rule (Section+Callout share one namespace;
    /// each other view-type family is independent — a Floor Plan and a
    /// Drafting View may legally share a name).
    /// </summary>
    private void DetectDuplicates()
    {
        foreach (var v in Views) v.IsDuplicate = false;

        var groups = Views
            .GroupBy(v => v.TypeGroup)
            .SelectMany(typeGroup => typeGroup
                .GroupBy(v => v.PreviewName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1));

        bool any = false;
        foreach (var g in groups)
        {
            any = true;
            foreach (var v in g) v.IsDuplicate = true;
        }

        if (any)
            LogWarning("Duplicate preview names detected within a view-type group — auto-fix will apply on commit.");

        DuplicatesCount = Views.Count(v => v.IsDuplicate);
        if (ActiveQuickFilter == QuickFilterMode.DuplicatesOnly)
            ViewsGrid.Refresh();
    }

    // ── Commit ───────────────────────────────────────────────────────────────
    [RelayCommand]
    private void CommitRename()
    {
        // Force any pending debounced preview to run synchronously first —
        // guards against committing stale PreviewName values if the user
        // clicks Commit within the 300ms debounce window after typing.
        if (_previewTimer.IsEnabled)
        {
            _previewTimer.Stop();
            RunPreview();
        }

        if (IsDryRun)
        {
            LogWarning("Dry run is ON — no changes committed.");
            return;
        }

        var selected = Views.Where(v => v.IsSelected).ToList();
        if (selected.Count == 0)
        {
            LogWarning("No rows selected — nothing to commit.");
            return;
        }

        SaveSettings();
        RevitEventManager.RequestRename(selected, this);
    }

    // ── Settings persistence ─────────────────────────────────────────────────
    public void SaveSettings()
    {
        var settings = new ViewAutoRenamerSettings
        {
            Version = ViewAutoRenamerSettings.CurrentVersion,
            CheckedViewTypeNames = ViewTypeFilterGroups
                .SelectMany(g => g.Rows)
                .Where(r => r.IsChecked)
                .Select(r => r.ViewType.ToString())
                .ToList(),
            ShowPlacedOnSheet          = ShowPlacedOnSheet,
            ShowNotPlaced              = ShowNotPlaced,
            SheetNumberContains        = SheetSearchText,
            DuplicateStrategy          = DuplicateStrategy,
            IsDryRun                   = IsDryRun,
            StandardizeEnabled         = StandardizeEnabled,
            StandardizeCase            = StandardizeCase,
            CleanWhitespacePunctuation = CleanWhitespacePunctuation,
            GroupByViewType            = GroupByViewType,
            IsRenamePanelExpanded      = IsRenamePanelExpanded,
        };
        ViewAutoRenamerSettingsService.Save(settings);
    }

    // ── Selection count ──────────────────────────────────────────────────────
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewItemViewModel.IsSelected))
        {
            // Bulk commands (SetSelection) count, refresh and preview once after their loop.
            if (_suppressSelectionPreview) return;

            // One row changed: adjust the count instead of recounting every row.
            if (sender is ViewItemViewModel changed)
                SelectedCount += changed.IsSelected ? 1 : -1;
            SelectedChipCount = SelectedCount;
            if (ActiveQuickFilter == QuickFilterMode.SelectedOnly)
                ViewsGrid.Refresh();

            // Batch transforms in RunPreview are selection-scoped (see RunPreview) —
            // a row that just got checked/unchecked needs its Preview recomputed
            // immediately under the current Prefix/Postfix/Standardize/... state,
            // not left showing whatever it had before the toggle. Bulk commands
            // (SelectAll/SelectNone/InvertSelection) suppress this and call
            // RunPreview once after the loop instead.
            SchedulePreview();
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = Views.Count(v => v.IsSelected);
        ScheduleGroupChecks();
    }

    // ── Log helpers ──────────────────────────────────────────────────────────
    public void LogInfo(string m)    => Logs.Add(new LogEntry(LogLevel.Info,    m));
    public void LogWarning(string m) => Logs.Add(new LogEntry(LogLevel.Warning, m));
    public void LogError(string m)   => Logs.Add(new LogEntry(LogLevel.Error,   m));
    public void LogSuccess(string m) => Logs.Add(new LogEntry(LogLevel.Success, m));
}

public enum QuickFilterMode
{
    All,
    Unplaced,
    ThisSheet,
    DuplicatesOnly,
    SelectedOnly
}
