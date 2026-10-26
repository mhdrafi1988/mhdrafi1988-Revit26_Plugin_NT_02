using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models
{
    /// <summary>One line style in the Rename styles preview grid.</summary>
    public sealed partial class RenameRow : ObservableObject
    {
        /// <summary>Ticked for Rename checked. Only rows that will be renamed can be ticked.</summary>
        [ObservableProperty] private bool isChecked = true;

        /// <summary>New name from the rules.</summary>
        [ObservableProperty] private string newName = string.Empty;

        /// <summary>Result of the checks.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText), nameof(CanRename), nameof(IsProblem))]
        private RenameStatus status;

        /// <summary>Why it is not renamed, or a note; empty when plain.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        private string reason = string.Empty;

        /// <summary>Line style subcategory id.</summary>
        public ElementId CategoryId { get; init; }

        /// <summary>Current name.</summary>
        public string CurrentName { get; init; } = string.Empty;

        /// <summary>True for a built-in style.</summary>
        public bool IsBuiltIn { get; init; }

        /// <summary>Lines using the style in the project; empty when not counted.</summary>
        public string CountText { get; init; } = string.Empty;

        /// <summary>True when the row will be renamed.</summary>
        public bool CanRename => Status == RenameStatus.WillRename;

        /// <summary>True for a duplicate or invalid name (shown in red).</summary>
        public bool IsProblem => Status is RenameStatus.Duplicate or RenameStatus.Invalid;

        /// <summary>Status column text.</summary>
        public string StatusText => Status switch
        {
            RenameStatus.WillRename => string.IsNullOrEmpty(Reason) ? "Will rename" : "Will rename: " + Reason,
            RenameStatus.Unchanged => "Unchanged",
            RenameStatus.Duplicate => "Duplicate: " + Reason,
            RenameStatus.Invalid => "Invalid: " + Reason,
            _ => "Built-in: not renamed"
        };
    }
}
