using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DetailLineManager.V001.Infrastructure;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DetailLineManager.V001.Core.Services
{
    /// <summary>What a Delete unused run should remove. Built by the window from the grid.</summary>
    public sealed class DeletePlan
    {
        /// <summary>Duplicate lines to delete (keepers already left out).</summary>
        public List<ElementId> Duplicates { get; } = new();

        /// <summary>Short lines to delete.</summary>
        public List<ElementId> ShortLines { get; } = new();

        /// <summary>Unused line style subcategory ids to delete.</summary>
        public List<ElementId> Styles { get; } = new();

        /// <summary>Skip (and log) lines in groups.</summary>
        public bool SkipGroups { get; init; } = true;
    }

    /// <summary>
    /// The model changes the window can make. Each one re-checks every element first (exists, is a
    /// detail line, not owned by another user, not in a group where that matters), logs every skip,
    /// then runs as one transaction through <see cref="TransactionRunner"/>.
    /// Call only in the Revit API context.
    /// </summary>
    public sealed class DetailLineActions
    {
        private const int MaxSkipLines = 30;

        private readonly Document _doc;
        private readonly Action<LogLevel, string> _log;
        private readonly EditabilityService _edit;
        private int _skipsLogged;
        private int _skips;

        /// <summary>Creates the actions for <paramref name="doc"/>; <paramref name="log"/> receives results and skips.</summary>
        public DetailLineActions(Document doc, Action<LogLevel, string> log)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _log = log ?? ((_, _) => { });
            _edit = new EditabilityService(doc);
        }

        /// <summary>
        /// Deletes the lines and styles in <paramref name="plan"/>, or with <paramref name="dryRun"/>
        /// only reports what would be deleted. Line styles are re-checked as unused against every
        /// curve element in the project just before deleting. Returns the number of items deleted.
        /// </summary>
        public int DeleteUnused(DeletePlan plan, bool dryRun)
        {
            ResetSkips();
            var lineIds = new List<ElementId>();
            var seen = new HashSet<ElementId>();
            int dupCount = 0, shortCount = 0;

            bool Consider(ElementId id, string what)
            {
                if (!seen.Add(id)) return false;
                var e = _doc.GetElement(id);
                string reason = LineBlockReason(e, plan.SkipGroups);
                if (reason != null) { Skip($"Line {id.Value} ({what})", reason); return false; }
                lineIds.Add(id);
                return true;
            }

            foreach (var id in plan.Duplicates) if (Consider(id, "duplicate")) dupCount++;
            foreach (var id in plan.ShortLines) if (Consider(id, "short")) shortCount++;

            var styles = new List<(ElementId Id, string Name)>();
            if (plan.Styles.Count > 0)
            {
                var usage = LineStyleService.CollectUsage(_doc);
                var linesCat = LineStyleService.GetLinesCategory(_doc);
                foreach (var catId in plan.Styles.Distinct())
                {
                    var sub = Category.GetCategory(_doc, catId);
                    if (sub == null || linesCat == null || !linesCat.Id.Equals(sub.Parent?.Id))
                    {
                        Skip($"Line style {catId.Value}", "it no longer exists");
                        continue;
                    }
                    string name = sub.Name;
                    if (LineStyleService.IsBuiltIn(sub)) { Skip($"Line style '{name}'", "it is a built-in style"); continue; }
                    if (usage.TryGetValue(sub.Id, out int n) && n > 0) { Skip($"Line style '{name}'", $"{n} line(s) now use it"); continue; }
                    var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                    string owner = gs == null ? null : _edit.OwnedByOther(gs);
                    if (owner != null) { Skip($"Line style '{name}'", $"owned by {owner}"); continue; }
                    styles.Add((sub.Id, name));
                }
            }
            FlushSkips();

            string counts = $"{lineIds.Count} line(s) ({dupCount} duplicate, {shortCount} short) and {styles.Count} line style(s)";
            if (dryRun)
            {
                _log(LogLevel.Info, $"Preview (no changes made): would delete {counts}; {_skips} skipped.");
                if (styles.Count > 0)
                    _log(LogLevel.Info, "Styles that would be deleted: " + string.Join(", ", styles.Take(25).Select(s => s.Name))
                        + (styles.Count > 25 ? $" … and {styles.Count - 25} more" : string.Empty));
                return 0;
            }

            if (lineIds.Count == 0 && styles.Count == 0)
            {
                _log(LogLevel.Warning, $"Nothing to delete; {_skips} item(s) skipped.");
                return 0;
            }

            int dependents = 0;
            bool ok = TransactionRunner.Run(_doc, "Delete unused detail lines", () =>
            {
                if (lineIds.Count > 0)
                {
                    var deleted = _doc.Delete(lineIds);
                    dependents = Math.Max(0, (deleted?.Count ?? 0) - lineIds.Count);
                }
                // Look each style up by id: Category objects go stale once another one is deleted.
                foreach (var style in styles)
                    _doc.Delete(style.Id);
            }, _log);

            if (!ok) return 0;
            _log(LogLevel.Success, $"Deleted {counts}; {_skips} skipped."
                + (dependents > 0 ? $" {dependents} dependent element(s) (dimensions, tags) were removed with them." : string.Empty)
                + " Ctrl+Z in Revit undoes it.");
            return lineIds.Count + styles.Count;
        }

        /// <summary>
        /// Sets <c>CurveElement.LineStyle</c> of the lines to <paramref name="graphicsStyleId"/>.
        /// Lines in groups, lines owned by others and lines that cannot take the style are skipped.
        /// Returns the number of lines changed.
        /// </summary>
        public int AssignStyle(IReadOnlyCollection<ElementId> ids, ElementId graphicsStyleId)
        {
            ResetSkips();
            if (_doc.GetElement(graphicsStyleId) is not GraphicsStyle gs)
            {
                _log(LogLevel.Error, "The chosen line style no longer exists. Click Find lines to reload styles.");
                return 0;
            }
            string styleName = gs.GraphicsStyleCategory?.Name ?? gs.Name;

            var targets = new List<CurveElement>();
            int unchanged = 0;
            foreach (var id in ids.Distinct())
            {
                var e = _doc.GetElement(id);
                string reason = LineBlockReason(e, skipGroups: true);
                if (reason != null) { Skip($"Line {id.Value}", reason); continue; }
                var curve = (CurveElement)e;
                if (curve.LineStyle?.Id == gs.Id) { unchanged++; continue; }
                if (!curve.GetLineStyleIds().Contains(gs.Id)) { Skip($"Line {id.Value}", $"it cannot use '{styleName}'"); continue; }
                targets.Add(curve);
            }
            FlushSkips();

            if (targets.Count == 0)
            {
                _log(LogLevel.Warning, $"No line to change ({unchanged} already '{styleName}', {_skips} skipped).");
                return 0;
            }

            bool ok = TransactionRunner.Run(_doc, "Assign detail line style", () =>
            {
                foreach (var curve in targets) curve.LineStyle = gs;
            }, _log);
            if (!ok) return 0;

            _log(LogLevel.Success, $"Assigned '{styleName}' to {targets.Count} line(s); {unchanged} already had it, {_skips} skipped.");
            return targets.Count;
        }

        /// <summary>
        /// Sets the projection line weight of the given line styles to <paramref name="weight"/>
        /// (project-wide). Returns the number of styles changed.
        /// </summary>
        public int SetStyleWeight(IReadOnlyCollection<ElementId> styleCategoryIds, int weight)
        {
            ResetSkips();
            if (weight < 1 || weight > 16) throw new ArgumentOutOfRangeException(nameof(weight), "Line weight must be 1–16.");

            var targets = new List<(ElementId Id, string Name)>();
            int unchanged = 0;
            foreach (var catId in styleCategoryIds.Where(id => id != null).Distinct())
            {
                var sub = Category.GetCategory(_doc, catId);
                if (sub == null) { Skip($"Line style {catId.Value}", "it no longer exists"); continue; }
                var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                string owner = gs == null ? null : _edit.OwnedByOther(gs);
                if (owner != null) { Skip($"Line style '{sub.Name}'", $"owned by {owner}"); continue; }
                if (LineStyleService.GetWeight(sub) == weight) { unchanged++; continue; }
                targets.Add((sub.Id, sub.Name));
            }
            FlushSkips();

            if (targets.Count == 0)
            {
                _log(LogLevel.Warning, $"No line style to change ({unchanged} already weight {weight}, {_skips} skipped).");
                return 0;
            }

            bool ok = TransactionRunner.Run(_doc, "Change line style weight", () =>
            {
                foreach (var t in targets)
                {
                    var sub = Category.GetCategory(_doc, t.Id)
                        ?? throw new InvalidOperationException($"Line style '{t.Name}' disappeared during the change.");
                    sub.SetLineWeight(weight, GraphicsStyleType.Projection);
                }
            }, _log);
            if (!ok) return 0;

            _log(LogLevel.Success, $"Set projection line weight {weight} on {targets.Count} line style(s) (project-wide): "
                + string.Join(", ", targets.Take(15).Select(t => t.Name)) + (targets.Count > 15 ? " …" : string.Empty)
                + $". {unchanged} already had it, {_skips} skipped.");
            return targets.Count;
        }

        /// <summary>
        /// Sets (or with <paramref name="weight"/> null, clears) a projection line weight override on
        /// each line in its owner view. Other override settings are kept. Returns the number of lines changed.
        /// </summary>
        public int SetViewOverride(IReadOnlyCollection<ElementId> ids, int? weight)
        {
            ResetSkips();
            if (weight is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(weight), "Line weight must be 1–16.");

            var targets = new List<(View View, ElementId Id)>();
            var viewChecks = new Dictionary<ElementId, string>();
            int unchanged = 0;
            int pen = weight ?? OverrideGraphicSettings.InvalidPenNumber;

            foreach (var id in ids.Distinct())
            {
                var e = _doc.GetElement(id);
                if (e == null || !DetailLineScanner.IsDetailLine(e)) { Skip($"Line {id.Value}", "it no longer exists"); continue; }

                var view = _doc.GetElement(e.OwnerViewId) as View;
                if (!viewChecks.TryGetValue(e.OwnerViewId, out string viewReason))
                    viewChecks[e.OwnerViewId] = viewReason = ViewBlockReason(view);
                if (viewReason != null) { Skip($"Line {id.Value}", viewReason); continue; }

                int current = view.GetElementOverrides(id).ProjectionLineWeight;
                if (current == pen || (weight == null && current <= 0)) { unchanged++; continue; }
                targets.Add((view, id));
            }
            FlushSkips();

            string what = weight.HasValue ? $"weight {weight} override" : "line weight override";
            if (targets.Count == 0)
            {
                _log(LogLevel.Warning, $"No line to change ({unchanged} unchanged, {_skips} skipped).");
                return 0;
            }

            bool ok = TransactionRunner.Run(_doc, weight.HasValue ? "Override detail line weight" : "Clear detail line weight override", () =>
            {
                foreach (var (view, id) in targets)
                {
                    var ogs = view.GetElementOverrides(id);
                    ogs.SetProjectionLineWeight(pen);
                    view.SetElementOverrides(id, ogs);
                }
            }, _log);
            if (!ok) return 0;

            int views = targets.Select(t => t.View.Id).Distinct().Count();
            _log(LogLevel.Success, (weight.HasValue ? $"Set {what}" : $"Cleared {what}")
                + $" on {targets.Count} line(s) in {views} view(s); {unchanged} unchanged, {_skips} skipped.");
            return targets.Count;
        }

        private string LineBlockReason(Element e, bool skipGroups)
        {
            if (e == null || !e.IsValidObject) return "it no longer exists";
            if (!DetailLineScanner.IsDetailLine(e)) return "it is not a detail line";
            string reason = _edit.BlockReason(e);
            if (reason != null) return reason;
            if (skipGroups && EditabilityService.InGroup(e)) return "it is in a group";
            return null;
        }

        private string ViewBlockReason(View view)
        {
            if (view == null) return "its view no longer exists";
            if (view.IsTemplate) return "its view is a view template";
            try
            {
                if (!view.AreGraphicsOverridesAllowed()) return $"view '{view.Name}' does not allow graphic overrides";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return $"view '{view.Name}' does not allow graphic overrides";
            }
            string owner = _edit.OwnedByOther(view);
            return owner == null ? null : $"view '{view.Name}' is owned by {owner}";
        }

        private void ResetSkips()
        {
            _skips = 0;
            _skipsLogged = 0;
        }

        private void Skip(string what, string reason)
        {
            _skips++;
            if (_skipsLogged >= MaxSkipLines) return;
            _skipsLogged++;
            _log(LogLevel.Warning, $"Skipped {what}: {reason}.");
        }

        private void FlushSkips()
        {
            if (_skips > _skipsLogged)
                _log(LogLevel.Warning, $"… {_skips - _skipsLogged} more skipped (not listed).");
        }
    }
}
