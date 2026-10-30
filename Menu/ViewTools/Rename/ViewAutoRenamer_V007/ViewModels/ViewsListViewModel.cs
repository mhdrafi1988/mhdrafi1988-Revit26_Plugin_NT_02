using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Models;
using Revit26_Plugin_Revised.ViewAutoRenamer.V007.Services;
using Revit26_Plugin_Revised.Shared.Models;
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

public partial class ViewsListViewModel : ObservableObject
{
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

    // ── Derived summary ─────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TruncateLastNumberCommand))]
    [NotifyCanExecuteChangedFor(nameof(TruncateLastTextCommand))]
    private int selectedCount;

    // 7.1.0: false until "Load all views" has brought in the rest of the project.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadAllViewsCommand))]
    private bool allViewsLoaded;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadAllViewsCommand))]
    private bool isLoadingViews;

    private readonly string _activeSheetNumber;
    private readonly DispatcherTimer _previewTimer;
    private bool _suppressSelectionPreview;

    // Filter changes are debounced so typing or a group tick doesn't re-filter thousands of rows per event.
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    // ViewTypes whose popover tick is off. Looked up per row, so kept as a set instead of searching the groups.
    private readonly HashSet<Autodesk.Revit.DB.ViewType> _uncheckedTypes = new();

    // ── Constructor ─────────────────────────────────────────────────────────
    public ViewsListViewModel(IEnumerable<ViewItemViewModel> views, string activeSheetNumber, bool allViewsLoaded = true)
    {
        this.allViewsLoaded = allViewsLoaded;
        _activeSheetNumber = activeSheetNumber;
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); ViewsGrid.Refresh(); RecalculateActiveFilterCount(); };

        Views = new ObservableCollection<ViewItemViewModel>(views);
        foreach (var v in Views)
            v.PropertyChanged += OnItemPropertyChanged;

        ViewsGrid        = CollectionViewSource.GetDefaultView(Views);
        ViewsGrid.Filter = PassesAllFilters;

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
        BuildViewTypeFilterGroups(settings);

        ShowPlacedOnSheet          = settings.ShowPlacedOnSheet;
        ShowNotPlaced              = settings.ShowNotPlaced;
        SheetSearchText            = settings.SheetNumberContains ?? "";
        DuplicateStrategy          = settings.DuplicateStrategy;
        IsDryRun                   = settings.IsDryRun;
        StandardizeEnabled         = settings.StandardizeEnabled;
        StandardizeCase            = settings.StandardizeCase;
        CleanWhitespacePunctuation = settings.CleanWhitespacePunctuation;

        SelectedSheetFilter =
            !string.IsNullOrWhiteSpace(activeSheetNumber) && SheetFilters.Contains(activeSheetNumber)
                ? activeSheetNumber
                : "All";

        RecalculateActiveFilterCount();

        UpdateSelectedCount();
        RecalculateQuickFilterCounts();
        if (allViewsLoaded)
            LogInfo($"Loaded {Views.Count} views across {ViewTypeFilterGroups.Count} categories. Active sheet: {SelectedSheetFilter}");
        else if (string.IsNullOrWhiteSpace(activeSheetNumber))
            LogInfo("The active view is not on a sheet — no views loaded yet. Click \"Load all views\" to load the project's views.");
        else
            LogInfo($"Loaded {Views.Count} views from sheet {activeSheetNumber}. Click \"Load all views\" to load the rest of the project.");
    }

    private bool CanLoadAllViews() => !AllViewsLoaded && !IsLoadingViews;

    /// <summary>Asks Revit (via an external event) to load every remaining view.</summary>
    [RelayCommand(CanExecute = nameof(CanLoadAllViews))]
    private void LoadAllViews()
    {
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
    public void MergeLoadedViews(IEnumerable<ViewItemViewModel> loaded)
    {
        var existing = Views.ToDictionary(v => v.ElementId);
        int added = 0;

        _suppressSelectionPreview = true;
        using var deferred = ViewsGrid.DeferRefresh();
        foreach (var item in loaded)
        {
            if (existing.TryGetValue(item.ElementId, out var current))
            {
                current.UpdatePlacement(item.PlacedSheetNumbers);
                continue;
            }
            item.PropertyChanged += OnItemPropertyChanged;
            Views.Add(item);
            added++;
        }
        _suppressSelectionPreview = false;

        RebuildViewTypeFilterGroups();
        // Add new sheet numbers in place — clearing the list would null the combo's selection.
        foreach (var n in Views.Where(x => x.IsPlaced).Select(x => x.SheetNumber!).Distinct()
                     .Where(n => !SheetFilters.Contains(n)))
        {
            int at = 2;
            while (at < SheetFilters.Count && string.CompareOrdinal(SheetFilters[at], n) < 0) at++;
            SheetFilters.Insert(at, n);
        }
        // The user asked for everything, so stop narrowing to the first sheet.
        SelectedSheetFilter = "All";
        RecalculateActiveFilterCount();
        UpdateSelectedCount();
        RecalculateQuickFilterCounts();
        RunPreview();
        ViewsGrid.Refresh();

        AllViewsLoaded = true;
        IsLoadingViews = false;
        LogSuccess($"Loaded {added} more views — {Views.Count} in total.");
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
    partial void OnActiveQuickFilterChanged(QuickFilterMode value) => ViewsGrid.Refresh();

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
    private void SelectAll()
    {
        _suppressSelectionPreview = true;
        foreach (var v in ViewsGrid.Cast<ViewItemViewModel>())
            v.IsSelected = true;
        _suppressSelectionPreview = false;
        UpdateSelectedCount();
        RunPreview();
    }

    [RelayCommand]
    private void SelectNone()
    {
        _suppressSelectionPreview = true;
        foreach (var v in ViewsGrid.Cast<ViewItemViewModel>())
            v.IsSelected = false;
        _suppressSelectionPreview = false;
        UpdateSelectedCount();
        RunPreview();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        _suppressSelectionPreview = true;
        foreach (var v in ViewsGrid.Cast<ViewItemViewModel>())
            v.IsSelected = !v.IsSelected;
        _suppressSelectionPreview = false;
        UpdateSelectedCount();
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
            raw = Regex.Replace(raw, @"\s+", " ").Trim();

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
                var raw = Regex.Replace($"{v.PreviewName} {serialText}", @"\s+", " ").Trim();
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
            name = Regex.Replace(name, @"[.:#'""]", "");
            name = Regex.Replace(name, @"\s+", " ").Trim();
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
        };
        ViewAutoRenamerSettingsService.Save(settings);
    }

    // ── Selection count ──────────────────────────────────────────────────────
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewItemViewModel.IsSelected))
        {
            UpdateSelectedCount();
            SelectedChipCount = SelectedCount;
            if (ActiveQuickFilter == QuickFilterMode.SelectedOnly)
                ViewsGrid.Refresh();

            // Batch transforms in RunPreview are selection-scoped (see RunPreview) —
            // a row that just got checked/unchecked needs its Preview recomputed
            // immediately under the current Prefix/Postfix/Standardize/... state,
            // not left showing whatever it had before the toggle. Bulk commands
            // (SelectAll/SelectNone/InvertSelection) suppress this and call
            // RunPreview once after the loop instead.
            if (!_suppressSelectionPreview)
                SchedulePreview();
        }
    }

    private void UpdateSelectedCount() =>
        SelectedCount = Views.Count(v => v.IsSelected);

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
