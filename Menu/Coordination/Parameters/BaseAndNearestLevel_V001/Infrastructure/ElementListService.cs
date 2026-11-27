using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure
{
    /// <summary>One listed element, before its levels are read.</summary>
    public sealed class ElementListItem
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>Category name: Roofs, Floors or Walls.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>"Family: Type", or the element name when it has no type.</summary>
        public string FamilyAndType { get; set; } = string.Empty;
    }

    /// <summary>
    /// Lists the host model's roofs, floors and walls — never another category, never a linked element.
    /// Runs on the Revit API thread.
    /// </summary>
    public static class ElementListService
    {
        /// <summary>The only categories this tool ever touches.</summary>
        public static readonly BuiltInCategory[] Categories =
        {
            BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Walls
        };

        /// <summary>Display names of <see cref="Categories"/>, in window order.</summary>
        public static readonly string[] CategoryNames = { "Roofs", "Floors", "Walls" };

        /// <summary>
        /// The roofs, floors and walls visible in <paramref name="view"/>, or all of them in the host model
        /// when <paramref name="view"/> is null.
        /// </summary>
        public static List<ElementListItem> List(Document doc, View view)
        {
            FilteredElementCollector collector = view == null
                ? new FilteredElementCollector(doc)
                : new FilteredElementCollector(doc, view.Id);

            var filter = new ElementMulticategoryFilter(Categories);
            var list = new List<ElementListItem>();
            foreach (Element e in collector.WhereElementIsNotElementType().WherePasses(filter))
            {
                ToolCancellation.CheckCurrent();
                if (!IsSupported(e)) continue;
                list.Add(Item(doc, e));
            }
            return Sort(list);
        }

        /// <summary>
        /// The roofs, floors and walls among <paramref name="ids"/>, for example the current Revit selection.
        /// <paramref name="ignored"/> counts the ids of any other kind.
        /// </summary>
        public static List<ElementListItem> FromIds(Document doc, IEnumerable<ElementId> ids, out int ignored)
        {
            ignored = 0;
            var list = new List<ElementListItem>();
            foreach (ElementId id in ids)
            {
                Element e = doc.GetElement(id);
                if (e == null || e is ElementType || !IsSupported(e))
                {
                    ignored++;
                    continue;
                }
                list.Add(Item(doc, e));
            }
            return Sort(list);
        }

        /// <summary>
        /// True when <paramref name="view"/> can list model elements: a graphical, non-template view.
        /// </summary>
        public static bool CanListFrom(View view)
        {
            if (view == null || view.IsTemplate) return false;
            return view.ViewType switch
            {
                ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.EngineeringPlan or ViewType.AreaPlan
                    or ViewType.Section or ViewType.Elevation or ViewType.Detail or ViewType.ThreeD => true,
                _ => false
            };
        }

        /// <summary>A placed roof, floor or wall of the host model.</summary>
        private static bool IsSupported(Element e)
        {
            Category c = e.Category;
            if (c == null || c.CategoryType != CategoryType.Model) return false;
            if (!Categories.Contains((BuiltInCategory)c.Id.Value)) return false;
            if (e.ViewSpecific || e.Id.Value < 0) return false;
            return true;
        }

        private static ElementListItem Item(Document doc, Element e) => new()
        {
            Id = e.Id.Value,
            Category = e.Category.Name,
            FamilyAndType = FamilyAndType(doc, e)
        };

        private static List<ElementListItem> Sort(List<ElementListItem> list) =>
            list.OrderBy(i => i.Category).ThenBy(i => i.Id).ToList();

        private static string FamilyAndType(Document doc, Element e)
        {
            if (doc.GetElement(e.GetTypeId()) is ElementType type)
                return string.IsNullOrEmpty(type.FamilyName) ? type.Name : $"{type.FamilyName}: {type.Name}";
            return e.Name ?? string.Empty;
        }
    }
}
