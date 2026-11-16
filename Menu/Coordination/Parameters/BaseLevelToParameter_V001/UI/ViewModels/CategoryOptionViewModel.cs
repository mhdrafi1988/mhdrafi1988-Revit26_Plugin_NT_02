using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.UI.ViewModels
{
    /// <summary>
    /// One row of the "Only the selected categories" grid in the New Parameter dialog (1.4.0): a model category
    /// that allows bound parameters, how many ticked elements it has, and whether the new parameter is bound to it.
    /// </summary>
    public sealed partial class CategoryOptionViewModel : ObservableObject
    {
        /// <summary>Creates a row.</summary>
        /// <param name="name">Category name.</param>
        /// <param name="tickedCount">Ticked elements of this category in the main window.</param>
        /// <param name="isSelected">Starts ticked.</param>
        public CategoryOptionViewModel(string name, int tickedCount, bool isSelected)
        {
            Name = name ?? string.Empty;
            TickedCount = Math.Max(0, tickedCount);
            _isSelected = isSelected;
        }

        /// <summary>Raised when <see cref="IsSelected"/> changes.</summary>
        public event EventHandler SelectionChanged;

        /// <summary>Category name.</summary>
        public string Name { get; }

        /// <summary>Ticked elements of this category in the main window.</summary>
        public int TickedCount { get; }

        /// <summary>True when the category has ticked elements.</summary>
        public bool HasTicked => TickedCount > 0;

        /// <summary>"12 ticked" or an empty string.</summary>
        public string TickedText => TickedCount > 0 ? $"{TickedCount} ticked" : "—";

        /// <summary>True when the new parameter is bound to this category.</summary>
        [ObservableProperty] private bool _isSelected;

        partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
