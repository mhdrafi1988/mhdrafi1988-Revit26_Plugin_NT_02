using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DeleteLineStyles.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DeleteLineStyles.V001.Core.Services
{
    /// <summary>A line style that lines can be moved to before their own style is deleted.</summary>
    public sealed class ReplacementStyle
    {
        /// <summary>Projection graphics style id assigned to <see cref="CurveElement.LineStyle"/>.</summary>
        public ElementId GraphicsStyleId { get; }

        /// <summary>Id of the line style's subcategory.</summary>
        public ElementId CategoryId { get; }

        /// <summary>Line style name.</summary>
        public string Name { get; }

        /// <summary>True for Revit's own line styles (&lt;Thin Lines&gt;, &lt;Hidden&gt;, …).</summary>
        public bool IsBuiltIn { get; }

        /// <summary>Creates a replacement option.</summary>
        public ReplacementStyle(ElementId graphicsStyleId, ElementId categoryId, string name, bool isBuiltIn)
        {
            GraphicsStyleId = graphicsStyleId;
            CategoryId = categoryId;
            Name = name;
            IsBuiltIn = isBuiltIn;
        }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>Counts of one Delete Line Styles run.</summary>
    public sealed class DeleteResult
    {
        /// <summary>Line styles deleted.</summary>
        public int Deleted { get; set; }

        /// <summary>Line styles skipped (now in use, blocked, or Revit refused).</summary>
        public int Skipped { get; set; }

        /// <summary>Lines moved to the replacement style.</summary>
        public int LinesMoved { get; set; }

        /// <inheritdoc/>
        public override string ToString()
            => $"{Deleted} deleted, {Skipped} skipped, {LinesMoved} line(s) moved to the replacement style.";
    }

    /// <summary>
    /// Finds custom line styles and which lines use them, and deletes them.
    /// Revit's built-in line styles are never touched.
    /// </summary>
    public sealed class LineStyleService
    {
        private readonly Action<LogEntry> _log;

        /// <summary>Creates the service; <paramref name="log"/> receives progress and warnings.</summary>
        public LineStyleService(Action<LogEntry> log)
        {
            _log = log ?? (_ => { });
        }

        /// <summary>The Lines category, or null when the document has none.</summary>
        public static Category GetLinesCategory(Document doc)
        {
            try { return doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return null; }
        }

        /// <summary>True for Revit's own line styles, which cannot be deleted.</summary>
        public static bool IsBuiltIn(Category subcategory)
            => subcategory.Id.Value < 0 || subcategory.BuiltInCategory != BuiltInCategory.INVALID;

        /// <summary>
        /// Lists every custom line style with its usage and whether it can be deleted.
        /// </summary>
        public List<LineStyleRow> LoadLineStyles(Document doc)
        {
            var rows = new List<LineStyleRow>();
            var lines = GetLinesCategory(doc);
            if (lines == null)
            {
                _log(new LogEntry(LogLevel.Warning, "This document has no Lines category."));
                return rows;
            }

            var usage = CollectUsage(doc);

            foreach (Category sub in lines.SubCategories)
            {
                ToolCancellation.CheckCurrent();
                if (sub == null || IsBuiltIn(sub)) continue;

                usage.TryGetValue(sub.Id, out var lineIds);
                int count = lineIds?.Count ?? 0;
                string reason = GetBlockReason(doc, sub, lineIds);
                rows.Add(new LineStyleRow(sub.Id, sub.Name, count, reason));
            }

            rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            _log(new LogEntry(LogLevel.Info,
                $"Found {rows.Count} custom line style(s): {rows.Count(r => !r.IsUsed)} unused, {rows.Count(r => r.IsUsed)} used."));
            return rows;
        }

        /// <summary>
        /// Every line style lines can be moved to: built-in styles first, then custom ones.
        /// </summary>
        public List<ReplacementStyle> LoadReplacementStyles(Document doc)
        {
            var list = new List<ReplacementStyle>();
            var lines = GetLinesCategory(doc);
            if (lines == null) return list;

            foreach (Category sub in lines.SubCategories)
            {
                ToolCancellation.CheckCurrent();
                if (sub == null) continue;
                var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                if (gs == null) continue;
                list.Add(new ReplacementStyle(gs.Id, sub.Id, sub.Name, IsBuiltIn(sub)));
            }

            return list
                .OrderByDescending(s => s.IsBuiltIn)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Deletes the line styles in <paramref name="rows"/> in one transaction. Usage is
        /// re-checked first: a style that is now in use is skipped unless <paramref name="includeUsed"/>
        /// is set, in which case its lines are moved to <paramref name="replacement"/> before deleting.
        /// A style whose lines cannot all be moved, or that Revit refuses to delete, is skipped and
        /// left unchanged. Throws on unexpected errors; the caller's using block rolls back.
        /// </summary>
        public DeleteResult DeleteLineStyles(Document doc, IReadOnlyList<LineStyleRow> rows,
                                             bool includeUsed, ReplacementStyle replacement)
        {
            var result = new DeleteResult();
            if (rows.Count == 0) return result;

            var lines = GetLinesCategory(doc)
                ?? throw new InvalidOperationException("This document has no Lines category.");
            var usage = CollectUsage(doc);
            // Category objects go stale once any line style is deleted (reading one then throws
            // "Category is unexpectedly NULL"), so keep only ids and look each style up afresh.
            var linesId = lines.Id;

            if (includeUsed && replacement != null && rows.Any(r => r.CategoryId == replacement.CategoryId))
                throw new InvalidOperationException(
                    $"The replacement style '{replacement.Name}' is also selected for deletion.");

            using var t = new Transaction(doc, "Delete Line Styles");
            t.Start();

            foreach (var row in rows)
            {
                ToolCancellation.CheckCurrent();
                var sub = Category.GetCategory(doc, row.CategoryId);
                if (sub == null || !linesId.Equals(sub.Parent?.Id))
                {
                    Skip(result, row.Name, "it no longer exists.");
                    continue;
                }

                // Read everything needed from the category before changing the model.
                string name = sub.Name;
                usage.TryGetValue(row.CategoryId, out var lineIds);
                lineIds ??= new List<ElementId>();

                string block = GetBlockReason(doc, sub, lineIds);
                if (!string.IsNullOrEmpty(block))
                {
                    Skip(result, name, block);
                    continue;
                }

                if (lineIds.Count > 0 && !includeUsed)
                {
                    Skip(result, name, $"{lineIds.Count} line(s) now use it.");
                    continue;
                }

                if (lineIds.Count > 0 && replacement == null)
                {
                    Skip(result, name, "it is in use and no replacement style is chosen.");
                    continue;
                }

                using var st = new SubTransaction(doc);
                st.Start();
                try
                {
                    int moved = MoveLines(doc, lineIds, replacement, name);
                    if (moved < 0)
                    {
                        st.RollBack();
                        result.Skipped++;
                        continue;
                    }

                    doc.Delete(row.CategoryId);
                    st.Commit();

                    result.Deleted++;
                    result.LinesMoved += moved;
                    _log(new LogEntry(LogLevel.Success, moved > 0
                        ? $"Deleted '{name}' ({moved} line(s) moved to '{replacement.Name}')."
                        : $"Deleted '{name}'."));
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
                                              || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                    Skip(result, name, $"Revit refused: {ex.Message}");
                }
            }

            if (result.Deleted == 0)
            {
                t.RollBack();
                return result;
            }

            var status = t.Commit();
            if (status != TransactionStatus.Committed)
                throw new InvalidOperationException($"Revit did not commit the deletion (status: {status}).");

            return result;
        }

        // Moves lines to the replacement style. Returns the count, or -1 (logged) if any line
        // cannot take the replacement style.
        private int MoveLines(Document doc, List<ElementId> lineIds, ReplacementStyle replacement, string styleName)
        {
            if (lineIds.Count == 0) return 0;

            var gs = doc.GetElement(replacement.GraphicsStyleId) as GraphicsStyle;
            if (gs == null)
            {
                _log(new LogEntry(LogLevel.Warning,
                    $"Skipped '{styleName}': replacement style '{replacement.Name}' no longer exists."));
                return -1;
            }

            int moved = 0;
            foreach (var id in lineIds)
            {
                ToolCancellation.CheckCurrent();
                if (doc.GetElement(id) is not CurveElement curve) continue;

                if (!curve.GetLineStyleIds().Contains(gs.Id))
                {
                    _log(new LogEntry(LogLevel.Warning,
                        $"Skipped '{styleName}': line {id.Value} cannot use '{replacement.Name}'. Choose another replacement style."));
                    return -1;
                }

                curve.LineStyle = gs;
                moved++;
            }
            return moved;
        }

        private void Skip(DeleteResult result, string name, string reason)
        {
            result.Skipped++;
            _log(new LogEntry(LogLevel.Warning, $"Skipped '{name}': {reason}"));
        }

        // Line style subcategory id → lines using it. Covers model, detail, symbolic and sketch
        // lines (including filled-region boundaries and lines in groups).
        private static Dictionary<ElementId, List<ElementId>> CollectUsage(Document doc)
        {
            var usage = new Dictionary<ElementId, List<ElementId>>();
            var seen = new HashSet<ElementId>();

            void Add(CurveElement curve)
            {
                if (curve == null || !seen.Add(curve.Id)) return;
                if (curve.LineStyle is not GraphicsStyle gs) return;
                var catId = gs.GraphicsStyleCategory?.Id;
                if (catId == null) return;
                if (!usage.TryGetValue(catId, out var list))
                    usage[catId] = list = new List<ElementId>();
                list.Add(curve.Id);
            }

            foreach (var curve in new FilteredElementCollector(doc).OfClass(typeof(CurveElement)).Cast<CurveElement>())
                Add(curve);

            // Filled-region boundaries are sketch lines; collect them explicitly in case the
            // class filter does not return them.
            var curveFilter = new ElementClassFilter(typeof(CurveElement));
            foreach (var region in new FilteredElementCollector(doc).OfClass(typeof(FilledRegion)))
            {
                ToolCancellation.CheckCurrent();
                foreach (var id in region.GetDependentElements(curveFilter))
                    Add(doc.GetElement(id) as CurveElement);
            }

            return usage;
        }

        // Why a line style cannot be deleted now, or null.
        private static string GetBlockReason(Document doc, Category sub, IList<ElementId> lineIds)
        {
            if (!doc.IsWorkshared) return null;

            var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            if (gs != null && WorksharingUtils.GetCheckoutStatus(doc, gs.Id) == CheckoutStatus.OwnedByOtherUser)
                return $"Owned by {Owner(doc, gs.Id)}. Ask them to synchronize and relinquish.";

            if (lineIds == null) return null;
            int lockedLines = lineIds.Count(id => WorksharingUtils.GetCheckoutStatus(doc, id) == CheckoutStatus.OwnedByOtherUser);
            return lockedLines > 0
                ? $"{lockedLines} line(s) using it are owned by other users."
                : null;
        }

        private static string Owner(Document doc, ElementId id)
        {
            try { return WorksharingUtils.GetWorksharingTooltipInfo(doc, id).Owner; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return "another user"; }
        }
    }
}
