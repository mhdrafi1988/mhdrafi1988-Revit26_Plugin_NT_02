using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.AnnotationOverlapReport.V001.UI
{
    /// <summary>One view or sheet in the Scope list.</summary>
    public sealed partial class ViewRow : ObservableObject
    {
        /// <summary>Element id value.</summary>
        public long Id { get; init; }

        /// <summary>Text shown.</summary>
        public string Name { get; init; }

        /// <summary>"Sheets" or "Views"; the list is grouped by it.</summary>
        public string Group { get; init; }

        /// <summary>True when the view or sheet is ticked for the check.</summary>
        [ObservableProperty]
        private bool _isSelected;
    }

    /// <summary>One annotation type in the type list.</summary>
    public sealed partial class TypeRow : ObservableObject
    {
        /// <summary>Revit category name.</summary>
        public string Name { get; init; }

        /// <summary>Number of annotations of this type in the scope.</summary>
        public int Count { get; init; }

        /// <summary>
        /// True for head types (grids, levels, section / elevation / callout marks, view titles):
        /// listed but not checked in 1.0.
        /// </summary>
        public bool IsDeferred { get; init; }

        /// <summary>True when the row can be ticked.</summary>
        public bool CanCheck => !IsDeferred;

        /// <summary>Count, or "coming in 1.1" for a head type.</summary>
        public string CountText => IsDeferred ? Count + " · coming in 1.1" : Count.ToString();

        /// <summary>True when the type takes part in the check.</summary>
        [ObservableProperty]
        private bool _isSelected;
    }
}
