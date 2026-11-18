using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.BulkDelete.V001.Core.Models;
using System;

namespace Revit26_Plugin_Revised.BulkDelete.V001.UI.ViewModels
{
    /// <summary>
    /// One row of the Bulk Delete grid: a listed <see cref="DeleteItem"/> plus the tick the user sets.
    /// Rows start unticked, so nothing is deleted unless the user asks for it.
    /// </summary>
    public sealed partial class DeleteRowViewModel : ObservableObject
    {
        /// <summary>The item as listed from the model.</summary>
        public DeleteItem Item { get; }

        /// <summary>Name the item has in the model.</summary>
        public string Name => Item.Name;

        /// <summary>Extra info: "Drafting" / "Model", the arrow style, or "Built-in" / "Custom".</summary>
        public string Details => Item.Details;

        /// <summary>How the item is used, for example "12 lines"; an em dash when not known.</summary>
        public string UsedBy { get; }

        /// <summary>Why the item cannot be deleted (built-in, in use, owned by another user); empty when it can.</summary>
        public string LockReason => Item.LockReason;

        /// <summary>True when the item cannot be deleted from this window.</summary>
        public bool IsLocked => Item.IsLocked;

        /// <summary>Ticked: Delete removes this item.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Status))]
        [NotifyPropertyChangedFor(nameof(StatusMessage))]
        private bool isSelected;

        /// <summary>Row state shown in the Status column.</summary>
        public DeleteStatus Status => IsLocked ? DeleteStatus.Locked : IsSelected ? DeleteStatus.Delete : DeleteStatus.Keep;

        /// <summary>Tooltip for the Status column.</summary>
        public string StatusMessage => IsLocked ? LockReason : IsSelected ? "Will be deleted." : "Stays in the model.";

        /// <summary>Creates a row for <paramref name="item"/>, unticked.</summary>
        public DeleteRowViewModel(DeleteItem item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            UsedBy = string.IsNullOrEmpty(item.UsedBy) ? "—" : item.UsedBy;
        }
    }
}
