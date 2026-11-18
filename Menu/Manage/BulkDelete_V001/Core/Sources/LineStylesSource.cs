using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Sources
{
    /// <summary>
    /// Line styles: the subcategories of the Lines category (Manage &gt; Object Styles &gt; Lines).
    /// A line style is listed through its projection graphics style (the same handle Bulk Rename uses), but
    /// deleting one deletes its subcategory, which takes its graphics styles with it. Built-in styles and
    /// styles that lines still use are locked: use Delete Line Styles to move lines to another style first.
    /// </summary>
    public sealed class LineStylesSource : DeleteSource
    {
        /// <inheritdoc/>
        public override string Key => "LineStyles";

        /// <inheritdoc/>
        public override string DisplayName => "Line styles";

        /// <inheritdoc/>
        public override string Description
            => "Subcategories of Lines (Object Styles). Built-in styles such as <Thin Lines> and styles that lines still use "
             + "are locked: move those lines to another style first (Delete Line Styles does this).";

        /// <summary>The line style's name is its subcategory's name, read through the graphics style.</summary>
        public override string ReadName(Element element)
            => element is GraphicsStyle style && style.GraphicsStyleCategory != null
                ? style.GraphicsStyleCategory.Name
                : base.ReadName(element);

        /// <summary>
        /// Deleting a line style deletes its subcategory; the projection and cut graphics styles go with it.
        /// Null when the graphics style has no subcategory under Lines.
        /// </summary>
        public override DeletePlan Plan(Document doc, Element element)
        {
            if (element is not GraphicsStyle style) return null;

            var category = style.GraphicsStyleCategory;
            if (category == null || IsBuiltIn(category)) return null;

            var expected = new List<ElementId> { category.Id, style.Id };
            var cut = category.GetGraphicsStyle(GraphicsStyleType.Cut);
            if (cut != null) expected.Add(cut.Id);

            return new DeletePlan(category.Id, expected);
        }

        /// <summary>Blocks a line style that is built-in or that lines use now.</summary>
        public override Func<Element, string> CreateRecheck(Document doc)
        {
            var usage = CountUsage(doc);
            return element =>
            {
                if (element is not GraphicsStyle style || style.GraphicsStyleCategory == null) return "it is not a line style.";
                if (IsBuiltIn(style.GraphicsStyleCategory)) return "Built-in line style.";

                usage.TryGetValue(style.GraphicsStyleCategory.Id, out int count);
                return count > 0 ? UsedReason(count) : null;
            };
        }

        /// <inheritdoc/>
        protected override IEnumerable<FoundItem> Collect(Document doc, Action<LogEntry> log)
        {
            var found = new List<FoundItem>();

            Category lines;
            try { lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { lines = null; }

            if (lines == null)
            {
                log(new LogEntry(LogLevel.Warning, "This document has no Lines category."));
                return found;
            }

            var usage = CountUsage(doc);

            foreach (Category sub in lines.SubCategories)
            {
                if (sub == null) continue;
                var style = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (style == null) continue;

                bool builtIn = IsBuiltIn(sub);
                usage.TryGetValue(sub.Id, out int count);

                string reason = builtIn ? "Built-in line style." : count > 0 ? UsedReason(count) : null;

                found.Add(new FoundItem(
                    style,
                    builtIn ? "Built-in" : "Custom",
                    count == 1 ? "1 line" : $"{count} lines",
                    count,
                    reason));
            }

            return found;
        }

        private static bool IsBuiltIn(Category sub)
            => sub.Id.Value < 0 || sub.BuiltInCategory != BuiltInCategory.INVALID;

        private static string UsedReason(int count)
            => $"{count} line(s) use it. Move them to another line style first (Delete Line Styles does this).";

        // Line style subcategory id -> number of lines using it. Covers model, detail, symbolic and
        // sketch lines, including filled-region boundaries and lines in groups.
        private static Dictionary<ElementId, int> CountUsage(Document doc)
        {
            var counts = new Dictionary<ElementId, int>();
            var seen = new HashSet<ElementId>();

            void Add(CurveElement curve)
            {
                if (curve == null || !seen.Add(curve.Id)) return;
                if (curve.LineStyle is not GraphicsStyle style) return;
                var categoryId = style.GraphicsStyleCategory?.Id;
                if (categoryId == null) return;
                counts[categoryId] = counts.TryGetValue(categoryId, out int c) ? c + 1 : 1;
            }

            foreach (var curve in new FilteredElementCollector(doc).OfClass(typeof(CurveElement)).Cast<CurveElement>())
                Add(curve);

            var curveFilter = new ElementClassFilter(typeof(CurveElement));
            foreach (var region in new FilteredElementCollector(doc).OfClass(typeof(FilledRegion)))
            {
                foreach (var id in region.GetDependentElements(curveFilter))
                    Add(doc.GetElement(id) as CurveElement);
            }

            return counts;
        }
    }
}
