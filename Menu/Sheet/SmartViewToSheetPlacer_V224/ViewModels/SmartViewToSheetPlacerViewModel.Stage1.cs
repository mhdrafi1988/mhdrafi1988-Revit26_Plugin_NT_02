using System;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.Models;
using Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.Services;

namespace Revit26_Plugin_Revised.SmartViewToSheetPlacer.V224.ViewModels
{
    /// <summary>Stage 1: Select Views — view/titleblock loading, selection, filtering.</summary>
    public partial class SmartViewToSheetPlacerViewModel
    {
        // ---- Stage 1 state ----
        [ObservableProperty] private string _viewNameFilter = string.Empty;
        [ObservableProperty] private TitleblockOption? _selectedTitleblock;
        [ObservableProperty] private double _marginTopMm = 10.0;
        [ObservableProperty] private double _marginBottomMm = 10.0;
        [ObservableProperty] private double _marginLeftMm = 10.0;
        [ObservableProperty] private double _marginRightMm = 10.0;

        /// <summary>
        /// V222: global H/V gap, moved here next to the Margin fields on the
        /// Titleblock card — reverted from the per-ViewType-group gap cards
        /// (confirmed with Rafi). Changing either value pushes it onto every
        /// entry in GapSettingsGroups (Stage 2) so all groups always share the
        /// same gap; new groups pick it up too (EnsureGapSettingsGroups).
        /// </summary>
        [ObservableProperty] private double _globalHorizontalGapMm = 5.0;
        [ObservableProperty] private double _globalVerticalGapMm = 5.0;

        partial void OnGlobalHorizontalGapMmChanged(double value)
        {
            foreach (var g in GapSettingsGroups)
                g.HorizontalGapMm = value;
        }

        partial void OnGlobalVerticalGapMmChanged(double value)
        {
            foreach (var g in GapSettingsGroups)
                g.VerticalGapMm = value;
        }

        /// <summary>
        /// Height in mm kept free below every view for its viewport title. 0 (the default)
        /// reserves nothing, as before 224.3.0. Changing it repacks.
        /// </summary>
        [ObservableProperty] private double _titleSpaceMm;

        partial void OnTitleSpaceMmChanged(double value)
        {
            foreach (var v in AllViews)
                v.TitleSpaceMm = value;
            RepackIfReady();
        }

        [ObservableProperty] private int _selectedViewCount;
        [ObservableProperty] private int _totalViewCount;
        [ObservableProperty] private bool _stage1Complete;
        public string Stage1StatusLabel => Stage1Complete ? "Complete" : "In Progress";

        // ---- Stage 1: View Name always-visible filter, View Type popover filter ----
        [ObservableProperty] private bool _isViewTypeFilterOpen;

        /// <summary>
        /// V213: guard flag set while THIS ViewModel is bulk-assigning
        /// IsSelected itself (SelectAllViews/ClearSelection/popover-driven
        /// ApplyViewTypeSelectionFromPopover). OnViewInfoPropertyChanged
        /// checks this before marking a row as "manually set by the user" —
        /// without it, every bulk operation would look identical to a real
        /// user click on the binding, since both go through the same
        /// IsSelected setter. Confirmed with Rafi: only individual per-row
        /// checkbox clicks are "manual" (sticky, locks that row against
        /// future popover auto-toggle); Select All / Clear Selection button
        /// clicks are NOT manual (popover can still override those rows later).
        /// </summary>
        private bool _isBulkAssigningSelection;

        /// <summary>True when the View Name filter box has an active (non-empty) value.</summary>
        public bool IsViewNameFilterActive => !string.IsNullOrWhiteSpace(ViewNameFilter);

        /// <summary>True when at least one View Type is excluded from the filter.</summary>
        public bool IsViewTypeFilterActive => ViewTypeFilters.Any(f => !f.IsChecked);

