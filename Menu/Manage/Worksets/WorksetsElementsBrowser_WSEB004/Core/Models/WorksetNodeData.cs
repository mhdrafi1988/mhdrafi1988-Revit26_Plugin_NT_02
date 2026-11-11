using System.Collections.Generic;

namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB004.Core.Models
{
    /// <summary>Root of the tree: one user workset, holding its Categories.</summary>
    public class WorksetNodeData
    {
        public string Name { get; }
        public int WorksetId { get; }
        public bool IsEditable { get; }
        public string Owner { get; }

        /// <summary>False when the workset is closed in this session (its elements are not loaded, so it lists none).</summary>
        public bool IsOpen { get; }
        public List<CategoryNodeData> Categories { get; }
        public bool IsEmpty => Categories.Count == 0;

        public WorksetNodeData(string name, int worksetId, bool isEditable, string owner, List<CategoryNodeData> categories, bool isOpen = true)
        {
            Name = name;
            WorksetId = worksetId;
            IsEditable = isEditable;
            Owner = owner;
            Categories = categories;
            IsOpen = isOpen;
        }
    }
}
