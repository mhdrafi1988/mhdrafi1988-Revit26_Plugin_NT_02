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

namespace Revit26_Plugin_Revised.TrackerId.V001.UI.ViewModels
{
    // Step 2: the types of the chosen category, with search, column filters and status chips.
    public partial class TrackerIdViewModel
    {
        private readonly Dictionary<long, List<TypeInfo>> _typeCache = new();

        /// <summary>Types of the chosen category.</summary>
        public ObservableCollection<TypeRowViewModel> Types { get; } = new();

        /// <summary>Filtered, sortable view of <see cref="Types"/> bound to the grid.</summary>
        public ICollectionView TypesView { get; private set; }

        /// <summary>Matches family, type or type mark.</summary>
        [ObservableProperty] private string _typeSearch = string.Empty;

        /// <summary>Column filter: family contains.</summary>
        [ObservableProperty] private string _familyFilter = string.Empty;

        /// <summary>Column filter: type name contains.</summary>
        [ObservableProperty] private string _typeNameFilter = string.Empty;

        /// <summary>Column filter: type mark contains.</summary>
        [ObservableProperty] private string _typeMarkFilter = string.Empty;

        /// <summary>"All", "None", "Partial" or "Complete".</summary>
        [ObservableProperty] private string _typeStatusFilter = "All";

        /// <summary>Number of ticked types.</summary>
        public int SelectedTypeCount => Types.Count(t => t.IsSelected);

        /// <summary>Elements of the ticked types.</summary>
        public int SelectedElementCount => Types.Where(t => t.IsSelected).Sum(t => t.ElementCount);

        /// <summary>"Doors · 14 types".</summary>
        public string TypesTitle => SelectedCategory == null ? "Types" : $"{SelectedCategory.Name} · {Types.Count} types";

        /// <summary>"3 selected · 46 elements".</summary>
        public string TypesSelectedText => $"{SelectedTypeCount} selected · {SelectedElementCount} elements";

        /// <summary>"12 of 14 types shown".</summary>
        public string TypesShownText => TypesView == null ? string.Empty : $"{TypesView.Cast<object>().Count()} of {Types.Count} types shown";

        /// <summary>Chip label "All (14)".</summary>
        public string TypeAllChipText => $"All ({Types.Count})";

        /// <summary>Chip label "No IDs (5)".</summary>
        public string TypeNoneChipText => $"No IDs ({Types.Count(t => t.Coverage == CoverageState.None)})";

        /// <summary>Chip label "Partial (4)".</summary>
        public string TypePartialChipText => $"Partial ({Types.Count(t => t.Coverage == CoverageState.Partial)})";

        /// <summary>Chip label "Complete (5)".</summary>
        public string TypeCompleteChipText => $"Complete ({Types.Count(t => t.Coverage == CoverageState.Complete)})";

        private void InitTypes()
        {
            TypesView = CollectionViewSource.GetDefaultView(Types);
            TypesView.Filter = PassesTypeFilters;
        }

        private void LoadTypes(CategoryRowViewModel category)
        {
            ShowTypes(new List<TypeInfo>());
            InvalidateSnapshots();
            if (category == null || category.ElementCount == 0) return;

            if (_typeCache.TryGetValue(category.Info.Id, out var cached))
            {
                ShowTypes(cached);
                return;
            }

            long categoryId = category.Info.Id;
            Run($"Reading {category.Name} types…", app =>
            {
                var doc = DocumentFor(app);
                if (doc == null) return;
                var types = ModelReader.ReadTypes(doc, categoryId);
                OnUi(() =>
                {
                    _typeCache[categoryId] = types;
                    if (SelectedCategory?.Info.Id == categoryId) ShowTypes(types);
                    SummaryText = $"{category.Name}: {types.Count} types.";
                });
            });
        }

        private void ShowTypes(List<TypeInfo> types)
        {
            Types.Clear();
            foreach (var t in types) Types.Add(new TypeRowViewModel(t, OnTypeSelectionChanged));
            RefreshTypeView();
            OnTypeSelectionChanged();
        }

        private void ClearTypeCache() => _typeCache.Clear();

        private void OnTypeSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedTypeCount));
            OnPropertyChanged(nameof(SelectedElementCount));
            OnPropertyChanged(nameof(TypesSelectedText));
            InvalidateSnapshots();
            RefreshCommands();
        }

        /// <summary>Ticks every type the filters show.</summary>
        [RelayCommand]
        private void SelectAllTypes() => SetTypeTicks(true);

        /// <summary>Unticks every type the filters show.</summary>
        [RelayCommand]
        private void SelectNoTypes() => SetTypeTicks(false);

        private void SetTypeTicks(bool value)
        {
            var visible = TypesView?.Cast<TypeRowViewModel>().ToList() ?? new List<TypeRowViewModel>();
            foreach (var t in visible) t.IsSelected = value;
            AddLog(LogLevel.Info, $"{(value ? "Ticked" : "Unticked")} {visible.Count} type(s).");
        }

        /// <summary>Picks a status chip.</summary>
        [RelayCommand]
        private void SetTypeStatus(string status) => TypeStatusFilter = status ?? "All";

        partial void OnTypeSearchChanged(string value) => RefreshTypeView();
        partial void OnFamilyFilterChanged(string value) => RefreshTypeView();
        partial void OnTypeNameFilterChanged(string value) => RefreshTypeView();
        partial void OnTypeMarkFilterChanged(string value) => RefreshTypeView();
        partial void OnTypeStatusFilterChanged(string value) => RefreshTypeView();

        private void RefreshTypeView()
        {
            TypesView?.Refresh();
            OnPropertyChanged(nameof(TypesTitle));
            OnPropertyChanged(nameof(TypesShownText));
            OnPropertyChanged(nameof(TypeAllChipText));
            OnPropertyChanged(nameof(TypeNoneChipText));
            OnPropertyChanged(nameof(TypePartialChipText));
            OnPropertyChanged(nameof(TypeCompleteChipText));
        }

        private bool PassesTypeFilters(object item)
        {
            if (item is not TypeRowViewModel t) return false;

            string search = TypeSearch?.Trim() ?? string.Empty;
            if (search.Length > 0 &&
                !t.FamilyName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !t.TypeName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !t.TypeMark.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;

            if (!Matches(t.FamilyName, FamilyFilter) || !Matches(t.TypeName, TypeNameFilter) || !Matches(t.TypeMark, TypeMarkFilter))
                return false;

            return TypeStatusFilter == "All" || t.Coverage.ToString() == TypeStatusFilter;
        }

        private static bool Matches(string value, string filter) =>
            string.IsNullOrWhiteSpace(filter) || (value ?? string.Empty).Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
