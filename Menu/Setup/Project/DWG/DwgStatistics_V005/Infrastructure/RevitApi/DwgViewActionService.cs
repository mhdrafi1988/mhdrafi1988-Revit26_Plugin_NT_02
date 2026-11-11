using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DwgStatistics.V005.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DwgStatistics.V005.Infrastructure.RevitApi
{
    /// <summary>What to do in one view: the view and the CAD instances to act on there.</summary>
    public sealed class DwgViewTarget
    {
        /// <summary>Creates a target.</summary>
        public DwgViewTarget(long viewId, string viewName, IReadOnlyList<long> instanceIds)
        {
            ViewId = viewId;
            ViewName = viewName;
            InstanceIds = instanceIds;
        }

        /// <summary>Element ID of the view.</summary>
        public long ViewId { get; }

        /// <summary>View name, for the log.</summary>
        public string ViewName { get; }

        /// <summary>CAD instances to hide / unhide / delete in this view.</summary>
        public IReadOnlyList<long> InstanceIds { get; }
    }

    /// <summary>
    /// V004: Hide / Unhide / Delete one DWG file in the views the user ticked. Each call is one
    /// transaction, so one Ctrl+Z in Revit undoes it. Must run in a Revit API context.
    /// </summary>
    public sealed class DwgViewActionService
    {
        private readonly Action<LogEntry> _log;

        /// <summary>Creates the service; <paramref name="log"/> receives per-view messages.</summary>
        public DwgViewActionService(Action<LogEntry> log)
        {
            _log = log ?? (_ => { });
        }

        // ── Hide / Unhide ────────────────────────────────────────────────────

        /// <summary>Hides the given instances in each target view ("Hide in View → Elements").</summary>
        public DwgActionResult Hide(Document doc, string fileName, IReadOnlyList<DwgViewTarget> targets)
            => HideOrUnhide(doc, fileName, targets, hide: true);

        /// <summary>Unhides the given instances in each target view.</summary>
        public DwgActionResult Unhide(Document doc, string fileName, IReadOnlyList<DwgViewTarget> targets)
            => HideOrUnhide(doc, fileName, targets, hide: false);

        private DwgActionResult HideOrUnhide(Document doc, string fileName, IReadOnlyList<DwgViewTarget> targets, bool hide)
        {
            var result = new DwgActionResult { Action = hide ? "Hide in Views" : "Unhide in Views" };
            if (!CanEdit(doc, result)) return result;

            using (var t = new Transaction(doc, $"DWG Statistics: {(hide ? "Hide" : "Unhide")} {fileName} in views"))
            {
                t.Start();
                foreach (var target in targets)
                {
                    ToolCancellation.CheckCurrent();
                    if (doc.GetElement(new ElementId(target.ViewId)) is not View view || view.IsTemplate)
                    {
                        result.Skipped++;
                        _log(new LogEntry(LogLevel.Warning, $"Skipped view #{target.ViewId}: it no longer exists."));
                        continue;
                    }

                    var ids = new List<ElementId>();
                    foreach (long id in target.InstanceIds)
                    {
                        ToolCancellation.CheckCurrent();
                        var element = doc.GetElement(new ElementId(id));
                        if (element == null) continue;
                        bool isHidden = SafeIsHidden(element, view);
                        if (hide && !isHidden && SafeCanBeHidden(element, view)) ids.Add(element.Id);
                        if (!hide && isHidden) ids.Add(element.Id);
                    }

                    if (ids.Count == 0)
                    {
                        result.Skipped++;
                        _log(new LogEntry(LogLevel.Info,
                            $"'{view.Name}': nothing to {(hide ? "hide" : "unhide")} (already {(hide ? "hidden" : "visible")} or not allowed)."));
                        continue;
                    }

                    try
                    {
                        if (hide) view.HideElements(ids);
                        else view.UnhideElements(ids);
                        result.Done++;
                        _log(new LogEntry(LogLevel.Info, $"{(hide ? "Hid" : "Unhid")} {ids.Count} instance(s) in '{view.Name}'."));
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        result.Failed++;
                        _log(new LogEntry(LogLevel.Warning, $"'{view.Name}': {ex.Message}"));
                    }
                }

                if (result.Done == 0)
                {
                    t.RollBack();
                    return result;
                }
                if (t.Commit() != TransactionStatus.Committed) return Rolled(result);
            }

            return result;
        }

        // ── V005: layers ─────────────────────────────────────────────────────

        /// <summary>
        /// Hides (or unhides) the given DWG layers in each target view — the same as unticking the layer
        /// under Imported Categories in Visibility/Graphics. Views whose template controls V/G are skipped
        /// by Revit; they are counted as failed with the reason.
        /// </summary>
        public DwgActionResult SetLayersHidden(Document doc, string fileName, IReadOnlyList<DwgViewTarget> views,
            IReadOnlyList<long> layerIds, bool hide)
        {
            var result = new DwgActionResult { Action = hide ? "Hide Layers" : "Unhide Layers" };
            if (!CanEdit(doc, result)) return result;

            using (var t = new Transaction(doc, $"DWG Statistics: {(hide ? "Hide" : "Unhide")} {layerIds.Count} layer(s) of {fileName}"))
            {
                t.Start();
                foreach (var target in views)
                {
                    ToolCancellation.CheckCurrent();
                    if (doc.GetElement(new ElementId(target.ViewId)) is not View view || view.IsTemplate)
                    {
                        result.Skipped++;
                        continue;
                    }

                    int changed = 0, refused = 0;
                    string reason = null;
                    foreach (long id in layerIds)
                    {
                        ToolCancellation.CheckCurrent();
                        var layerId = new ElementId(id);
                        try
                        {
                            if (!view.CanCategoryBeHidden(layerId))
                            {
                                refused++;
                                reason ??= view.ViewTemplateId != ElementId.InvalidElementId
                                    ? "its view template controls Visibility/Graphics"
                                    : "Revit does not allow hiding it here";
                                continue;
                            }
                            if (view.GetCategoryHidden(layerId) == hide) continue;
                            view.SetCategoryHidden(layerId, hide);
                            changed++;
                        }
                        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                        {
                            refused++;
                            reason ??= ex.Message;
                        }
                    }

                    if (changed > 0)
                    {
                        result.Done++;
                        _log(new LogEntry(LogLevel.Info, $"{(hide ? "Hid" : "Unhid")} {changed} layer(s) in '{view.Name}'."));
                    }
                    else if (refused == 0)
                    {
                        result.Skipped++;
                    }

                    if (refused > 0)
                    {
                        if (changed == 0) result.Failed++;
                        _log(new LogEntry(LogLevel.Warning, $"'{view.Name}': {refused} layer(s) not changed — {reason}."));
                    }
                }

                if (result.Done == 0)
                {
                    t.RollBack();
                    return result;
                }
                if (t.Commit() != TransactionStatus.Committed) return Rolled(result);
            }

            return result;
        }

        // ── Delete ───────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes the "Current view only" instances of the file owned by the target views. Model-placed
        /// instances are never deleted here (deleting one removes it from every view) — they are counted
        /// as skipped with a hint to use Hide instead. Pinned instances are unpinned first (as in DWG
        /// Manager V003). With <paramref name="purgeType"/>, the CAD type is deleted too once no
        /// instance of it is left anywhere.
        /// </summary>
        public DwgActionResult DeleteInViews(Document doc, long typeId, string fileName,
            IReadOnlyList<DwgViewTarget> targets, int modelInstancesSkipped, bool purgeType)
        {
            var result = new DwgActionResult { Action = "Delete in Views" };
            if (!CanEdit(doc, result)) return result;

            if (modelInstancesSkipped > 0)
            {
                result.Skipped += modelInstancesSkipped;
                _log(new LogEntry(LogLevel.Info,
                    $"{modelInstancesSkipped} model-placed instance(s) left alone — deleting them would remove them from every view. Use Hide in Ticked Views."));
            }

            using (var t = new Transaction(doc, $"DWG Statistics: Delete {fileName} in views"))
            {
                var options = t.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningLogger(_log));
                t.SetFailureHandlingOptions(options);
                t.Start();

                foreach (var target in targets)
                {
                    ToolCancellation.CheckCurrent();
                    foreach (long id in target.InstanceIds)
                    {
                        ToolCancellation.CheckCurrent();
                        var elementId = new ElementId(id);
                        var element = doc.GetElement(elementId);
                        if (element == null || !element.IsValidObject)
                        {
                            result.Skipped++;
                            continue;
                        }
                        if (IsOwnedByOtherUser(doc, elementId, out string owner))
                        {
                            result.Skipped++;
                            _log(new LogEntry(LogLevel.Warning, $"Skipped #{id} in '{target.ViewName}': owned by {owner}."));
                            continue;
                        }

                        try
                        {
                            if (element.Pinned) element.Pinned = false;
                            var deleted = doc.Delete(elementId);
                            if (deleted == null || deleted.Count == 0)
                            {
                                result.Failed++;
                                _log(new LogEntry(LogLevel.Warning, $"Could not delete #{id}: Revit deleted nothing."));
                                continue;
                            }
                            result.Done++;
                        }
                        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                        {
                            result.Failed++;
                            _log(new LogEntry(LogLevel.Warning, $"Could not delete #{id} in '{target.ViewName}': {ex.Message}"));
                        }
                    }
                }

                if (purgeType && result.Done > 0) PurgeIfUnused(doc, typeId, fileName, result);

                if (result.Done == 0)
                {
                    t.RollBack();
                    return result;
                }
                if (t.Commit() != TransactionStatus.Committed) return Rolled(result);
            }

            return result;
        }

        private void PurgeIfUnused(Document doc, long typeId, string fileName, DwgActionResult result)
        {
            var id = new ElementId(typeId);
            if (doc.GetElement(id) == null) return;

            int remaining = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Count(i => i.GetTypeId() == id);
            if (remaining > 0)
            {
                _log(new LogEntry(LogLevel.Info, $"Kept type '{fileName}': {remaining} instance(s) are still placed elsewhere."));
                return;
            }

            try
            {
                var deleted = doc.Delete(id);
                if (deleted != null && deleted.Count > 0)
                {
                    result.TypesPurged++;
                    _log(new LogEntry(LogLevel.Info, $"Purged type '{fileName}' (no instance left)."));
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                result.Failed++;
                _log(new LogEntry(LogLevel.Warning, $"Could not purge '{fileName}': {ex.Message}"));
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static bool SafeIsHidden(Element element, View view)
        {
            try { return element.IsHidden(view); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return false; }
        }

        private static bool SafeCanBeHidden(Element element, View view)
        {
            try { return element.CanBeHidden(view); }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return false; }
        }

        private bool CanEdit(Document doc, DwgActionResult result)
        {
            if (doc == null) return false;
            if (!doc.IsReadOnly) return true;

            _log(new LogEntry(LogLevel.Error, "The document is read-only; nothing was changed."));
            result.RolledBack = true;
            return false;
        }

        private DwgActionResult Rolled(DwgActionResult result)
        {
            _log(new LogEntry(LogLevel.Error, $"Revit did not commit '{result.Action}'; every change was rolled back."));
            return new DwgActionResult { Action = result.Action, RolledBack = true };
        }

        private static bool IsOwnedByOtherUser(Document doc, ElementId id, out string owner)
        {
            owner = null;
            if (!doc.IsWorkshared) return false;
            return WorksharingUtils.GetCheckoutStatus(doc, id, out owner) == CheckoutStatus.OwnedByOtherUser;
        }

        /// <summary>Writes Revit's warnings to the tool log (as DWG Manager V003 does); errors keep Revit's handling.</summary>
        private sealed class WarningLogger : IFailuresPreprocessor
        {
            private readonly Action<LogEntry> _log;

            public WarningLogger(Action<LogEntry> log) => _log = log;

            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                foreach (var failure in failuresAccessor.GetFailureMessages()
                             .Where(f => f.GetSeverity() == FailureSeverity.Warning))
                {
                    _log(new LogEntry(LogLevel.Warning, "Revit: " + failure.GetDescriptionText()));
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}
