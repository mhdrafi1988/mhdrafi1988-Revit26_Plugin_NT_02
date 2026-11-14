using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.DwgManager.V003.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;

namespace Revit26_Plugin_Revised.DwgManager.V003.Core.Services
{
    /// <summary>
    /// Deletes CAD instances (and optionally their unused types) in one transaction.
    /// Execution mode A: one run, all-or-nothing — a failure Revit cannot recover from rolls the whole
    /// delete back. Single elements Revit refuses are skipped and logged as warnings.
    /// Must run in a Revit API context.
    /// </summary>
    public sealed class DwgDeleteService
    {
        private readonly Action<LogEntry> _log;

        /// <summary>Creates the service; <paramref name="log"/> receives per-element messages.</summary>
        public DwgDeleteService(Action<LogEntry> log)
        {
            _log = log ?? (_ => { });
        }

        /// <summary>Deletes everything in <paramref name="requests"/> and returns the counts.</summary>
        public DwgDeleteResult Delete(Document doc, IReadOnlyList<DwgDeleteRequest> requests)
        {
            var result = new DwgDeleteResult();
            if (doc == null || requests == null || requests.Count == 0) return result;

            if (doc.IsReadOnly)
            {
                _log(new LogEntry(LogLevel.Error, "The document is read-only; nothing was deleted."));
                result.RolledBack = true;
                return result;
            }

            using (var transaction = new Transaction(doc, "DWG Manager: Delete CAD"))
            {
                var options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningLogger(_log));
                transaction.SetFailureHandlingOptions(options);
                transaction.Start();

                var typesToPurge = new HashSet<ElementId>();

                foreach (var request in requests)
                {
                    ToolCancellation.CheckCurrent();
                    foreach (var id in request.InstanceIds)
                        DeleteOne(doc, id, $"{request.FileName} in {request.ViewName}", result, isType: false);

                    if (request.PurgeType) typesToPurge.Add(request.TypeId);
                }

                foreach (var typeId in typesToPurge)
                    PurgeIfUnused(doc, typeId, result);

                var status = transaction.Commit();
                if (status != TransactionStatus.Committed)
                {
                    _log(new LogEntry(LogLevel.Error, $"Revit did not commit the delete ({status}); every change was rolled back."));
                    return new DwgDeleteResult { RolledBack = true };
                }
            }

            return result;
        }

        private void PurgeIfUnused(Document doc, ElementId typeId, DwgDeleteResult result)
        {
            var type = doc.GetElement(typeId);
            if (type == null) return;

            int remaining = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Count(i => i.GetTypeId() == typeId);

            if (remaining > 0)
            {
                result.TypesKept++;
                _log(new LogEntry(LogLevel.Info,
                    $"Kept type '{type.Name}': {remaining} instance(s) are still placed in other views."));
                return;
            }

            if (DeleteOne(doc, typeId, $"type '{type.Name}'", result, isType: true))
                result.TypesPurged++;
        }

        // Deletes one element; expected refusals are counted and logged instead of stopping the run.
        private bool DeleteOne(Document doc, ElementId id, string label, DwgDeleteResult result, bool isType)
        {
            var element = doc.GetElement(id);
            if (element == null || !element.IsValidObject)
            {
                if (!isType) result.Skipped++;
                _log(new LogEntry(LogLevel.Warning, $"Skipped #{id.Value} ({label}): it no longer exists."));
                return false;
            }

            if (IsOwnedByOtherUser(doc, id, out string owner))
            {
                if (isType) result.Failed++;
                else result.Skipped++;
                _log(new LogEntry(LogLevel.Warning, $"Skipped #{id.Value} ({label}): owned by {owner}."));
                return false;
            }

            try
            {
                // V003: CAD links are often pinned (auto-pinned when placed by shared coordinates),
                // and Revit can refuse to delete a pinned element. Unpin inside this transaction —
                // a rollback or Ctrl+Z restores the pin along with the element.
                if (!isType && element.Pinned)
                {
                    element.Pinned = false;
                    _log(new LogEntry(LogLevel.Info, $"Unpinned #{id.Value} ({label}) before deleting."));
                }

                var deleted = doc.Delete(id);
                if (deleted == null || deleted.Count == 0)
                {
                    result.Failed++;
                    _log(new LogEntry(LogLevel.Warning, $"Could not delete #{id.Value} ({label}): Revit deleted nothing."));
                    return false;
                }
                if (!isType) result.Deleted++;
                return true;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                result.Failed++;
                _log(new LogEntry(LogLevel.Warning, $"Could not delete #{id.Value} ({label}): {ex.Message}"));
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                result.Failed++;
                _log(new LogEntry(LogLevel.Warning, $"Could not delete #{id.Value} ({label}): {ex.Message}"));
            }

            return false;
        }

        private static bool IsOwnedByOtherUser(Document doc, ElementId id, out string owner)
        {
            owner = null;
            if (!doc.IsWorkshared) return false;

            var status = WorksharingUtils.GetCheckoutStatus(doc, id, out owner);
            return status == CheckoutStatus.OwnedByOtherUser;
        }

        /// <summary>
        /// Writes Revit's warnings for the delete transaction to the tool log. Warnings are not
        /// suppressed — Revit still shows them — and errors keep Revit's default handling.
        /// </summary>
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
