using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.PlanRegionManager.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.PlanRegionManager.V001.Core.Services
{
    /// <summary>Counts of one Plan Region Manager action.</summary>
    public sealed class RegionActionResult
    {
        /// <summary>Regions changed.</summary>
        public int Done { get; set; }

        /// <summary>Regions skipped (blocked, not hideable, or Revit refused).</summary>
        public int Skipped { get; set; }

        /// <inheritdoc/>
        public override string ToString() => $"{Done} done, {Skipped} skipped.";
    }

    /// <summary>
    /// Finds plan regions and hides, unhides or deletes them. Plan regions belong to one plan view each
    /// (<see cref="Element.OwnerViewId"/>), so everything is grouped by owner view.
    /// </summary>
    public sealed class PlanRegionService
    {
        private readonly Action<LogEntry> _log;

        /// <summary>Creates the service; <paramref name="log"/> receives progress and problems.</summary>
        public PlanRegionService(Action<LogEntry> log) => _log = log ?? (_ => { });

        /// <summary>True for view types that can hold plan regions: floor, ceiling and structural plans (not templates).</summary>
        public static bool CanHoldPlanRegions(View view)
            => view != null && !view.IsTemplate
               && (view.ViewType == ViewType.FloorPlan
                   || view.ViewType == ViewType.CeilingPlan
                   || view.ViewType == ViewType.EngineeringPlan);

        /// <summary>
        /// Loads plan regions. With <paramref name="onlyViewId"/> set, only that view's regions; otherwise every
        /// plan view's. Hidden regions are included (a view-filtered collector would skip them).
        /// </summary>
        public List<PlanRegionRow> Load(Document doc, ElementId onlyViewId)
        {
            var rows = new List<PlanRegionRow>();
            var categoryId = new ElementId(BuiltInCategory.OST_PlanRegion);
            var views = new Dictionary<ElementId, View>();
            var categoryHidden = new Dictionary<ElementId, bool>();

            var regions = new FilteredElementCollector(doc)
                .OfCategoryId(categoryId)
                .WhereElementIsNotElementType()
                .ToElements();

            foreach (var region in regions)
            {
                try
                {
                    var viewId = region.OwnerViewId;
                    if (viewId == null || viewId == ElementId.InvalidElementId) continue;
                    if (onlyViewId != null && viewId != onlyViewId) continue;

                    if (!views.TryGetValue(viewId, out var view))
                        views[viewId] = view = doc.GetElement(viewId) as View;
                    if (view == null) continue;

                    if (!categoryHidden.TryGetValue(viewId, out var catHidden))
                    {
                        try { catHidden = view.GetCategoryHidden(categoryId); }
                        catch (Exception) { catHidden = false; }
                        categoryHidden[viewId] = catHidden;
                    }

                    bool elementHidden = false;
                    try { elementHidden = region.IsHidden(view); }
                    catch (Exception) { /* view cannot answer; treat as visible */ }

                    string level = (view as ViewPlan)?.GenLevel?.Name;
                    string range = region.get_Parameter(BuiltInParameter.PLAN_REGION_VIEW_RANGE)?.AsValueString();

                    rows.Add(new PlanRegionRow(region.Id, viewId, view.Name, level, range,
                        elementHidden, catHidden, GetBlockReason(doc, region)));
                }
                catch (Exception ex)
                {
                    _log(new LogEntry(LogLevel.Warning, $"Skipped plan region {region?.Id}: {ex.Message}"));
                }
            }

            return rows
                .OrderBy(r => r.GroupOrder)
                .ThenBy(r => r.ViewName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Id.Value)
                .ToList();
        }

        /// <summary>Hides the regions in their own views (Hide in View ▸ Elements).</summary>
        public RegionActionResult Hide(Document doc, IEnumerable<PlanRegionRow> rows)
            => SetHidden(doc, rows, hide: true);

        /// <summary>Unhides regions that were hidden individually.</summary>
        public RegionActionResult Unhide(Document doc, IEnumerable<PlanRegionRow> rows)
            => SetHidden(doc, rows, hide: false);

        private RegionActionResult SetHidden(Document doc, IEnumerable<PlanRegionRow> rows, bool hide)
        {
            var result = new RegionActionResult();
            var work = new List<PlanRegionRow>();
            foreach (var r in rows)
            {
                if (r.IsBlocked) { Skip(result, r, r.BlockReason); continue; }
                if (hide && r.IsElementHidden) { Skip(result, r, "already hidden"); continue; }
                if (!hide && !r.IsElementHidden)
                {
                    Skip(result, r, r.IsHidden ? "its category is hidden, not the element" : "already visible");
                    continue;
                }
                work.Add(r);
            }
            if (work.Count == 0) return result;

            using (var tx = new Transaction(doc, hide ? "Hide plan regions" : "Unhide plan regions"))
            {
                tx.Start();
                try
                {
                    foreach (var byView in work.GroupBy(r => r.ViewId))
                    {
                        var view = doc.GetElement(byView.Key) as View;
                        if (view == null) { foreach (var r in byView) Skip(result, r, "its view no longer exists"); continue; }

                        var ids = new List<ElementId>();
                        foreach (var r in byView)
                        {
                            var el = doc.GetElement(r.Id);
                            if (el == null) { Skip(result, r, "no longer in the model"); continue; }
                            if (hide && !el.CanBeHidden(view)) { Skip(result, r, "Revit cannot hide it"); continue; }
                            ids.Add(r.Id);
                        }
                        if (ids.Count == 0) continue;

                        if (hide) view.HideElements(ids); else view.UnhideElements(ids);
                        result.Done += ids.Count;
                    }
                    tx.Commit();
                }
                catch
                {
                    tx.RollBack();
                    throw;
                }
            }
            return result;
        }

        /// <summary>Deletes the regions. Each is deleted in its own sub-transaction so one refusal does not stop the rest.</summary>
        public RegionActionResult Delete(Document doc, IEnumerable<PlanRegionRow> rows)
        {
            var result = new RegionActionResult();
            using (var tg = new TransactionGroup(doc, "Delete plan regions"))
            {
                tg.Start();
                try
                {
                    foreach (var r in rows)
                    {
                        if (r.IsBlocked) { Skip(result, r, r.BlockReason); continue; }
                        if (doc.GetElement(r.Id) == null) { Skip(result, r, "no longer in the model"); continue; }

                        using (var tx = new Transaction(doc, "Delete plan region"))
                        {
                            tx.Start();
                            try
                            {
                                doc.Delete(r.Id);
                                tx.Commit();
                                result.Done++;
                            }
                            catch (Exception ex)
                            {
                                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                                Skip(result, r, ex.Message);
                            }
                        }
                    }
                    if (result.Done > 0) tg.Assimilate(); else tg.RollBack();
                }
                catch
                {
                    tg.RollBack();
                    throw;
                }
            }
            return result;
        }

        private void Skip(RegionActionResult result, PlanRegionRow r, string why)
        {
            result.Skipped++;
            _log(new LogEntry(LogLevel.Warning, $"Skipped region {r.Id} in '{r.ViewName}': {why}."));
        }

        // Worksharing: a region checked out by another user cannot be changed.
        private static string GetBlockReason(Document doc, Element region)
        {
            try
            {
                if (!doc.IsWorkshared) return string.Empty;
                var status = WorksharingUtils.GetCheckoutStatus(doc, region.Id, out string owner);
                return status == CheckoutStatus.OwnedByOtherUser ? $"Owned by {owner}." : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
