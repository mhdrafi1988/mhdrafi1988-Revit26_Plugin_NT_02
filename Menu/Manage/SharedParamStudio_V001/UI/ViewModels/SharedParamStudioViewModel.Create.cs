using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Core;
using Revit26_Plugin_Revised.SharedParamStudio.V001.Infrastructure;

namespace Revit26_Plugin_Revised.SharedParamStudio.V001.UI.ViewModels
{
    public sealed partial class SharedParamStudioViewModel
    {
        // ── Tab 1: Create & Assign ────────────────────────────────────────────

        [ObservableProperty] private string _createFilePath = string.Empty;
        [ObservableProperty] private string _createFileGroup = string.Empty;
        [ObservableProperty] private string _paramName = string.Empty;
        [ObservableProperty] private string _paramDescription = string.Empty;
        [ObservableProperty] private bool _userModifiable = true;
        [ObservableProperty] private bool _showInProperties = true;
        [ObservableProperty] private bool _isInstance = true;
        [ObservableProperty] private DataTypeOption _selectedDataType;
        [ObservableProperty] private GroupOption _selectedPaletteGroup;
        [ObservableProperty] private string _categoryFilter = string.Empty;
        [ObservableProperty] private string _paramNameError = string.Empty;

        private readonly System.Collections.Generic.HashSet<string> _fileNames = new();
        private readonly System.Collections.Generic.HashSet<string> _projectNames = new();

        /// <summary>Data types to choose from.</summary>
        public System.Collections.Generic.IReadOnlyList<DataTypeOption> DataTypeOptions => DataTypeOption.All;

        /// <summary>Properties-palette groups to choose from.</summary>
        public System.Collections.Generic.IReadOnlyList<GroupOption> PaletteGroups => GroupOption.All;

        /// <summary>Group names already in the chosen file.</summary>
        public ObservableCollection<string> FileGroupNames { get; } = new();

        /// <summary>All bindable categories.</summary>
        public ObservableCollection<CategoryItem> Categories { get; } = new();

        /// <summary>Categories after the search box filter.</summary>
        public ICollectionView CategoriesView { get; }

        /// <summary>Count of ticked categories, for the tab.</summary>
        public int SelectedCategoryCount => Categories.Count(c => c.IsSelected);

        partial void OnCategoryFilterChanged(string value) => CategoriesView.Refresh();

        partial void OnCreateFilePathChanged(string value) => LoadCreateFile(value);

        partial void OnParamNameChanged(string value) => UpdateNameError();

        /// <summary>Reads the chosen file once for its group names and parameter names (live name check).</summary>
        private void LoadCreateFile(string path)
        {
            FileGroupNames.Clear();
            _fileNames.Clear();
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    var file = SpFileReader.Read(path.Trim());
                    foreach (var g in file.Groups) FileGroupNames.Add(g);
                    foreach (var p in file.Params) _fileNames.Add(SpKeys.NameKey(p.Name));
                }
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Warning, $"Could not read {path}: {ex.Message}");
            }
            UpdateNameError();
        }

        private void UpdateNameError()
        {
            if (string.IsNullOrEmpty(ParamName)) { ParamNameError = string.Empty; return; }
            string key = SpKeys.NameKey(ParamName);
            ParamNameError = SpKeys.ValidateName(ParamName)
                ?? (_fileNames.Contains(key) ? "A parameter with this name is already in the file."
                : _projectNames.Contains(key) ? "The project already has a parameter with this name."
                : string.Empty);
        }

        [RelayCommand]
        private void BrowseCreateFile()
        {
            string p = PickSave(CreateFilePath, "Shared parameter file to add the parameter to (type a new name to create one)");
            if (p != null) CreateFilePath = p;
        }

        [RelayCommand]
        private void SelectVisibleCategories()
        {
            foreach (CategoryItem c in CategoriesView) c.IsSelected = true;
            OnPropertyChanged(nameof(SelectedCategoryCount));
        }

        [RelayCommand]
        private void ClearCategories()
        {
            foreach (var c in Categories) c.IsSelected = false;
            OnPropertyChanged(nameof(SelectedCategoryCount));
        }

        /// <summary>Called by the view when a category tick box changes.</summary>
        public void NotifyCategoryTicked() => OnPropertyChanged(nameof(SelectedCategoryCount));

        [RelayCommand]
        private void CreateAndAssign()
        {
            if (string.IsNullOrWhiteSpace(ParamName)) { Log(LogLevel.Error, "Enter a parameter name."); return; }
            if (!string.IsNullOrEmpty(ParamNameError)) { Log(LogLevel.Error, ParamNameError); return; }
            if (string.IsNullOrWhiteSpace(CreateFilePath)) { Log(LogLevel.Error, "Choose the shared parameter file first."); return; }
            if (string.IsNullOrWhiteSpace(CreateFileGroup)) { Log(LogLevel.Error, "Enter or pick a group name for the file."); return; }
            var ids = Categories.Where(c => c.IsSelected).Select(c => c.Id).ToList();
            if (ids.Count == 0) { Log(LogLevel.Error, "Tick at least one category."); return; }

            var request = new CreateParameterRequest
            {
                FilePath = CreateFilePath.Trim(),
                FileGroup = CreateFileGroup.Trim(),
                Name = ParamName,
                DataType = SelectedDataType,
                Description = ParamDescription,
                UserModifiable = UserModifiable,
                Visible = ShowInProperties,
                IsInstance = IsInstance,
                PaletteGroup = SelectedPaletteGroup,
                CategoryIds = ids
            };

            JobResult result = null;
            Log(LogLevel.Info, $"Creating '{request.Name}' and binding it to {ids.Count} categor{(ids.Count == 1 ? "y" : "ies")}…");
            RunJob(doc => result = SharedParamStudioService.CreateAndAssign(doc, request), () =>
            {
                if (result == null) return;
                LogAll(result.Messages);
                if (result.Success)
                {
                    _projectNames.Add(SpKeys.NameKey(request.Name));
                    ParamName = string.Empty;
                }
                LoadCreateFile(CreateFilePath);
            });
        }
    }
}
