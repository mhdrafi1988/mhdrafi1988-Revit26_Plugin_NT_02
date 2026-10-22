using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DetailLineManager.V001.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Services
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
        /// Line style subcategory id → number of curve elements using it, across the whole project:
        /// every <see cref="CurveElement"/> (model, detail, symbolic and room/area separation lines),
        /// every <see cref="ModelCurve"/> in the Lines category, and sketch lines (filled-region and
        /// other sketch boundaries), including lines in groups.
        /// </summary>
        public static Dictionary<ElementId, int> CollectUsage(Document doc)
        {
            var usage = new Dictionary<ElementId, int>();
            var seen = new HashSet<ElementId>();

            void Add(Element element)
            {
                if (element is not CurveElement curve || !seen.Add(curve.Id)) return;
                ElementId catId;
                try { catId = (curve.LineStyle as GraphicsStyle)?.GraphicsStyleCategory?.Id; }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { return; }
                if (catId == null) return;
                usage[catId] = usage.TryGetValue(catId, out int n) ? n + 1 : 1;
            }

            foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(CurveElement)))
                Add(e);

            // ModelCurves are CurveElements, but collect the Lines category as well in case the
            // class filter leaves some out.
            foreach (var e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Lines).WhereElementIsNotElementType())
                Add(e);

            foreach (Sketch sketch in new FilteredElementCollector(doc).OfClass(typeof(Sketch)))
            {
                IList<ElementId> members;
                try { members = sketch.GetAllElements(); }
                catch (Autodesk.Revit.Exceptions.ApplicationException) { continue; }
                foreach (var id in members) Add(doc.GetElement(id));
            }

            var curveFilter = new ElementClassFilter(typeof(CurveElement));
            foreach (var region in new FilteredElementCollector(doc).OfClass(typeof(FilledRegion)))
            {
                foreach (var id in region.GetDependentElements(curveFilter))
                    Add(doc.GetElement(id));
            }

            return usage;
        }

        /// <summary>Every line style with its usage, built-in styles first, then by name.</summary>
        public static List<LineStyleItem> LoadStyles(Document doc, Dictionary<ElementId, int> usage)
        {
            var list = new List<LineStyleItem>();
            var lines = GetLinesCategory(doc);
            if (lines == null) return list;

            foreach (Category sub in lines.SubCategories)
            {
                if (sub == null) continue;
                var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                usage.TryGetValue(sub.Id, out int count);
                list.Add(new LineStyleItem
                {
                    CategoryId = sub.Id,
                    GraphicsStyleId = gs?.Id ?? ElementId.InvalidElementId,
                    Name = sub.Name,
                    Weight = GetWeight(sub),
                    UseCount = count,
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
