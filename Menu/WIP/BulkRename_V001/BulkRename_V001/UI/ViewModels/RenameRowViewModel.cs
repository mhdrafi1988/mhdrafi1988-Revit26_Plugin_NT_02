using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin.BulkRename.V001.Core.Models;
using System;

namespace Revit26_Plugin.BulkRename.V001.UI.ViewModels
{
    /// <summary>
    /// One row of the Bulk Rename grid: a listed <see cref="RenameItem"/> plus what the user is doing with
    /// it (the new name, the tick and the validation verdict).
    /// </summary>
    public sealed partial class RenameRowViewModel : ObservableObject
    {
        /// <summary>The item as listed from the model.</summary>
        public RenameItem Item { get; }

        /// <summary>Name the item has in the model.</summary>
        public string CurrentName => Item.CurrentName;

        /// <summary>Extra info: "Drafting" / "Model", the arrow style, or "Built-in" / "Custom".</summary>
        public string Details => Item.Details;

        /// <summary>How the item is used, for example "12 lines"; an em dash when not known.</summary>
        public string UsedBy { get; }

        /// <summary>Why the item can never be renamed (built-in, owned by another user); empty when it can.</summary>
        public string LockReason => Item.LockReason;

        /// <summary>True when the item can never be renamed.</summary>
        public bool IsLocked => Item.IsLocked;

        /// <summary>Name typed or generated for the row; the item is renamed to it (trimmed) on Apply.</summary>
        [ObservableProperty] private string newName;

        /// <summary>Ticked: the rules act on this row and Apply renames it.</summary>
        [ObservableProperty] private bool isSelected;

        /// <summary>Row state shown in the Status column.</summary>
        [ObservableProperty] private RenameStatus status;

        /// <summary>Tooltip for the Status column: why the row is Invalid, Duplicate or Locked.</summary>
        [ObservableProperty] private string statusMessage = string.Empty;

        /// <summary>
        /// Name Apply will give the item, as worked out by the last validation: the trimmed new name for a
        /// Ready row, otherwise the current name.
        /// </summary>
        public string FinalName { get; internal set; }

        /// <summary>
        /// Creates a row for <paramref name="item"/>. The new name starts equal to the current name, and the
        /// row starts ticked unless the item is locked.
        /// </summary>
        public RenameRowViewModel(RenameItem item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            UsedBy = string.IsNullOrEmpty(item.UsedBy) ? "—" : item.UsedBy;
            FinalName = item.CurrentName;

            // Assign the backing fields directly: no change notifications while constructing.
            newName = item.CurrentName;
            isSelected = !item.IsLocked;
            status = item.IsLocked ? RenameStatus.Locked : RenameStatus.Unchanged;
            statusMessage = item.LockReason;
        }
    }
}
