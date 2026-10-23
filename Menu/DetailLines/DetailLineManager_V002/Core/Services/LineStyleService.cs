using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DetailLineManager.V002.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services
{
    /// <summary>
    /// Line styles (subcategories of Lines): project-wide usage, built-in protection and weights.
    /// Call only in the Revit API context.
    /// </summary>
    public static class LineStyleService
    {
        // Revit's own styles. Most are caught by id / BuiltInCategory, the names are a second guard
        // because some of them are ordinary subcategories in older templates.
        private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Lines", "Thin Lines", "Medium Lines", "Wide Lines", "Hidden Lines"
        };

        /// <summary>The Lines category, or null when the document has none.</summary>
        public static Category GetLinesCategory(Document doc)
        {
            try { return doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        /// <summary>
        /// True for Revit's own line styles (Thin/Medium/Wide Lines, &lt;Hidden&gt;, &lt;Overhead&gt;,
        /// &lt;Centerline&gt;, …). These are never offered for deletion.
        /// </summary>
        public static bool IsBuiltIn(Category subcategory)
        {
            if (subcategory == null) return true;
            if (subcategory.Id.Value < 0 || subcategory.BuiltInCategory != BuiltInCategory.INVALID) return true;
            string name = subcategory.Name ?? string.Empty;
            return (name.StartsWith("<", StringComparison.Ordinal) && name.EndsWith(">", StringComparison.Ordinal))
                || ProtectedNames.Contains(name);
        }

        /// <summary>Projection line weight of <paramref name="subcategory"/> (1 when Revit reports none).</summary>
        public static int GetWeight(Category subcategory)
        {
            try
            {
                int? weight = subcategory?.GetLineWeight(GraphicsStyleType.Projection);
                return weight is > 0 ? weight.Value : 1;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return 1;
            }
        }

        /// <summary>
        /// Line style subcategory id → number of curve elements using it, across the whole project
        /// (see <see cref="UsageCounter"/>), counted in one go.
        /// </summary>
        public static Dictionary<ElementId, int> CollectUsage(Document doc)
        {
            var counter = new UsageCounter(doc);
            while (!counter.Step(System.TimeSpan.MaxValue)) { }
            return counter.Usage;
        }

        /// <summary>
        /// Every line style, built-in styles first, then by name. With <paramref name="usage"/> null
        /// the use counts are unknown and no style is reported as unused.
        /// </summary>
        public static List<LineStyleItem> LoadStyles(Document doc, Dictionary<ElementId, int> usage)
        {
            var list = new List<LineStyleItem>();
            var lines = GetLinesCategory(doc);
            if (lines == null) return list;

            foreach (Category sub in lines.SubCategories)
            {
                if (sub == null) continue;
                var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                int count = 0;
                usage?.TryGetValue(sub.Id, out count);
                list.Add(new LineStyleItem
                {
                    CategoryId = sub.Id,
                    GraphicsStyleId = gs?.Id ?? ElementId.InvalidElementId,
                    Name = sub.Name,
                    Weight = GetWeight(sub),
                    UseCount = count,
                    IsUsageKnown = usage != null,
                    IsBuiltIn = IsBuiltIn(sub)
                });
            }

            return list
                .OrderByDescending(s => s.IsBuiltIn)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}
