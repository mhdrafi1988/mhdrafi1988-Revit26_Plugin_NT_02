using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Models
{
    /// <summary>
    /// One deletable project item (a line style, line pattern, arrowhead or fill pattern) as listed from
    /// the model. Read-only: the tick and the row status live on the grid row, not here, so Core holds
    /// no UI state.
    /// </summary>
    public sealed class DeleteItem
    {
        /// <summary>
        /// Id of the listed element. For a line style this is its projection graphics style, because a
        /// line style is a category and the graphics style is what the Revit API lets the tool read.
        /// </summary>
        public ElementId Id { get; }

        /// <summary>Name the item has in the model.</summary>
        public string Name { get; }

        /// <summary>Extra info shown in the grid: "Drafting" / "Model" for fill patterns, the arrow style for arrowheads, "Built-in" / "Custom" for line styles.</summary>
        public string Details { get; }

        /// <summary>How the item is used, for example "12 lines"; empty when not known.</summary>
        public string UsedBy { get; }

        /// <summary>Number of uses when it is reliably known (0 means unused); null when it is not known.</summary>
        public int? UseCount { get; }

        /// <summary>Why the item can never be deleted from this window (built-in, in use, owned by another user); empty when it can.</summary>
        public string LockReason { get; }

        /// <summary>True when <see cref="LockReason"/> is set.</summary>
        public bool IsLocked => !string.IsNullOrEmpty(LockReason);

        /// <summary>True when the item is known to be unused.</summary>
        public bool IsKnownUnused => UseCount == 0;

        /// <summary>Creates an item. Null texts become empty strings.</summary>
        public DeleteItem(ElementId id, string name, string details, string usedBy, int? useCount, string lockReason)
        {
            Id = id;
            Name = name ?? string.Empty;
            Details = details ?? string.Empty;
            UsedBy = usedBy ?? string.Empty;
            UseCount = useCount;
            LockReason = lockReason ?? string.Empty;
        }
    }
}
