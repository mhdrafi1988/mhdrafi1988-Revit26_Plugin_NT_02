using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DwgStatistics.V005.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DwgStatistics.V005.Infrastructure.RevitApi
{
    /// <summary>
    /// The three cleanup actions: Pin All, Reload Links and Purge Unused. Each works on the CAD types
    /// the user ticked and runs as one undoable step (Ctrl+Z in Revit). Must run in a Revit API context.
    /// </summary>
    public sealed class DwgCleanupService
    {
        private readonly Action<LogEntry> _log;

        /// <summary>Creates the service; <paramref name="log"/> receives per-element messages.</summary>
        public DwgCleanupService(Action<LogEntry> log)
        {
            _log = log ?? (_ => { });
        }

        // ── Pin All ──────────────────────────────────────────────────────────

        /// <summary>Pins every unpinned instance of the given CAD types.</summary>
        public DwgActionResult PinAll(Document doc, IReadOnlyCollection<long> typeIds)
        {
            var result = new DwgActionResult { Action = "Pin All" };
            if (!CanEdit(doc, result)) return result;

            var wanted = new HashSet<long>(typeIds);
            var targets = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Where(i => wanted.Contains(i.GetTypeId().Value))
                .ToList();

            using (var t = new Transaction(doc, "DWG Statistics: Pin CAD instances"))
            {
                t.Start();
                foreach (var inst in targets)
                {
                    ToolCancellation.CheckCurrent();
                    if (inst.Pinned)
                    {
                        result.Skipped++;
                        continue;
                    }
                    if (IsOwnedByOtherUser(doc, inst.Id, out string owner))
                    {
                        result.Skipped++;
                        _log(new LogEntry(LogLevel.Warning, $"Skipped #{inst.Id.Value}: owned by {owner}."));
                        continue;
                    }

                    try
                    {
                        inst.Pinned = true;
                        result.Done++;
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        result.Failed++;
                        _log(new LogEntry(LogLevel.Warning, $"Could not pin #{inst.Id.Value}: {ex.Message}"));
                    }
                }

                if (result.Done == 0)
                {
                    t.RollBack();
                    return result;
                }

                if (t.Commit() != TransactionStatus.Committed)
                    return Rolled(result);
            }

            return result;
        }

        // ── Reload Links ─────────────────────────────────────────────────────

        /// <summary>
        /// Reloads the given CAD links from their saved path. Imports are skipped (they have no file to
        /// reload from). Revit's documentation does not say whether CADLinkType.Reload needs an open
        /// transaction, so it is tried inside one first and, if Revit refuses that, outside.
        /// </summary>
        public DwgActionResult ReloadLinks(Document doc, IReadOnlyCollection<long> typeIds)
        {
            var result = new DwgActionResult { Action = "Reload" };
            if (!CanEdit(doc, result)) return result;

            foreach (long id in typeIds)
            {
                ToolCancellation.CheckCurrent();
                var typeId = new ElementId(id);
                if (doc.GetElement(typeId) is not CADLinkType type || !ExternalFileUtils.IsExternalFileReference(doc, typeId))
                {
                    result.Skipped++;
                    _log(new LogEntry(LogLevel.Info, $"Skipped type #{id}: it is an import, not a link."));
                    continue;
                }

                string name = type.Name;
                if (!TryReload(doc, type, name, out string error))
                {
                    result.Failed++;
                    _log(new LogEntry(LogLevel.Warning, $"Could not reload '{name}': {error}"));
                    continue;
                }

                string status = ReadStatus(doc, typeId);
                if (status == nameof(LinkedFileStatus.Loaded))
                {
                    result.Done++;
                    _log(new LogEntry(LogLevel.Success, $"Reloaded '{name}'."));
                }
                else
                {
                    result.Failed++;
                    _log(new LogEntry(LogLevel.Warning, $"'{name}' is still {status} — check the path in Manage Links."));
                }
            }

            return result;
        }

        private bool TryReload(Document doc, CADLinkType type, string name, out string error)
        {
            error = null;

            // Attempt 1: inside a transaction (how CADLinkType.LoadFrom is normally called).
            try
            {
                using (var t = new Transaction(doc, $"DWG Statistics: Reload {name}"))
                {
                    t.Start();
                    type.Reload();
                    if (t.Commit() == TransactionStatus.Committed) return true;
                    error = "Revit did not commit the reload.";
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                error = ex.Message;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                error = ex.Message;
                return false;
            }

            // Attempt 2: outside a transaction (in case Revit manages its own, like RevitLinkType.Reload).
            try
            {
                type.Reload();
                error = null;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ReadStatus(Document doc, ElementId typeId)
        {
            try
            {
                return ExternalFileUtils.GetExternalFileReference(doc, typeId)?.GetLinkedFileStatus().ToString() ?? "unknown";
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return "unknown";
            }
        }

        // ── Purge Unused ─────────────────────────────────────────────────────

        /// <summary>
        /// Deletes the given CAD types if no instance of them is placed anywhere. Types that still have
        /// instances are kept (counted as skipped). Deleting a link type removes it from Manage Links.
        /// </summary>
        public DwgActionResult PurgeUnused(Document doc, IReadOnlyCollection<long> typeIds)
        {
            var result = new DwgActionResult { Action = "Purge Unused" };
            if (!CanEdit(doc, result)) return result;

            var used = new HashSet<long>(new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Select(i => i.GetTypeId().Value));

            using (var t = new Transaction(doc, "DWG Statistics: Purge unused CAD types"))
            {
                t.Start();
                foreach (long id in typeIds)
                {
                    ToolCancellation.CheckCurrent();
                    var typeId = new ElementId(id);
                    var type = doc.GetElement(typeId);
                    if (type == null)
                    {
                        result.Skipped++;
                        continue;
                    }
                    if (used.Contains(id))
                    {
                        result.Skipped++;
                        _log(new LogEntry(LogLevel.Info, $"Kept '{type.Name}': it still has instances."));
                        continue;
                    }
                    if (IsOwnedByOtherUser(doc, typeId, out string owner))
                    {
                        result.Skipped++;
                        _log(new LogEntry(LogLevel.Warning, $"Skipped '{type.Name}': owned by {owner}."));
                        continue;
                    }

                    try
                    {
                        string name = type.Name;
                        var deleted = doc.Delete(typeId);
                        if (deleted == null || deleted.Count == 0)
                        {
                            result.Failed++;
                            _log(new LogEntry(LogLevel.Warning, $"Could not purge '{name}': Revit deleted nothing."));
                            continue;
                        }
                        result.Done++;
                        _log(new LogEntry(LogLevel.Info, $"Purged '{name}'."));
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        result.Failed++;
                        _log(new LogEntry(LogLevel.Warning, $"Could not purge type #{id}: {ex.Message}"));
                    }
                }

                if (result.Done == 0)
                {
                    t.RollBack();
                    return result;
                }

                if (t.Commit() != TransactionStatus.Committed)
                    return Rolled(result);
            }

            return result;
        }

        // ── V005: Move to Workset ─────────────────────────────────────────────

        /// <summary>
        /// Moves every model-placed instance of the given CAD types onto workset <paramref name="worksetId"/>.
        /// "Current view only" instances follow their view's workset and are skipped, as are elements
        /// owned by another user and ones already on the target.
        /// </summary>
        public DwgActionResult MoveToWorkset(Document doc, IReadOnlyCollection<long> typeIds, int worksetId, string worksetName)
        {
            var result = new DwgActionResult { Action = $"Move to '{worksetName}'" };
            if (!CanEdit(doc, result)) return result;
            if (!doc.IsWorkshared)
            {
                _log(new LogEntry(LogLevel.Warning, "The model is not workshared; there are no worksets to move to."));
                return result;
            }

            var wanted = new HashSet<long>(typeIds);
            List<ImportInstance> targets;
            using (var collector = new FilteredElementCollector(doc))
            {
                targets = collector.OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
                    .Where(i => wanted.Contains(i.GetTypeId().Value))
                    .ToList();
            }

            using (var t = new Transaction(doc, $"DWG Statistics: Move CAD to workset {worksetName}"))
            {
                t.Start();
                foreach (var inst in targets)
                {
                    ToolCancellation.CheckCurrent();
                    if (inst.WorksetId.IntegerValue == worksetId)
                    {
                        result.Skipped++;
                        continue;
                    }

                    var p = inst.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (inst.ViewSpecific || p == null || p.IsReadOnly)
                    {
                        result.Skipped++;
                        _log(new LogEntry(LogLevel.Info, $"Skipped #{inst.Id.Value}: 'Current view only' — it follows its view's workset."));
                        continue;
                    }
                    if (IsOwnedByOtherUser(doc, inst.Id, out string owner))
                    {
                        result.Skipped++;
                        _log(new LogEntry(LogLevel.Warning, $"Skipped #{inst.Id.Value}: owned by {owner}."));
                        continue;
                    }

                    try
                    {
                        if (p.Set(worksetId)) result.Done++;
                        else
                        {
                            result.Failed++;
                            _log(new LogEntry(LogLevel.Warning, $"Could not move #{inst.Id.Value}: Revit refused the workset change."));
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                    {
                        result.Failed++;
                        _log(new LogEntry(LogLevel.Warning, $"Could not move #{inst.Id.Value}: {ex.Message}"));
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

        // ── Helpers ──────────────────────────────────────────────────────────

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
    }
}
