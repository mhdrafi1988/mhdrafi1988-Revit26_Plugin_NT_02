using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>
    /// Finds, deletes and creates the result detail groups. Names are GroupType names:
    /// "EPS2D_&lt;floorId&gt;_&lt;viewId&gt;" (single group) or "EPS2D_&lt;floorId&gt;_&lt;viewId&gt;_&lt;Layer&gt;" (per layer).
    /// Deleting a type removes every instance. Call inside an open transaction.
    /// </summary>
    public static class DetailGroupManager
    {
        public static GroupType? FindType(Document doc, string name) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(GroupType))
                .Cast<GroupType>()
                .FirstOrDefault(t => t.Name == name);

        /// <summary>
        /// Deletes every group type of a previous pattern for this floor and view — the base name and any
        /// "base_…" name (per-layer groups and _2/_3 copies) — whichever grouping mode created it.
        /// Returns the number of group instances removed.
        /// </summary>
        public static int DeleteExisting(Document doc, string baseName)
        {
            List<GroupType> types = new FilteredElementCollector(doc)
                .OfClass(typeof(GroupType))
                .Cast<GroupType>()
                .Where(t => t.Name == baseName || t.Name.StartsWith(baseName + "_", StringComparison.Ordinal))
                .ToList();

            int instances = types.Sum(t => t.Groups.Size);
            foreach (GroupType t in types)
            {
                doc.Delete(t.Id);
            }

            return instances;
        }

        /// <summary>Groups the elements; when the name is taken a suffix _2, _3 … is added.</summary>
        public static Group Create(Document doc, ICollection<ElementId> elementIds, string name)
        {
            Group group = doc.Create.NewGroup(elementIds);
            string unique = name;
            for (int n = 2; FindType(doc, unique) != null; n++)
            {
                unique = $"{name}_{n}";
            }

            group.GroupType.Name = unique;
            return group;
        }
    }
}
