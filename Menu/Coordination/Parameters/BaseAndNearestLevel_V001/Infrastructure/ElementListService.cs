using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Infrastructure
{
    /// <summary>One listed element, with the tier its level reading falls in.</summary>
    public sealed class ElementListItem
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>Category name.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>"Family: Type", or the element name when it has no type.</summary>
        public string FamilyAndType { get; set; } = string.Empty;

        /// <summary>Which tier this element's level reading is in.</summary>
        public LevelTier Tier { get; set; } = LevelTier.None;
    }

    /// <summary>One category of the listed elements, with how many of each tier it holds.</summary>
    public sealed class CategoryCount
    {
        /// <summary>Category name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Elements of this category in the list.</summary>
        public int Total { get; set; }

        /// <summary>Of those, how many are Tier 1 (level + offset, re-hostable).</summary>
        public int Tier1 { get; set; }

        /// <summary>Of those, how many are Tier 2 (offset from host).</summary>
        public int Tier2 { get; set; }

        /// <summary>Of those, how many are Tier 3 (level only).</summary>
        public int Tier3 { get; set; }
    }

    /// <summary>What one load produced: the elements and the categories they fall in.</summary>
    public sealed class ListResult
    {
        /// <summary>The listed elements.</summary>
        public List<ElementListItem> Items { get; } = new();

        /// <summary>The categories of those elements, sorted by name, with per-tier counts.</summary>
        public List<CategoryCount> Categories { get; } = new();

        /// <summary>Elements that were looked at but report no level at all.</summary>
        public int WithoutLevel { get; set; }
    }

    /// <summary>
    /// Lists the host model's level-based model elements — any element <see cref="LevelPairReader"/> can
    /// read a level from — and groups them by category. Never a linked element. Runs on the Revit API thread.
    /// <para>
    /// 1.1.0 widened this from a fixed Roofs / Floors / Walls array to whatever the model holds. Those
    /// three stay the default selection, so the window opens on the same elements as before.
    /// </para>
    /// </summary>
    public static class ElementListService
    {
        /// <summary>The categories ticked when the window opens, as in 1.0.0.</summary>
        public static readonly string[] DefaultCategoryNames = { "Roofs", "Floors", "Walls" };

        /// <summary>Categories of the "Structure" preset.</summary>
        public static readonly string[] StructurePresetNames =
        {
            "Walls", "Floors", "Roofs", "Ceilings", "Structural Columns", "Structural Framing",
            "Structural Foundations", "Columns", "Stairs"
        };

        /// <summary>Categories of the "MEP" preset.</summary>
        public static readonly string[] MepPresetNames =
        {
            "Ducts", "Duct Fittings", "Duct Accessories", "Flex Ducts", "Air Terminals",
            "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes", "Plumbing Fixtures",
            "Conduits", "Conduit Fittings", "Cable Trays", "Cable Tray Fittings",
            "Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures", "Mechanical Equipment"
        };

        /// <summary>
        /// The level-based elements visible in <paramref name="view"/>, or all of them in the host model
        /// when <paramref name="view"/> is null.
        /// </summary>
        public static ListResult List(Document doc, View view)
        {
            FilteredElementCollector collector = view == null
                ? new FilteredElementCollector(doc)
                : new FilteredElementCollector(doc, view.Id);

            var result = new ListResult();
            foreach (Element e in collector.WhereElementIsNotElementType())
            {
                ToolCancellation.CheckCurrent();
                Add(doc, e, result, countRejects: true);
            }
            return Finish(result);
        }

        /// <summary>
        /// The level-based elements among <paramref name="ids"/>, for example the current Revit selection.
        /// <paramref name="ignored"/> counts the ids that are not level-based host-model elements.
        /// </summary>
        public static ListResult FromIds(Document doc, IEnumerable<ElementId> ids, out int ignored)
        {
            var result = new ListResult();
            ignored = 0;
            foreach (ElementId id in ids)
            {
                ToolCancellation.CheckCurrent();
                Element e = doc.GetElement(id);
                if (e == null || e is ElementType || !Add(doc, e, result, countRejects: false)) ignored++;
            }
            return Finish(result);
        }

        /// <summary>True when <paramref name="view"/> can list model elements: a graphical, non-template view.</summary>
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

        // Adds e when it is a host-model element with a level. Returns false when it was left out.
        private static bool Add(Document doc, Element e, ListResult result, bool countRejects)
        {
            if (!IsCandidate(e)) return false;

            BasePlacement placement = LevelPairReader.Read(doc, e);
            if (!placement.Found)
            {
                if (countRejects) result.WithoutLevel++;
                return false;
            }

            result.Items.Add(new ElementListItem
            {
                Id = e.Id.Value,
                Category = e.Category.Name,
                FamilyAndType = FamilyAndType(doc, e),
                Tier = placement.Tier
            });
            return true;
        }

        // A placed model element of the host model whose category can carry bound parameters.
        private static bool IsCandidate(Element e)
        {
            Category c = e.Category;
            if (c == null || c.CategoryType != CategoryType.Model) return false;
            if (!c.AllowsBoundParameters) return false;
            if (e.ViewSpecific || e.Id.Value < 0) return false;
            if (e is RevitLinkInstance || e is ImportInstance || e is CurveElement || e is SketchPlane) return false;
            return true;
        }

        private static ListResult Finish(ListResult result)
        {
            foreach (IGrouping<string, ElementListItem> group in result.Items
                         .GroupBy(i => i.Category)
                         .OrderBy(g => g.Key, System.StringComparer.CurrentCultureIgnoreCase))
            {
                result.Categories.Add(new CategoryCount
                {
                    Name = group.Key,
                    Total = group.Count(),
                    Tier1 = group.Count(i => i.Tier == LevelTier.LevelAndOffset),
                    Tier2 = group.Count(i => i.Tier == LevelTier.HostOffset),
                    Tier3 = group.Count(i => i.Tier == LevelTier.ScheduleOnly)
                });
            }

            var sorted = result.Items.OrderBy(i => i.Category, System.StringComparer.CurrentCultureIgnoreCase)
                                     .ThenBy(i => i.Id).ToList();
            result.Items.Clear();
            result.Items.AddRange(sorted);
            return result;
        }

        private static string FamilyAndType(Document doc, Element e)
        {
            if (doc.GetElement(e.GetTypeId()) is ElementType type)
                return string.IsNullOrEmpty(type.FamilyName) ? type.Name : $"{type.FamilyName}: {type.Name}";
            return e.Name ?? string.Empty;
        }
    }
}
