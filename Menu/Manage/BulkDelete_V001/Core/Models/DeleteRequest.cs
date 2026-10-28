using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Models
{
    /// <summary>One deletion the user confirmed: the item and the name it had when it was listed.</summary>
    public sealed class DeleteRequest
    {
        /// <summary>Id of the listed element (see <see cref="DeleteItem.Id"/>).</summary>
        public ElementId Id { get; }

        /// <summary>Name the item had when listed; the run skips the item if its name has changed since.</summary>
        public string Name { get; }

        /// <summary>Creates a request.</summary>
        public DeleteRequest(ElementId id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }
    }
}
