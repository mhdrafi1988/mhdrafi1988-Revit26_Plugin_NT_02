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

        /// <summary>Straight lines fully covered by a longer line of the same style, to delete.</summary>
        public List<ElementId> Covered { get; } = new();

        /// <summary>Sets of overlapping straight lines to join into one line each.</summary>
        public List<List<ElementId>> JoinGroups { get; } = new();

        /// <summary>Unused line style subcategory ids to delete.</summary>
        public List<ElementId> Styles { get; } = new();

        /// <summary>Skip (and log) lines in groups. Joins always skip lines in groups.</summary>
        public bool SkipGroups { get; init; } = true;

        /// <summary>Collinearity tolerance for joins, in feet.</summary>
        public double ToleranceFeet { get; init; }
    }

    /// <summary>What a Delete unused run deleted, or with a dry run would delete.</summary>
    public sealed class DeleteSummary
    {
        /// <summary>Duplicate lines.</summary>
        public int Duplicates { get; set; }

        /// <summary>Short lines.</summary>
        public int Short { get; set; }

        /// <summary>Covered lines.</summary>
        public int Covered { get; set; }

        /// <summary>Overlap sets joined into one line.</summary>
        public int JoinGroups { get; set; }

        /// <summary>Lines removed by joins (each set keeps one).</summary>
        public int JoinRemoved { get; set; }

        /// <summary>Line styles.</summary>
        public int Styles { get; set; }

        /// <summary>Items skipped.</summary>
        public int Skipped { get; set; }

        /// <summary>Other elements removed with the lines (dimensions, tags, …) by category name.</summary>
        public Dictionary<string, int> Dependents { get; } = new(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>False when the attached elements could not be measured.</summary>
        public bool DependentsMeasured { get; set; } = true;

        /// <summary>Lines removed in total.</summary>
        public int LinesRemoved => Duplicates + Short + Covered + JoinRemoved;

        /// <summary>Attached elements removed in total.</summary>
        public int DependentCount => Dependents.Values.Sum();

        /// <summary>True when nothing would change.</summary>
        public bool IsEmpty => LinesRemoved == 0 && JoinGroups == 0 && Styles == 0;

        /// <summary>"12 Dimensions, 3 Generic Annotations", or empty.</summary>
        public string DependentsText => string.Join(", ", Dependents.OrderByDescending(p => p.Value).Select(p => $"{p.Value} {p.Key}"));

        /// <summary>One-line description of the lines and styles.</summary>
        public string CountsText
        {
            get
            {
                var parts = new List<string>();
                if (Duplicates > 0) parts.Add($"{Duplicates} duplicate");
                if (Covered > 0) parts.Add($"{Covered} covered");
                if (Short > 0) parts.Add($"{Short} short");
                if (JoinGroups > 0) parts.Add($"{JoinRemoved} merged into {JoinGroups} joined line(s)");
                string lines = $"{LinesRemoved} line(s)" + (parts.Count > 0 ? $" ({string.Join(", ", parts)})" : string.Empty);
                return $"{lines} and {Styles} line style(s)";
            }
        }
    }

    /// <summary>
    /// The model changes the window can make. Each one re-checks every element first (exists, is a
    /// detail line, not owned by another user, not in a group where that matters), borrows the
    /// elements it needs in one call before changing anything (workshared models), logs every skip,
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

        private sealed class JoinOp
        {
            public ElementId Keeper { get; init; }
            public List<ElementId> Others { get; init; }
            public Line NewLine { get; init; }
        }

        /// <summary>Creates the actions for <paramref name="doc"/>; <paramref name="log"/> receives results and skips.</summary>
        public DetailLineActions(Document doc, Action<LogLevel, string> log)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _log = log ?? ((_, _) => { });
            _edit = new EditabilityService(doc);
        }

        /// <summary>
        /// Deletes the lines and styles in <paramref name="plan"/> and joins its overlap sets, or with
        /// <paramref name="dryRun"/> only reports what would happen, including the dimensions, tags
        /// and other elements Revit would remove with the lines (measured by a delete that is rolled
        /// back). Line styles are re-checked as unused against every curve in the project first.
        /// </summary>
        public DeleteSummary DeleteUnused(DeletePlan plan, bool dryRun)
        {
            ResetSkips();
            var summary = new DeleteSummary();
            var lineIds = new List<ElementId>();
            var deleteSet = new HashSet<ElementId>();
            var seen = new HashSet<ElementId>();
            var kinds = new Dictionary<ElementId, string>();

            void Consider(ElementId id, string what)
            {
                if (!seen.Add(id)) return;
                string reason = LineBlockReason(_doc.GetElement(id), plan.SkipGroups);
                if (reason != null) { Skip($"Line {id.Value} ({what})", reason); return; }
                lineIds.Add(id);
                deleteSet.Add(id);
                kinds[id] = what;
            }

            foreach (var id in plan.Duplicates) Consider(id, "duplicate");
            foreach (var id in plan.Covered) Consider(id, "covered");
            foreach (var id in plan.ShortLines) Consider(id, "short");

            var joins = BuildJoins(plan, deleteSet);
            var styles = CheckStyles(plan.Styles);

            if (!dryRun && _doc.IsWorkshared)
            {
                var wanted = new List<ElementId>(lineIds);
                foreach (var j in joins) { wanted.Add(j.Keeper); wanted.AddRange(j.Others); }
                var styleGs = new Dictionary<ElementId, ElementId>();
                foreach (var s in styles)
                {
                    var gs = Category.GetCategory(_doc, s.Id)?.GetGraphicsStyle(GraphicsStyleType.Projection);
                    if (gs != null) { styleGs[gs.Id] = s.Id; wanted.Add(gs.Id); }
                }

                var blocked = BorrowUpFront(wanted, id => styleGs.TryGetValue(id, out var cat)
                    ? $"Line style '{styles.First(s => s.Id == cat).Name}'"
                    : $"Line {id.Value}");
                if (blocked.Count > 0)
                {
                    lineIds.RemoveAll(blocked.Contains);
                    joins.RemoveAll(j => blocked.Contains(j.Keeper) || j.Others.Any(blocked.Contains));
                    var blockedStyles = new HashSet<ElementId>(styleGs.Where(p => blocked.Contains(p.Key)).Select(p => p.Value));
                    styles.RemoveAll(s => blockedStyles.Contains(s.Id));
                }
            }
            FlushSkips();

            summary.Duplicates = lineIds.Count(id => kinds[id] == "duplicate");
            summary.Covered = lineIds.Count(id => kinds[id] == "covered");
            summary.Short = lineIds.Count(id => kinds[id] == "short");
            summary.JoinGroups = joins.Count;
            summary.JoinRemoved = joins.Sum(j => j.Others.Count);
            summary.Styles = styles.Count;
            summary.Skipped = _skips;

            var removed = new List<ElementId>(lineIds);
            foreach (var j in joins) removed.AddRange(j.Others);

            if (dryRun)
            {
                MeasureDependents(removed, summary);
                _log(LogLevel.Info, $"Preview (no changes made): would delete {summary.CountsText}; {summary.Skipped} skipped."
                    + (summary.DependentCount > 0 ? $" Revit would also remove {summary.DependentsText} attached to these lines." : string.Empty)
                    + (summary.DependentsMeasured ? string.Empty : " Attached dimensions and tags could not be measured."));
                if (styles.Count > 0)
                    _log(LogLevel.Info, "Styles that would be deleted: " + string.Join(", ", styles.Take(25).Select(s => s.Name))
                        + (styles.Count > 25 ? $" … and {styles.Count - 25} more" : string.Empty));
                return summary;
            }

            if (summary.IsEmpty)
            {
                _log(LogLevel.Warning, $"Nothing to delete; {summary.Skipped} item(s) skipped.");
                return summary;
            }

            int dependents = 0;
            bool ok = TransactionRunner.Run(_doc, "Delete unused detail lines", () =>
            {
                if (lineIds.Count > 0)
                {
                    var deleted = _doc.Delete(lineIds);
                    dependents += Math.Max(0, (deleted?.Count ?? 0) - lineIds.Count);
                }
                foreach (var j in joins)
                {
                    if (_doc.GetElement(j.Keeper) is not CurveElement keeper)
                        throw new InvalidOperationException($"Line {j.Keeper.Value} disappeared during the join.");
                    keeper.SetGeometryCurve(j.NewLine, true);
                    var deleted = _doc.Delete(j.Others);
                    dependents += Math.Max(0, (deleted?.Count ?? 0) - j.Others.Count);
                }
                // Look each style up by id: Category objects go stale once another one is deleted.
                foreach (var style in styles)
                    _doc.Delete(style.Id);
            }, _log);

            if (!ok) return new DeleteSummary { Skipped = summary.Skipped };
            _log(LogLevel.Success, $"Deleted {summary.CountsText}; {summary.Skipped} skipped."
                + (dependents > 0 ? $" {dependents} attached element(s) (dimensions, tags) were removed with them." : string.Empty)
                + " Ctrl+Z in Revit undoes it.");
            return summary;
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

            var blocked = BorrowUpFront(targets.Select(c => c.Id).ToList(), id => $"Line {id.Value}");
            targets.RemoveAll(c => blocked.Contains(c.Id));
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

            var targets = new List<(ElementId Id, string Name, ElementId GsId)>();
            int unchanged = 0;
            foreach (var catId in styleCategoryIds.Where(id => id != null).Distinct())
            {
                var sub = Category.GetCategory(_doc, catId);
                if (sub == null) { Skip($"Line style {catId.Value}", "it no longer exists"); continue; }
                var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                string owner = gs == null ? null : _edit.OwnedByOther(gs);
                if (owner != null) { Skip($"Line style '{sub.Name}'", $"owned by {owner}"); continue; }
                if (LineStyleService.GetWeight(sub) == weight) { unchanged++; continue; }
                targets.Add((sub.Id, sub.Name, gs?.Id));
            }

            var names = targets.Where(t => t.GsId != null).ToDictionary(t => t.GsId, t => t.Name);
            var blocked = BorrowUpFront(names.Keys.ToList(), id => $"Line style '{names[id]}'");
            targets.RemoveAll(t => t.GsId != null && blocked.Contains(t.GsId));
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

            // Element overrides are stored in the view, so the views are what must be borrowed.
            var views = targets.Select(t => t.View).GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.First().Name);
            var blocked = BorrowUpFront(views.Keys.ToList(), id => $"View '{views[id]}'");
            targets.RemoveAll(t => blocked.Contains(t.View.Id));
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

            int viewCount = targets.Select(t => t.View.Id).Distinct().Count();
            _log(LogLevel.Success, (weight.HasValue ? $"Set {what}" : $"Cleared {what}")
                + $" on {targets.Count} line(s) in {viewCount} view(s); {unchanged} unchanged, {_skips} skipped.");
            return targets.Count;
        }

        // ── Borrowing ────────────────────────────────────────────────────

        // Workshared models: borrows every element in one call before the change, logs how many were
        // borrowed and skips (logs) each one another user owns. Returns the ids that could not be had.
        private HashSet<ElementId> BorrowUpFront(IList<ElementId> ids, Func<ElementId, string> describe)
        {
            var blocked = new HashSet<ElementId>();
            if (!_doc.IsWorkshared || ids.Count == 0) return blocked;

            var distinct = ids.Where(id => id != null && id != ElementId.InvalidElementId).Distinct().ToList();
            int alreadyMine = distinct.Count(id => SafeStatus(id) == CheckoutStatus.OwnedByCurrentUser);

            ICollection<ElementId> owned;
            try
            {
                owned = WorksharingUtils.CheckoutElements(_doc, distinct);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                _log(LogLevel.Warning, $"Could not borrow elements in advance ({ex.Message}). Revit will borrow them during the change; "
                    + "if one is not available the whole change is rolled back.");
                return blocked;
            }

            var ownedSet = new HashSet<ElementId>(owned ?? Array.Empty<ElementId>());
            foreach (var id in distinct.Where(id => !ownedSet.Contains(id)))
            {
                blocked.Add(id);
                string owner = _edit.OwnedByOther(_doc.GetElement(id)) ?? "another user, or changed in central";
                Skip(describe(id), $"owned by {owner}");
            }

            int borrowed = distinct.Count - blocked.Count - alreadyMine;
            _log(blocked.Count > 0 ? LogLevel.Warning : LogLevel.Info,
                $"Borrowed {Math.Max(0, borrowed)} element(s) before the change ({alreadyMine} were already yours)"
                + (blocked.Count > 0 ? $"; {blocked.Count} owned by other users are skipped." : "."));
            return blocked;
        }

        private CheckoutStatus SafeStatus(ElementId id)
        {
            try { return WorksharingUtils.GetCheckoutStatus(_doc, id); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return CheckoutStatus.NotOwned; }
        }

        // ── Delete helpers ───────────────────────────────────────────────

        private List<JoinOp> BuildJoins(DeletePlan plan, HashSet<ElementId> deleteSet)
        {
            var joins = new List<JoinOp>();
            double tol = Math.Max(plan.ToleranceFeet, _doc.Application.ShortCurveTolerance);

            foreach (var group in plan.JoinGroups)
            {
                var members = new List<(CurveElement Curve, Line Line)>();
                string block = null;
                foreach (var id in group.Distinct())
                {
                    if (deleteSet.Contains(id)) continue;
                    var e = _doc.GetElement(id);
                    string reason = LineBlockReason(e, skipGroups: true);
                    if (reason != null) { block = $"line {id.Value} {reason}"; break; }
                    if (((CurveElement)e).GeometryCurve is not Line line || !line.IsBound) { block = $"line {id.Value} is no longer straight"; break; }
                    members.Add(((CurveElement)e, line));
                }

                string label = $"Overlap set of {group.Count} line(s) starting at {group.First().Value}";
                if (block != null) { Skip(label, block); continue; }
                if (members.Count < 2) continue;

                if (members.Select(m => m.Curve.OwnerViewId).Distinct().Count() > 1
                    || members.Select(m => m.Curve.LineStyle?.Id).Distinct().Count() > 1)
                {
                    Skip(label, "its lines no longer share one view and one style");
                    continue;
                }

                var keeper = members.OrderByDescending(m => m.Line.Length).ThenBy(m => m.Curve.Id.Value).First();
                XYZ origin = keeper.Line.GetEndPoint(0);
                XYZ axis = keeper.Line.Direction.Normalize();
                double tMin = 0, tMax = keeper.Line.Length;
                bool collinear = true;
                foreach (var m in members)
                {
                    foreach (var p in new[] { m.Line.GetEndPoint(0), m.Line.GetEndPoint(1) })
                    {
                        var v = p - origin;
                        if (v.CrossProduct(axis).GetLength() > tol) { collinear = false; break; }
                        double t = v.DotProduct(axis);
                        tMin = Math.Min(tMin, t);
                        tMax = Math.Max(tMax, t);
                    }
                    if (!collinear) break;
                }
                if (!collinear) { Skip(label, "its lines are no longer in line with each other"); continue; }

                Line joined;
                try { joined = Line.CreateBound(origin + axis * tMin, origin + axis * tMax); }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex) { Skip(label, $"the joined line could not be made ({ex.Message})"); continue; }

                joins.Add(new JoinOp
                {
                    Keeper = keeper.Curve.Id,
                    Others = members.Where(m => m.Curve.Id != keeper.Curve.Id).Select(m => m.Curve.Id).ToList(),
                    NewLine = joined
                });
            }
            return joins;
        }

        private List<(ElementId Id, string Name)> CheckStyles(List<ElementId> styleIds)
        {
            var styles = new List<(ElementId Id, string Name)>();
            if (styleIds.Count == 0) return styles;

            var usage = LineStyleService.CollectUsage(_doc);
            var linesCat = LineStyleService.GetLinesCategory(_doc);
            foreach (var catId in styleIds.Distinct())
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
            return styles;
        }

        // Deletes the lines in a transaction that is rolled back, then reads what else went with them.
        private void MeasureDependents(List<ElementId> removed, DeleteSummary summary)
        {
            if (removed.Count == 0) return;
            try
            {
                var deleted = TransactionRunner.RunAndRollBack(_doc, "Measure delete", () => _doc.Delete(removed)?.ToList());
                if (deleted == null) { summary.DependentsMeasured = false; return; }

                var own = new HashSet<ElementId>(removed);
                foreach (var id in deleted.Where(id => !own.Contains(id)))
                {
                    var e = _doc.GetElement(id);
                    // Skip internal parts (sketch planes, references) that have no category.
                    string category = e?.Category?.Name;
                    if (string.IsNullOrEmpty(category)) continue;
                    summary.Dependents[category] = summary.Dependents.TryGetValue(category, out int n) ? n + 1 : 1;
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                summary.DependentsMeasured = false;
                _log(LogLevel.Warning, $"Could not measure attached dimensions and tags ({ex.Message}).");
            }
        }

        // ── Checks and skip log ──────────────────────────────────────────

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