        /// <summary>
        /// V213: All/Placed/Not-Placed toggle for Stage 1's view-selection
        /// grid. "Placed" reflects ViewInfo.IsAlreadyPlaced — a REAL Revit
        /// sheet placement (existing Viewport on some ViewSheet), detected
        /// when views load, independent of this tool session's own packing.
        /// Simple two-button toggle above the grid, not a popover — confirmed
        /// with Rafi.
        /// </summary>
        [ObservableProperty] private PlacementFilterMode _placementFilter = PlacementFilterMode.All;

        partial void OnPlacementFilterChanged(PlacementFilterMode value) => ViewsView.Refresh();

        /// <summary>
        /// Generic parameter-driven filter: FilterParameterOptions lists every
        /// parameter found POPULATED (non-blank on at least one loaded view) on
        /// ANY loaded view — built-in (View Name, View Scale, View Template, ...),
        /// project, and shared parameters bound to the Views category all show up
        /// here alike (union of ViewInfo.ParameterValues keys across AllViews,
        /// rebuilt in HandleLoadViewsCompleted; parameters blank on every view are
        /// excluded so the picker doesn't get cluttered with dead entries).
        /// Single-select, presented as a popover (ParameterFilterToggle/Popup in
        /// XAML) with a live search box over the list — confirmed with Rafi,
        /// same visual pattern as the View Type popover, but a single-select
        /// ListBox instead of a checkbox list (only one parameter/value pair can
        /// be active at a time). Picking one populates the dependent typable
        /// Value dropdown (FilterValueOptions) with that parameter's distinct
        /// values across AllViews. Purely a grid-visibility filter (like
        /// ViewNameFilter/PlacementFilter) — never touches IsSelected; the data
        /// grid's own checkboxes are the only thing that decides the final
        /// selection. Resets to unset on every LoadViews/Refresh.
        /// </summary>
        public ObservableCollection<string> FilterParameterOptions { get; } = new();
        public ObservableCollection<string> VisibleFilterParameterOptions { get; } = new();
        public ObservableCollection<string> FilterValueOptions { get; } = new();

        [ObservableProperty] private bool _isFilterParameterOpen;
        [ObservableProperty] private string _filterParameterSearchText = string.Empty;
        [ObservableProperty] private string? _selectedFilterParameter;
        [ObservableProperty] private string? _selectedFilterValue;

        private string? GetFilterParameterValue(ViewInfo v) =>
            !string.IsNullOrEmpty(SelectedFilterParameter) &&
            v.ParameterValues.TryGetValue(SelectedFilterParameter, out var val)
                ? val
                : null;

        partial void OnFilterParameterSearchTextChanged(string value) => RefreshVisibleFilterParameterOptions();

