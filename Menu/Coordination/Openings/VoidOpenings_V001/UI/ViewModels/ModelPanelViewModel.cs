using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>
    /// One of the two selection panels (STRUCTURAL MODEL or MEP MODEL): the model list, the chosen
    /// model with its status, and that model's categories with element counts.
    /// </summary>
    public sealed class ModelPanelViewModel : ObservableObject
    {
        private readonly Func<ModelRef, Task<IReadOnlyList<CategoryCountDto>>> _loadCounts;
        private readonly Action<string> _reportError;
        private ModelRef _selectedModel;
        private bool _isBusy;
        private int _loadVersion;
        private bool _updatingModels;

        /// <summary>Creates a panel.</summary>
        /// <param name="title">Panel title, for example "STRUCTURAL MODEL".</param>
        /// <param name="modelLabel">Label above the model list, for example "Structural link".</param>
        /// <param name="loadCounts">Reads the category counts of a model.</param>
        /// <param name="reportError">Shows a message in the window's notification strip.</param>
        public ModelPanelViewModel(string title, string modelLabel,
            Func<ModelRef, Task<IReadOnlyList<CategoryCountDto>>> loadCounts, Action<string> reportError)
        {
            Title = title;
            ModelLabel = modelLabel;
            _loadCounts = loadCounts ?? throw new ArgumentNullException(nameof(loadCounts));
            _reportError = reportError;
        }

        /// <summary>Panel title.</summary>
        public string Title { get; }

        /// <summary>Label above the model list.</summary>
        public string ModelLabel { get; }

        /// <summary>The active model and the Revit links.</summary>
        public ObservableCollection<ModelRef> Models { get; } = new ObservableCollection<ModelRef>();

        /// <summary>Categories of the chosen model.</summary>
        public ObservableCollection<CategoryItemViewModel> Categories { get; } = new ObservableCollection<CategoryItemViewModel>();

        /// <summary>The chosen model; choosing one reads its category counts.</summary>
        public ModelRef SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_updatingModels || ReferenceEquals(_selectedModel, value))
                    return;
                _selectedModel = value;
                RaiseModelChanged();
                _ = LoadCountsAsync();
            }
        }

        /// <summary>True when a model is chosen.</summary>
        public bool HasModel => _selectedModel != null;

        /// <summary>True when the chosen model's elements can be read.</summary>
        public bool IsModelUsable => _selectedModel != null && _selectedModel.IsLoaded;

        /// <summary>"Active model", "Loaded" or "Not loaded".</summary>
        public string ModelStatus
        {
            get
            {
                if (_selectedModel == null) return string.Empty;
                if (_selectedModel.IsHost) return "Active model";
                return _selectedModel.IsLoaded ? "Loaded" : "Not loaded";
            }
        }

        /// <summary>True while category counts are being read.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>One-line summary of the selection, shown in the panel header.</summary>
        public string Summary
        {
            get
            {
                if (_selectedModel == null) return "No model chosen";
                if (!_selectedModel.IsLoaded) return "Link not loaded";
                var selected = Categories.Where(c => c.IsSelected).ToList();
                return $"{selected.Count} of {Categories.Count} categories · {selected.Sum(c => c.Count):N0} elements selected";
            }
        }

        /// <summary>Keys of the ticked categories.</summary>
        public IReadOnlyList<string> SelectedCategoryKeys => Categories.Where(c => c.IsSelected).Select(c => c.Key).ToList();

        /// <summary>
        /// Replaces the model list, keeping the current choice when that model is still there, and
        /// reads its counts again.
        /// </summary>
        public void SetModels(IReadOnlyList<ModelRef> models)
        {
            ModelRef previous = _selectedModel;

            // Clearing the list makes the bound ComboBox push a null selection; ignore it until the
            // list is rebuilt, so the ticked categories are still there to be kept.
            _updatingModels = true;
            try
            {
                Models.Clear();
                foreach (ModelRef model in models)
                    Models.Add(model);
                _selectedModel = previous == null ? null : Models.FirstOrDefault(m => Same(m, previous));
            }
            finally
            {
                _updatingModels = false;
            }

            RaiseModelChanged();
            _ = LoadCountsAsync();
        }

        private void RaiseModelChanged()
        {
            OnPropertyChanged(nameof(SelectedModel));
            OnPropertyChanged(nameof(ModelStatus));
            OnPropertyChanged(nameof(IsModelUsable));
            OnPropertyChanged(nameof(HasModel));
        }

        private static bool Same(ModelRef a, ModelRef b)
            => a.IsHost == b.IsHost
            && string.Equals(a.ModelKey, b.ModelKey, StringComparison.Ordinal)
            && string.Equals(a.LinkInstanceUniqueId, b.LinkInstanceUniqueId, StringComparison.Ordinal);

        private async Task LoadCountsAsync()
        {
            int version = ++_loadVersion;
            ModelRef model = _selectedModel;
            var keepSelected = new HashSet<string>(SelectedCategoryKeys);

            if (model == null)
            {
                Categories.Clear();
                OnPropertyChanged(nameof(Summary));
                return;
            }

            IsBusy = true;
            try
            {
                IReadOnlyList<CategoryCountDto> rows = await _loadCounts(model);
                if (version != _loadVersion)
                    return; // the user chose another model while this one was being read

                Categories.Clear();
                foreach (CategoryCountDto row in rows)
                    Categories.Add(new CategoryItemViewModel(row, model.DisplayName, keepSelected.Contains(row.Key),
                        () => OnPropertyChanged(nameof(Summary))));
            }
            catch (Exception ex)
            {
                if (version != _loadVersion)
                    return;
                Categories.Clear();
                _reportError?.Invoke($"Could not read {model.DisplayName}: {ex.Message}");
            }
            finally
            {
                if (version == _loadVersion)
                {
                    IsBusy = false;
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }
    }
}
