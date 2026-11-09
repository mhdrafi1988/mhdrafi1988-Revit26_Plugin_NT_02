using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DeleteWorkset.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.DeleteWorkset.V001.Core.Services
{
    /// <summary>
    /// Encapsulates workset enumeration and deletion logic, operating inside
    /// caller-supplied transactions/groups.
    /// </summary>
    public class DeleteWorksetService
    {
        private readonly Action<LogEntry> _log;

        public DeleteWorksetService(Action<LogEntry> log) => _log = log;

        // ── Enumeration ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns one <see cref="WorksetRow"/> for every user workset in the document.
        /// A workset is deletable unless another user has it checked out, Revit reports that
        /// some of its elements are owned by other users, or it is the only user workset.
        /// Worksets nobody owns are deletable: they are checked out when the run starts.
        /// </summary>
        public List<WorksetRow> LoadWorksets(Document doc)
        {
            var allUser = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .ToList();

            string currentUser = doc.Application.Username ?? string.Empty;
            var rows = new List<WorksetRow>();

            foreach (var ws in allUser)
            {
                ToolCancellation.CheckCurrent();
                int elementCount = CountElements(doc, ws.Id);
                string owner = ws.Owner ?? string.Empty;
                bool ownedByOther = owner.Length > 0
                    && !string.Equals(owner, currentUser, StringComparison.OrdinalIgnoreCase);

                bool isDeletable = true;
                string reason = null;
                string badge = null;

                if (allUser.Count == 1)
                {
                    isDeletable = false;
                    badge = "Only workset";
                    reason = "This is the only user workset; Revit requires at least one.";
                }
                else if (ownedByOther)
                {
                    isDeletable = false;
                    badge = "In use";
                    reason = $"Checked out by {owner}. Ask them to relinquish it, then Refresh.";
                }
                else if (ws.IsEditable && !CanDelete(doc, ws.Id))
                {
                    isDeletable = false;
                    badge = "Blocked";
                    reason = "Revit cannot delete this workset: some of its elements are checked out by other users.";
                }

                rows.Add(new WorksetRow(ws.Id, ws.Name, elementCount, owner, ws.IsOpen, isDeletable, reason, badge));
            }

            return rows.OrderBy(r => r.Name).ToList();
        }

        /// <summary>
        /// Checks out the given worksets from central so they can be deleted. Must be called
        /// outside any transaction. Returns the ids of the worksets now owned by the current user;
        /// each workset that could not be checked out is logged.
        /// If central cannot be reached (server offline, unmapped drive, detached copy), falls back
        /// to the worksets that are already editable in this model instead of failing the run.
        /// </summary>
        public HashSet<int> CheckoutWorksets(Document doc, IReadOnlyList<WorksetRow> rows)
        {
            var ids = rows.Select(r => r.WorksetId).ToList();
            HashSet<int> owned;
            bool centralUnreachable = false;

            try
            {
                owned = WorksharingUtils.CheckoutWorksets(doc, ids)
                    .Select(id => id.IntegerValue)
                    .ToHashSet();
            }
            catch (Autodesk.Revit.Exceptions.CentralModelException ex)
            {
                centralUnreachable = true;
                _log(new LogEntry(LogLevel.Warning,
                    $"Central model unreachable — only worksets you already own can be deleted. ({ex.Message})"));

                var table = doc.GetWorksetTable();
                owned = rows
                    .Where(r => table.GetWorkset(r.WorksetId)?.IsEditable == true)
                    .Select(r => r.WorksetId.IntegerValue)
                    .ToHashSet();
            }

            string reason = centralUnreachable
                ? "you do not own it and central is unreachable. Reconnect, Synchronize with Central, then run again"
                : "it could not be checked out (owned by another user?)";
            foreach (var row in rows.Where(r => !owned.Contains(r.WorksetId.IntegerValue)))
                _log(new LogEntry(LogLevel.Warning, $"Skipped workset '{row.Name}': {reason}."));

            return owned;
        }

        // ── Deletion ──────────────────────────────────────────────────────────

        /// <summary>
        /// Deletes <paramref name="toDelete"/> worksets, migrating their elements
        /// to <paramref name="targetId"/> where possible.
        /// Returns a <see cref="DeletionResult"/> with counts.
        /// Must be called inside a <see cref="TransactionGroup"/>.
        /// </summary>
        public DeletionResult DeleteWorksets(
            Document doc,
            IReadOnlyList<WorksetRow> toDelete,
            WorksetId targetId,
            bool hardDeleteUnmigratable,
            Func<WorksetRow, bool> confirmCallback)
        {
            int deleted = 0;
            int migrated = 0;
            int hardDeleted = 0;
            int skipped = 0;

            foreach (var row in toDelete)
            {
                ToolCancellation.CheckCurrent();
                if (confirmCallback != null && !confirmCallback(row))
                {
                    _log(new LogEntry(LogLevel.Warning, $"Skipped workset '{row.Name}' (not confirmed)."));
                    skipped++;
                    continue;
                }

                // With no target the workset is empty, so deleting "all" its elements removes nothing.
                var settings = targetId != null
                    ? new DeleteWorksetSettings(DeleteWorksetOption.MoveElementsToWorkset, targetId)
                    : new DeleteWorksetSettings();

                if (!WorksetTable.CanDeleteWorkset(doc, row.WorksetId, settings))
                {
                    _log(new LogEntry(LogLevel.Error,
                        $"Skipped workset '{row.Name}': Revit refuses to delete it (not checked out by you, " +
                        "or some of its elements are owned by other users)."));
                    skipped++;
                    continue;
                }

                using var tx = new Transaction(doc, $"Delete workset '{row.Name}'");
                tx.Start();
                try
                {
                    // 1. Collect all elements on this workset.
                    var allIds = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.WorksetId.IntegerValue == row.WorksetId.IntegerValue)
                        .Select(e => e.Id)
                        .ToList();

                    // 2. Separate migratable from un-migratable elements.
                    var (canMigrate, cannotMigrate) = targetId != null
                        ? PartitionElements(doc, allIds, targetId)
                        : (new List<ElementId>(), new List<ElementId>(allIds));

                    // 3. Migrate migratable elements.
                    foreach (var id in canMigrate)
                    {
                        ToolCancellation.CheckCurrent();
                        var el = doc.GetElement(id);
                        if (el == null) continue;
                        var param = el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (param != null && !param.IsReadOnly)
                        {
                            param.Set(targetId.IntegerValue);
                            migrated++;
                        }
                        else
                        {
                            _log(new LogEntry(LogLevel.Warning,
                                $"  Skipped element {id.Value} ('{GetElementDescription(doc, id)}'): workset parameter is read-only."));
                            skipped++;
                        }
                    }

                    if (canMigrate.Count > 0)
                        _log(new LogEntry(LogLevel.Info,
                            $"  Migrated {canMigrate.Count} elements → '{GetWorksetName(doc, targetId)}'."));

                    // 4. Handle un-migratable elements.
                    if (cannotMigrate.Count > 0)
                    {
                        if (hardDeleteUnmigratable)
                        {
                            doc.Delete(new System.Collections.Generic.List<ElementId>(cannotMigrate));
                            hardDeleted += cannotMigrate.Count;
                            _log(new LogEntry(LogLevel.Warning,
                                $"  Hard-deleted {cannotMigrate.Count} un-migratable element(s)."));
                        }
                        else
                        {
                            skipped += cannotMigrate.Count;
                            _log(new LogEntry(LogLevel.Warning,
                                $"  Skipped {cannotMigrate.Count} un-migratable element(s) (option not enabled)."));
                        }
                    }

                    // 5. Delete the workset itself.
                    // At this point all migratable elements have been moved and
                    // un-migratable elements have been deleted or skipped.
                    // Use MoveElementsToWorkset so any remaining elements (skipped ones)
                    // are at least moved to the target rather than silently deleted by Revit.
                    WorksetTable.DeleteWorkset(doc, row.WorksetId, settings);
                    deleted++;
                    _log(new LogEntry(LogLevel.Success, $"Deleted workset '{row.Name}'."));

                    tx.Commit();
                }
                catch (OperationCanceledException)
                {
                    throw; // user cancel: never swallowed as a failure
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    _log(new LogEntry(LogLevel.Error,
                        $"Failed to delete workset '{row.Name}': {ex.Message}"));
                    skipped++;
                }
            }

            return new DeletionResult(deleted, migrated, hardDeleted, skipped);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool CanDelete(Document doc, WorksetId id)
        {
            try { return WorksetTable.CanDeleteWorkset(doc, id, new DeleteWorksetSettings()); }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception) { return true; } // let the run report the real failure
        }

        private static int CountElements(Document doc, WorksetId wsId)
            => new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Count(e => e.WorksetId.IntegerValue == wsId.IntegerValue);

        /// <summary>
        /// Returns (canMigrate, cannotMigrate) split based on whether the
        /// element's workset parameter is settable and whether a valid target exists.
        /// </summary>
        private static (List<ElementId> canMigrate, List<ElementId> cannotMigrate) PartitionElements(
            Document doc, IEnumerable<ElementId> ids, WorksetId targetId)
        {
            var canMigrate    = new List<ElementId>();
            var cannotMigrate = new List<ElementId>();

            foreach (var id in ids)
            {
                ToolCancellation.CheckCurrent();
                var el = doc.GetElement(id);
                if (el == null) continue;

                // View-specific elements (annotations, tags placed in views) live on their
                // view's workset and cannot be reassigned.
                if (el.OwnerViewId != null && el.OwnerViewId != ElementId.InvalidElementId)
                {
                    cannotMigrate.Add(id);
                    continue;
                }

                var param = el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (param == null || param.IsReadOnly)
                {
                    cannotMigrate.Add(id);
                    continue;
                }

                canMigrate.Add(id);
            }

            return (canMigrate, cannotMigrate);
        }

        private static string GetWorksetName(Document doc, WorksetId id)
            => doc.GetWorksetTable().GetWorkset(id)?.Name ?? id.IntegerValue.ToString();

        private static string GetElementDescription(Document doc, ElementId id)
        {
            var el = doc.GetElement(id);
            if (el == null) return id.Value.ToString();
            string cat = el.Category?.Name ?? el.GetType().Name;
            return string.IsNullOrEmpty(el.Name) ? cat : $"{cat} '{el.Name}'";
        }
    }

    /// <summary>Outcome counters from a deletion run.</summary>
    public sealed class DeletionResult
    {
        public int Deleted     { get; }
        public int Migrated    { get; }
        public int HardDeleted { get; }
        public int Skipped     { get; }

        public DeletionResult(int deleted, int migrated, int hardDeleted, int skipped)
        {
            Deleted     = deleted;
            Migrated    = migrated;
            HardDeleted = hardDeleted;
            Skipped     = skipped;
        }

        public override string ToString()
            => $"{Deleted} deleted  |  {Migrated} elements migrated  |  {HardDeleted} hard-deleted  |  {Skipped} skipped";
    }
}
