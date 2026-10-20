using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;

namespace Revit26_Plugin_Revised.TrackerId.V001.UI.ViewModels
{
    // Step 1: the categories grid, its search box, discipline chips and column filters.
    public partial class TrackerIdViewModel
    {
        /// <summary>Every trackable category.</summary>
        public ObservableCollection<CategoryRowViewModel> Categories { get; } = new();

        /// <summary>Filtered, sortable view of <see cref="Categories"/> bound to the grid.</summary>
        public ICollectionView CategoriesView { get; private set; }

        /// <summary>The category being tracked.</summary>
        [ObservableProperty] private CategoryRowViewModel _selectedCategory;

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
        }

        private void ShowCategories(IEnumerable<CategoryInfo> categories)
        {
            long? previous = SelectedCategory?.Info.Id;
            Categories.Clear();
            foreach (var c in categories) Categories.Add(new CategoryRowViewModel(c));
            SelectedCategory = previous.HasValue ? Categories.FirstOrDefault(c => c.Info.Id == previous.Value) : null;
            RefreshCategoryView();
        }

        /// <summary>Picks a discipline chip.</summary>
        [RelayCommand]
        private void SetDiscipline(string discipline) => DisciplineFilter = discipline ?? "All";

        partial void OnCategorySearchChanged(string value) => RefreshCategoryView();
        partial void OnDisciplineFilterChanged(string value) => RefreshCategoryView();
        partial void OnHidePlacedNoneChanged(bool value) => RefreshCategoryView();
        partial void OnCategoryNameFilterChanged(string value) => RefreshCategoryView();
        partial void OnMinTypesFilterChanged(string value) => RefreshCategoryView();
        partial void OnMinElementsFilterChanged(string value) => RefreshCategoryView();
        partial void OnCoverageFilterChanged(string value) => RefreshCategoryView();

        partial void OnSelectedCategoryChanged(CategoryRowViewModel value)
        {
            if (_suspendTypeLoad) return;
            LoadTypes(value);
            RefreshCommands();
        }

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

        // The "placed only" switch also applies to the chip counts.
        private bool PassesCategoryBase(CategoryRowViewModel c) => !HidePlacedNone || c.ElementCount > 0;

        private string ChipText(string discipline)
        {
            int n = Categories.Count(c => PassesCategoryBase(c) && (discipline == "All" || c.Discipline == discipline));
            return $"{discipline} ({n})";
        }
    }
}
