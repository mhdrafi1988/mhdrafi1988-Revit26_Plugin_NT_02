using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BaseLevelToParameter.V001.Infrastructure
{
    /// <summary>One listed element: id, category and family/type, before its base level is read.</summary>
    public sealed class ElementListItem
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>Category name.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>"Family: Type", or the element name when it has no type.</summary>
        public string FamilyAndType { get; set; } = string.Empty;
    }

    /// <summary>Lists the host model's model elements (never linked elements). Runs on the Revit API thread.</summary>
    public static class ElementListService
    {
        /// <summary>
        /// Returns the model elements visible in <paramref name="view"/>, or every model element of the
        /// host model when <paramref name="view"/> is null.
        /// </summary>
        public static List<ElementListItem> List(Document doc, View view)
        {
            FilteredElementCollector collector = view == null
                ? new FilteredElementCollector(doc)
                : new FilteredElementCollector(doc, view.Id);

            var list = new List<ElementListItem>();
            foreach (Element e in collector.WhereElementIsNotElementType())
            {
                ToolCancellation.CheckCurrent();
                if (!IsModelElement(e)) continue;
                list.Add(new ElementListItem
                {
                    Id = e.Id.Value,
                    Category = e.Category.Name,
                    FamilyAndType = FamilyAndType(doc, e)
                });
            }
            return list.OrderBy(i => i.Category).ThenBy(i => i.Id).ToList();
        }

        /// <summary>
        /// True when <paramref name="view"/> can list model elements: a graphical, non-template view
        /// (plan, ceiling plan, section, elevation, 3D, area plan, structural plan).
        /// </summary>
        public static bool CanListFrom(View view)
        {
            if (view == null || view.IsTemplate) return false;
            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                case ViewType.ThreeD:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>A placed model element of the host model: has a model category, is not view-specific, a link, an import or a model line.</summary>
        private static bool IsModelElement(Element e)
        {
            Category c = e.Category;
            if (c == null || c.CategoryType != CategoryType.Model) return false;
            if (e.ViewSpecific) return false;
            if (e is RevitLinkInstance || e is ImportInstance || e is CurveElement || e is SketchPlane) return false;
            if (e.Id.Value < 0) return false;
            return true;
        }

        private static string FamilyAndType(Document doc, Element e)
        {
            if (e is FamilyInstance fi && fi.Symbol != null)
                return $"{fi.Symbol.FamilyName}: {fi.Symbol.Name}";
            if (doc.GetElement(e.GetTypeId()) is ElementType type)
                return string.IsNullOrEmpty(type.FamilyName) ? type.Name : $"{type.FamilyName}: {type.Name}";
            return e.Name ?? string.Empty;
        }
    }
}
