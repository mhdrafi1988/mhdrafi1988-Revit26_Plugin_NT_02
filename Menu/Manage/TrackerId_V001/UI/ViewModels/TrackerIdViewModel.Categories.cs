using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Infrastructure.Revit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.TrackerId.V001.UI.ViewModels
{
    // Step 1: the scope (whole model / selection / active view) and the categories grid, where several
    // categories can be ticked, with search, discipline chips and column filters.
    public partial class TrackerIdViewModel
    {
        private ReadScope _scope = ReadScope.WholeModel;
        private string _scopeViewName = string.Empty;
        private HashSet<long> _restoreCategoryIds;
        private DispatcherTimer _typesTimer;

        /// <summary>Every trackable category within the scope.</summary>
        public ObservableCollection<CategoryRowViewModel> Categories { get; } = new();

        /// <summary>Filtered, sortable view of <see cref="Categories"/> bound to the grid.</summary>
        public ICollectionView CategoriesView { get; private set; }

        /// <summary>Whole model (default), current selection or active view.</summary>
        [ObservableProperty] private ScopeMode _scopeMode = ScopeMode.WholeModel;

        /// <summary>Matches the category or discipline name.</summary>
        [ObservableProperty] private string _categorySearch = string.Empty;

        /// <summary>"All", "Architecture", "Structure" or "MEP".</summary>
        [ObservableProperty] private string _disciplineFilter = "All";

        /// <summary>Hides categories with nothing placed.</summary>
        [ObservableProperty] private bool _hidePlacedNone = true;

        /// <summary>Column filter: category name contains.</summary>
        [ObservableProperty] private string _categoryNameFilter = string.Empty;

        /// <summary>Column filter: at least this many types (blank = any).</summary>
        [ObservableProperty] private string _minTypesFilter = string.Empty;

        /// <summary>Column filter: at least this many elements (blank = any).</summary>
        [ObservableProperty] private string _minElementsFilter = string.Empty;

        /// <summary>Column filter: coverage ("Any", "None", "Partial", "Complete").</summary>
        [ObservableProperty] private string _coverageFilter = "Any";

        /// <summary>Choices of the coverage column filter.</summary>
        public IReadOnlyList<string> CoverageFilters { get; } = new[] { "Any", "None", "Partial", "Complete" };

        /// <summary>Ticked categories that have placed elements.</summary>
        public IReadOnlyList<CategoryRowViewModel> TickedCategories =>
            Categories.Where(c => c.IsSelected && c.ElementCount > 0).ToList();

        /// <summary>Elements of the ticked categories.</summary>
        public int TickedElementCount => TickedCategories.Sum(c => c.ElementCount);

        /// <summary>"Doors, Windows · 328 elements".</summary>
        public string TickedCategoriesText
        {
            get
            {
                var ticked = TickedCategories;
                if (ticked.Count == 0) return "Tick one or more categories.";
                string names = string.Join(", ", ticked.Take(4).Select(c => c.Name)) + (ticked.Count > 4 ? $" +{ticked.Count - 4} more" : string.Empty);
                return $"{names} · {TickedElementCount} elements";
            }
        }

        /// <summary>What the scope covers, e.g. "12 selected element(s)".</summary>
        public string ScopeText => _scope.Describe(_scopeViewName);

        /// <summary>Chip label "All (23)".</summary>
        public string AllChipText => ChipText("All");

        /// <summary>Chip label "Architecture (12)".</summary>
        public string ArchitectureChipText => ChipText("Architecture");

        /// <summary>Chip label "Structure (3)".</summary>
        public string StructureChipText => ChipText("Structure");

        /// <summary>Chip label "MEP (8)".</summary>
        public string MepChipText => ChipText("MEP");

        /// <summary>"18 of 25 categories shown".</summary>
        public string CategoriesShownText => CategoriesView == null
            ? string.Empty
            : $"{CategoriesView.Cast<object>().Count()} of {Categories.Count} categories shown";

        private void InitCategories()
        {
            CategoriesView = CollectionViewSource.GetDefaultView(Categories);
            CategoriesView.Filter = PassesCategoryFilters;

            // Ticking several categories in a row reads their types once, after the last tick.
            _typesTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) =>
            {
                _typesTimer.Stop();
                LoadTypes(TickedCategories);
            }, _dispatcher);
            _typesTimer.Stop();
        }

        private void ShowCategories(IEnumerable<CategoryInfo> categories)
        {
            var keep = new HashSet<long>(Categories.Where(c => c.IsSelected).Select(c => c.Info.Id));
            if (_restoreCategoryIds != null) keep.UnionWith(_restoreCategoryIds);
            _restoreCategoryIds = null;

            Categories.Clear();
            foreach (var c in categories)
            {
                var row = new CategoryRowViewModel(c, OnCategoryTicked);
                if (keep.Contains(c.Id) && c.ElementCount > 0) row.SetSelectedQuietly(true);
                Categories.Add(row);
            }
            RefreshCategoryView();
            OnCategoryTicked();
        }

        private void OnCategoryTicked()
        {
            OnPropertyChanged(nameof(TickedCategories));
            OnPropertyChanged(nameof(TickedElementCount));
            OnPropertyChanged(nameof(TickedCategoriesText));
            InvalidateSnapshots();
            if (_suspendTypeLoad) return;

            _typesTimer?.Stop();
            _typesTimer?.Start();
            RefreshCommands();
        }

        /// <summary>Ticks every category the filters show (with elements).</summary>
        [RelayCommand]
        private void SelectAllCategories() => SetCategoryTicks(true);

        /// <summary>Unticks every category the filters show.</summary>
        [RelayCommand]
        private void SelectNoCategories() => SetCategoryTicks(false);

        private void SetCategoryTicks(bool value)
        {
            var visible = CategoriesView?.Cast<CategoryRowViewModel>().Where(c => !value || c.ElementCount > 0).ToList()
                          ?? new List<CategoryRowViewModel>();
            foreach (var c in visible) c.SetSelectedQuietly(value);
            OnCategoryTicked();
        }

        /// <summary>Picks a discipline chip.</summary>
        [RelayCommand]
        private void SetDiscipline(string discipline) => DisciplineFilter = discipline ?? "All";

        // ── Scope ─────────────────────────────────────────────────────────────

        /// <summary>Picks a scope chip: "WholeModel", "Selection" or "ActiveView".</summary>
        [RelayCommand]
        private void SetScope(string mode)
        {
            if (!Enum.TryParse(mode, out ScopeMode parsed)) return;
            ScopeMode = parsed;
            ReloadCategories(captureScope: true);
        }

        /// <summary>Reads Revit's current selection again (Selection scope) and lists its categories.</summary>
        [RelayCommand]
        private void UseCurrentSelection()
        {
            ScopeMode = ScopeMode.Selection;
            ReloadCategories(captureScope: true);
        }

        partial void OnScopeModeChanged(ScopeMode value) => OnPropertyChanged(nameof(ScopeText));

        // Reads the categories within the scope again. With captureScope, the selection or active view is
        // taken from Revit now; otherwise the one captured before is kept.
        private void ReloadCategories(bool captureScope, Action afterShow = null)
        {
            var mode = ScopeMode;
            var previous = _scope;
            string previousView = _scopeViewName;

            Run("Reading categories…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;

                var scope = previous;
                string viewName = previousView;
                if (captureScope || scope.Mode != mode)
                {
                    scope = CaptureScope(app.ActiveUIDocument, mode, out viewName);
                }

                var categories = ModelReader.ReadCategories(doc, scope);
                OnUi(() =>
                {
                    _scope = scope;
                    _scopeViewName = viewName;
                    OnPropertyChanged(nameof(ScopeText));
                    ClearTypeCache();
                    InvalidateSnapshots();
                    ShowCategories(categories);
                    AddLog(LogLevel.Info, $"Scope: {ScopeText}. {Categories.Count(c => c.ElementCount > 0)} categories with elements.");
                    if (scope.Mode == ScopeMode.Selection && scope.ElementIds.Count == 0)
                        AddLog(LogLevel.Warning, "Nothing is selected in Revit. Select elements, then click 'Use current selection'.");
                    afterShow?.Invoke();
                });
            });
        }

        // Runs in a Revit API context (or while the command runs).
        private static ReadScope CaptureScope(Autodesk.Revit.UI.UIDocument uidoc, ScopeMode mode, out string viewName)
        {
            viewName = string.Empty;
            switch (mode)
            {
                case ScopeMode.Selection:
                    return new ReadScope { Mode = ScopeMode.Selection, ElementIds = uidoc == null ? new HashSet<long>() : SelectionService.Current(uidoc) };
                case ScopeMode.ActiveView:
                    var view = uidoc?.ActiveView;
                    if (view == null) return ReadScope.WholeModel;
                    viewName = view.Name;
                    return new ReadScope { Mode = ScopeMode.ActiveView, ViewId = view.Id.Value };
                default:
                    return ReadScope.WholeModel;
            }
        }

        // ── Filters ───────────────────────────────────────────────────────────

        partial void OnCategorySearchChanged(string value) => RefreshCategoryView();
        partial void OnDisciplineFilterChanged(string value) => RefreshCategoryView();
        partial void OnHidePlacedNoneChanged(bool value) => RefreshCategoryView();
        partial void OnCategoryNameFilterChanged(string value) => RefreshCategoryView();
        partial void OnMinTypesFilterChanged(string value) => RefreshCategoryView();
        partial void OnMinElementsFilterChanged(string value) => RefreshCategoryView();
        partial void OnCoverageFilterChanged(string value) => RefreshCategoryView();

        private void RefreshCategoryView()
        {
            CategoriesView?.Refresh();
            OnPropertyChanged(nameof(CategoriesShownText));
            OnPropertyChanged(nameof(AllChipText));
            OnPropertyChanged(nameof(ArchitectureChipText));
            OnPropertyChanged(nameof(StructureChipText));
            OnPropertyChanged(nameof(MepChipText));
        }

        private bool PassesCategoryFilters(object item)
        {
            if (item is not CategoryRowViewModel c) return false;
            if (!PassesCategoryBase(c)) return false;
            if (DisciplineFilter != "All" && c.Discipline != DisciplineFilter) return false;

            string search = CategorySearch?.Trim() ?? string.Empty;
            if (search.Length > 0 &&
                !c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !c.Discipline.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;

            string name = CategoryNameFilter?.Trim() ?? string.Empty;
            if (name.Length > 0 && !c.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) return false;

            if (int.TryParse(MinTypesFilter, out int minTypes) && c.TypeCount < minTypes) return false;
            if (int.TryParse(MinElementsFilter, out int minElements) && c.ElementCount < minElements) return false;
            if (CoverageFilter != "Any" && c.Coverage.ToString() != CoverageFilter) return false;
            return true;
        }

        // The "placed only" switch also applies to the chip counts. A ticked category always stays visible.
        private bool PassesCategoryBase(CategoryRowViewModel c) => !HidePlacedNone || c.ElementCount > 0 || c.IsSelected;

        private string ChipText(string discipline)
        {
            int n = Categories.Count(c => PassesCategoryBase(c) && (discipline == "All" || c.Discipline == discipline));
            return $"{discipline} ({n})";
        }
    }
}