        private void RefreshVisibleFilterParameterOptions()
        {
            VisibleFilterParameterOptions.Clear();
            var matches = string.IsNullOrWhiteSpace(FilterParameterSearchText)
                ? FilterParameterOptions
                : FilterParameterOptions.Where(p => p.IndexOf(FilterParameterSearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            foreach (var p in matches)
                VisibleFilterParameterOptions.Add(p);
        }

        [RelayCommand]
        private void ClearFilterParameter() => SelectedFilterParameter = null;

        partial void OnSelectedFilterParameterChanged(string? value)
        {
            // Selecting (or clearing) a parameter closes the popover and resets
            // its search box, same as picking a value closes any other dropdown.
            IsFilterParameterOpen = false;
            FilterParameterSearchText = string.Empty;

            SelectedFilterValue = null;
            FilterValueOptions.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                foreach (var v in AllViews.Select(GetFilterParameterValue)
                             .Where(s => !string.IsNullOrEmpty(s))
                             .Distinct()
                             .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    FilterValueOptions.Add(v!);
            }
            ViewsView.Refresh();
        }

        partial void OnSelectedFilterValueChanged(string? value) => ViewsView.Refresh();

        [RelayCommand]
        private void SetPlacementFilterAll() => PlacementFilter = PlacementFilterMode.All;

        [RelayCommand]
        private void SetPlacementFilterPlaced() => PlacementFilter = PlacementFilterMode.Placed;

        [RelayCommand]
        private void SetPlacementFilterNotPlaced() => PlacementFilter = PlacementFilterMode.NotPlaced;

        private void RunLoadViews()
        {
            IsBusy = true;
            BusyMessage = "Loading project views...";
            Logs.Add(new LogEntry(LogLevel.Info, "Requesting view + titleblock load from Revit."));

            _handler.Request = SmartViewToSheetPlacerRequest.LoadViews;
            _event.Raise();
        }

        [RelayCommand]
        private void RefreshViews() => RunLoadViews();

        [RelayCommand]
        private void SelectAllViews()
        {
            _isBulkAssigningSelection = true;
            foreach (ViewInfo v in ViewsView)
                v.IsSelected = true;
            _isBulkAssigningSelection = false;
            RecomputeSelectedCount();
        }

        [RelayCommand]
        private void ClearSelection()
        {
            _isBulkAssigningSelection = true;
            foreach (ViewInfo v in ViewsView)
                v.IsSelected = false;
            _isBulkAssigningSelection = false;
            RecomputeSelectedCount();
        }

        /// <summary>
        /// V213: fires on ANY property change of ANY loaded ViewInfo — used
        /// specifically to detect a genuine user click on a row's IsSelected
        /// checkbox (as opposed to IsSelected being set by our own bulk code
        /// in SelectAllViews/ClearSelection/ApplyViewTypeSelectionFromPopover,
        /// which all set _isBulkAssigningSelection around their loop so this
        /// handler knows to skip marking WasManuallySetByUser in those cases).
        /// </summary>
        private void OnViewInfoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewInfo.IsSelected)) return;
            if (_isBulkAssigningSelection) return;
            if (sender is not ViewInfo v) return;

            v.WasManuallySetByUser = true;
            RecomputeSelectedCount();
        }

        public void RecomputeSelectedCount()
        {
            SelectedViewCount = AllViews.Count(v => v.IsSelected);
        }

        /// <summary>Bound to the "All" link inside the View Type header popover.</summary>
        [RelayCommand]
        private void CheckAllViewTypes()
        {
            foreach (var f in ViewTypeFilters)
                f.IsChecked = true;
        }

        /// <summary>Bound to the "None" link inside the View Type header popover.</summary>
        [RelayCommand]
        private void UncheckAllViewTypes()
        {
            foreach (var f in ViewTypeFilters)
                f.IsChecked = false;
        }

        partial void OnStage1CompleteChanged(bool value)
        {
            OnPropertyChanged(nameof(Stage1StatusLabel));
            OnPropertyChanged(nameof(Stage2StatusLabel));
        }

        partial void OnViewNameFilterChanged(string value)
        {
            ViewsView.Refresh();
            OnPropertyChanged(nameof(IsViewNameFilterActive));
        }

