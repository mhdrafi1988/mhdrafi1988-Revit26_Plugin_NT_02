using Autodesk.Revit.DB;

namespace Revit26_Plugin.BulkRename.V001.Core.Models
{
    /// <summary>
    /// One rename the user confirmed. Requests are captured the moment the user clicks Yes, so the names
    /// applied are exactly the names shown in the confirmation, whatever happens in the window afterwards.
    /// </summary>
    public sealed class RenameRequest
    {
        /// <summary>Id of the element to rename (see <see cref="RenameItem.Id"/>).</summary>
        public ElementId Id { get; }

        /// <summary>Name the item had when it was listed. Apply skips the item if it has been renamed since.</summary>
        public string CurrentName { get; }

        /// <summary>Validated, trimmed name to give the item.</summary>
        public string NewName { get; }

        /// <summary>Creates a request. Null names become empty strings.</summary>
        public RenameRequest(ElementId id, string currentName, string newName)
        {
            Id = id;
            CurrentName = currentName ?? string.Empty;
            NewName = newName ?? string.Empty;
        }
    }
}
