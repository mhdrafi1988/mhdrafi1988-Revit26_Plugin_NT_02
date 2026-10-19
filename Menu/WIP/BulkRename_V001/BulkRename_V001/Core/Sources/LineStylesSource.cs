using Autodesk.Revit.DB;
using Revit26_Plugin.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin.BulkRename.V001.Core.Sources
{
    /// <summary>
    /// Line styles: the subcategories of the Lines category (Manage &gt; Object Styles &gt; Lines).
    /// A category's own name is read-only in the Revit API, so a line style is renamed through its
    /// projection graphics style, which is why the row's id is the graphics style's id.
    /// </summary>
    public sealed class LineStylesSource : RenameSource
    {
        /// <inheritdoc/>
        public override string Key => "LineStyles";

        /// <inheritdoc/>
        public override string DisplayName => "Line styles";

        /// <inheritdoc/>
        public override string Description
            => "Subcategories of Lines (Object Styles). Revit's built-in styles such as <Thin Lines> cannot be renamed.";

        /// <summary>The line style's name is its subcategory's name, read through the graphics style.</summary>
        public override string ReadName(Element element)
            => element is GraphicsStyle style && style.GraphicsStyleCategory != null
                ? style.GraphicsStyleCategory.Name
                : base.ReadName(element);

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

                bool builtIn = sub.Id.Value < 0 || sub.BuiltInCategory != BuiltInCategory.INVALID;
                usage.TryGetValue(sub.Id, out int count);

                found.Add(new FoundItem(
                    style,
                    builtIn ? "Built-in" : "Custom",
                    count == 1 ? "1 line" : $"{count} lines",
                    builtIn ? "Built-in line style." : null));
            }

            return found;
        }

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