        private bool FilterViews(object obj)
        {
            if (obj is not ViewInfo v) return false;

            if (!string.IsNullOrWhiteSpace(ViewNameFilter) &&
                v.Name.IndexOf(ViewNameFilter, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            var typeFilter = ViewTypeFilters.FirstOrDefault(f => f.RevitViewType == v.RevitViewType);
            if (typeFilter != null && !typeFilter.IsChecked)
                return false;

            if (PlacementFilter == PlacementFilterMode.Placed && !v.IsAlreadyPlaced)
                return false;
            if (PlacementFilter == PlacementFilterMode.NotPlaced && v.IsAlreadyPlaced)
                return false;

            if (!string.IsNullOrEmpty(SelectedFilterParameter) && !string.IsNullOrEmpty(SelectedFilterValue) &&
                !string.Equals(GetFilterParameterValue(v), SelectedFilterValue, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        [RelayCommand(CanExecute = nameof(CanGoToStage2))]
        private void NextToStage2()
        {
            RecomputeSelectedCount();
            if (SelectedViewCount == 0)
            {
                Logs.Add(new LogEntry(LogLevel.Warning, "No views selected — cannot proceed to Suggested Placement."));
                return;
            }
            if (SelectedTitleblock == null)
            {
                Logs.Add(new LogEntry(LogLevel.Warning, "No titleblock selected — cannot proceed."));
                return;
            }
            if (!AllViews.Any(v => v.IsSelected && v.CanBePlaced))
            {
                Logs.Add(new LogEntry(LogLevel.Warning,
                    "Every selected view is already on a sheet, and Revit allows a view on one sheet only (legends excepted) — nothing to place."));
                return;
            }

            SelectedTitleblock.ApplyMargins(MarginTopMm, MarginBottomMm, MarginLeftMm, MarginRightMm);
            SaveSettings();

            RunPacking();

            Stage1Complete = true;
            Stage1Expanded = false;
            Stage2Expanded = true;
        }

        private bool CanGoToStage2() => !IsBusy;

        /// <summary>
        /// Fires whenever a View Type filter checkbox is toggled in the column-header
        /// popover. Re-applies the ViewsView filter predicate (grid visibility) AND
        /// bulk-selects/deselects every view of that type (V213 fix — previously this
        /// only affected grid visibility, never IsSelected, so checking "Floor Plan
        /// only" in the popover hid other rows but never actually selected the Floor
        /// Plan rows, leaving SelectedViewCount at 0). Rows the user has manually
        /// touched (WasManuallySetByUser) are skipped — confirmed with Rafi: manual
        /// per-row edits are sticky and survive future popover toggles.
        /// </summary>
        private void OnViewTypeFilterOptionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ViewTypeFilterOption.IsChecked)) return;
            if (sender is ViewTypeFilterOption option)
                ApplyViewTypeSelectionFromPopover(option);

            ViewsView.Refresh();
            OnPropertyChanged(nameof(IsViewTypeFilterActive));
        }

        /// <summary>
        /// V213: bulk-sets IsSelected on every AllViews entry matching the given
        /// popover option's RevitViewType, to option.IsChecked — except rows the
        /// user has manually touched by hand (WasManuallySetByUser), which are
        /// left exactly as the user last set them. Guarded by
        /// _isBulkAssigningSelection so this doesn't itself get flagged as a
        /// manual edit via OnViewInfoPropertyChanged.
        /// </summary>
        private void ApplyViewTypeSelectionFromPopover(ViewTypeFilterOption option)
        {
            _isBulkAssigningSelection = true;
            foreach (var v in AllViews.Where(v => v.RevitViewType == option.RevitViewType && !v.WasManuallySetByUser))
                v.IsSelected = option.IsChecked;
            _isBulkAssigningSelection = false;
            RecomputeSelectedCount();
        }

        /// <summary>
        /// Populates AllViews/ViewTypeFilters/Titleblocks from the handler's
        /// LoadViews output. Restores last-used titleblock by name and
        /// preserves View Type filter checked-state across a Refresh.
        /// </summary>
        private void HandleLoadViewsCompleted()
        {
            // Reset all selections/filters on every load (initial page load and
            // Refresh alike) — confirmed with Rafi: filters never carry over from
            // a prior load, the grid always starts unfiltered.
            ViewNameFilter = string.Empty;
            PlacementFilter = PlacementFilterMode.All;
            IsFilterParameterOpen = false;
            SelectedFilterParameter = null;
            SelectedFilterValue = null;
            FilterValueOptions.Clear();

            // Unsubscribe from any views left over from a prior load (e.g. Refresh) —
            // the old ViewInfo instances are being discarded, so this isn't a leak,
            // but matches the same subscribe/unsubscribe hygiene used elsewhere.
            foreach (var existing in AllViews)
                existing.PropertyChanged -= OnViewInfoPropertyChanged;

            AllViews.Clear();
            foreach (var v in _handler.LoadedViews)
            {
                v.TitleSpaceMm = TitleSpaceMm;
                v.PropertyChanged += OnViewInfoPropertyChanged;
                AllViews.Add(v);
            }
            TotalViewCount = AllViews.Count;

            // Rebuild the Parameter filter's options from scratch every load —
            // populated-only (confirmed with Rafi): a parameter only shows up
            // if at least one loaded view has a non-blank value for it, so the
            // picker doesn't fill up with shared/project parameters nobody
            // actually used on any view. Scope is ALL loaded views, not just
            // currently-visible/selected ones.
            FilterParameterOptions.Clear();
            foreach (var name in AllViews
                         .SelectMany(v => v.ParameterValues)
                         .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                         .Select(kv => kv.Key)
                         .Distinct()
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                FilterParameterOptions.Add(name);
            FilterParameterSearchText = string.Empty;
            RefreshVisibleFilterParameterOptions();

            // Preserve each type's checked state across a Refresh (match by RevitViewType)
            // so re-loading views doesn't silently clear a filter the user had set.
            var previousCheckedState = ViewTypeFilters.ToDictionary(f => f.RevitViewType, f => f.IsChecked);

            // Unsubscribe from any options left over from a prior load (e.g. Refresh),
            // mirroring the AllPlacements subscribe/unsubscribe pattern used in Stage 2.
            foreach (var existing in ViewTypeFilters)
                existing.PropertyChanged -= OnViewTypeFilterOptionPropertyChanged;

            ViewTypeFilters.Clear();
            foreach (var vt in AllViews.Select(v => v.RevitViewType).Distinct())
            {
                var option = new ViewTypeFilterOption(vt, ViewTypeLabelHelper.Label(vt));
                if (previousCheckedState.TryGetValue(vt, out var wasChecked))
                {
                    // Type existed in a prior in-session load (e.g. a Refresh) —
                    // restore whatever the user last set, per existing behavior.
                    option.IsChecked = wasChecked;
                }
                else
                {
                    // V213 UPDATE: Rafi confirmed removal of the earlier
                    // "default Floor Plan checked" behavior — every ViewType
                    // now starts UNCHECKED on first-ever appearance (no prior
                    // state to restore). User must explicitly check what they
                    // want. ApplyViewTypeSelectionFromPopover() below still
                    // runs for every option (unconditionally unchecked here
                    // is itself a real value the popover-sync logic needs to
                    // see), so IsSelected on the grid rows stays correctly
                    // in sync with "unchecked" too — not just with "checked".
                    option.IsChecked = false;
                }

                // V213 FIX: IsChecked is set above BEFORE we subscribe to
                // PropertyChanged two lines down — so OnViewTypeFilterOptionPropertyChanged
                // never fires for this initial value, and the grid's IsSelected
                // checkboxes were never actually being set to match (the original
                // bug: Floor Plan defaulted checked in the popover, but no row was
                // ever selected). Explicitly apply selection here for every option,
                // covering both the restored-from-prior-session and first-ever-default
                // cases identically.
                ApplyViewTypeSelectionFromPopover(option);

                option.PropertyChanged += OnViewTypeFilterOptionPropertyChanged;
                ViewTypeFilters.Add(option);
            }
            OnPropertyChanged(nameof(IsViewTypeFilterActive));

            Titleblocks.Clear();
            foreach (var tb in _handler.LoadedTitleblocks)
                Titleblocks.Add(tb);

            // Restore last-used titleblock by name, falling back to first available.
            SelectedTitleblock = Titleblocks.FirstOrDefault(t => t.Name == _settings.LastTitleblockName)
                                 ?? Titleblocks.FirstOrDefault();

            RecomputeSelectedCount();
        }
    }
}
