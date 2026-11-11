using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Application.Dtos;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.UI.ViewModels
{
    /// <summary>One row of a category list: checkbox, name, element count, model name and status.</summary>
    public sealed class CategoryItemViewModel : ObservableObject
    {
        private readonly Action _selectionChanged;
        private bool _isSelected;

        /// <summary>Creates a row.</summary>
        public CategoryItemViewModel(CategoryCountDto row, string modelName, bool isSelected, Action selectionChanged)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            Key = row.Key;
            Name = row.Name;
            Count = row.Count;
            Note = row.Note;
            ModelName = modelName;
            _selectionChanged = selectionChanged;
            _isSelected = isSelected && CanSelect;
        }

        /// <summary>Identifies the category (its enum name).</summary>
        public string Key { get; }

        /// <summary>Category name.</summary>
        public string Name { get; }

        /// <summary>Number of elements of the category in the chosen model.</summary>
        public int Count { get; }

        /// <summary>Name of the chosen model.</summary>
        public string ModelName { get; }

        /// <summary>Extra information about the category, such as "Marker only, no cut".</summary>
        public string Note { get; }

        /// <summary>False when the model has no element of the category, so there is nothing to select.</summary>
        public bool CanSelect => Count > 0;

        /// <summary>True when the user ticked the category.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (!SetProperty(ref _isSelected, value && CanSelect))
                    return;
                OnPropertyChanged(nameof(Status));
                _selectionChanged?.Invoke();
            }
        }

        /// <summary>Selection status shown in the last column.</summary>
        public string Status
        {
            get
            {
                if (!CanSelect) return "Empty";
                if (!IsSelected) return "Not selected";
                return string.IsNullOrEmpty(Note) ? "Selected" : "Selected · " + Note;
            }
        }
    }
}
