using Autodesk.Revit.DB;

namespace Revit26_Plugin.BulkRename.V001.Core.Models
{
    /// <summary>
    /// One renameable project item (a line style, line pattern, arrowhead or fill pattern) as listed from
    /// the model. Read-only: the name the user types, the tick and the row status live on the grid row,
    /// not here, so Core holds no UI state.
    /// </summary>
    public sealed class RenameItem
    {
        /// <summary>
        /// Id of the element whose <c>Name</c> is changed. For a line style this is its projection
        /// graphics style, because a line style's category has no settable name.
        /// </summary>
        public ElementId Id { get; }

        /// <summary>Name the item has in the model.</summary>
        public string CurrentName { get; }

        /// <summary>Extra info shown in the grid: "Drafting" / "Model" for fill patterns, the arrow style for arrowheads, "Built-in" / "Custom" for line styles.</summary>
        public string Details { get; }

        /// <summary>How the item is used, for example "12 lines"; empty when not known.</summary>
        public string UsedBy { get; }

        /// <summary>Why the item can never be renamed (built-in, owned by another user); empty when it can.</summary>
        public string LockReason { get; }

        /// <summary>True when <see cref="LockReason"/> is set.</summary>
        public bool IsLocked => !string.IsNullOrEmpty(LockReason);

        /// <summary>Creates an item. Null texts become empty strings.</summary>
        public RenameItem(ElementId id, string currentName, string details, string usedBy, string lockReason)
        {
            Id = id;
            CurrentName = currentName ?? string.Empty;
            Details = details ?? string.Empty;
            UsedBy = usedBy ?? string.Empty;
            LockReason = lockReason ?? string.Empty;
        }
    }
}
