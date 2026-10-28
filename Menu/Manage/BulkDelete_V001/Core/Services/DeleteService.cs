using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BulkDelete.V001.Core.Models;
using Revit26_Plugin_Revised.BulkDelete.V001.Core.Sources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.BulkDelete.V001.Core.Services
{
    /// <summary>
    /// Deletes items from the model. The service writes no log lines: it returns one outcome per item, and
    /// the caller reports them once the transaction is over, so the log never shows a deletion that was
    /// rolled back.
    /// </summary>
    public sealed class DeleteService
    {
        /// <summary>
        /// Deletes the items in <paramref name="requests"/> in one transaction, so one Ctrl+Z undoes the whole
        /// run. Each item is re-checked first: one that no longer exists, was renamed outside the window, is
        /// now in use or owned by another user is skipped and left in the model. Each deletion runs in its
        /// own sub-transaction and is undone, and the item skipped, when Revit would delete anything beyond
        /// the item itself (for example elements that depend on it) or refuses. Throws on any other error;
        /// the transaction's using block then rolls everything back. Must run in a Revit API context.
        /// </summary>
        /// <param name="doc">Document to change.</param>
        /// <param name="source">Kind of item being deleted; reads the names and rules that are re-checked.</param>
        /// <param name="requests">The deletions the user confirmed.</param>
        /// <param name="transactionName">Name of the undo step in Revit.</param>
        public DeleteResult Apply(Document doc, DeleteSource source, IReadOnlyList<DeleteRequest> requests, string transactionName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (source == null) throw new ArgumentNullException(nameof(source));

            var outcomes = new List<DeleteOutcome>();
            if (requests == null || requests.Count == 0) return new DeleteResult(outcomes);

            var recheck = source.CreateRecheck(doc);

            using var t = new Transaction(doc, transactionName);
            var started = t.Start();
            if (started != TransactionStatus.Started)
                throw new InvalidOperationException($"Revit did not start the deletion (status: {started}).");

            int deleted = 0;
            foreach (var request in requests)
            {
                var outcome = DeleteOne(doc, source, recheck, request);
                if (outcome.Deleted) deleted++;
                outcomes.Add(outcome);
            }

            if (deleted == 0)
            {
                t.RollBack();
                return new DeleteResult(outcomes);
            }

            var status = t.Commit();
            if (status != TransactionStatus.Committed)
                throw new InvalidOperationException($"Revit did not commit the deletions (status: {status}).");

            return new DeleteResult(outcomes);
        }

        private static DeleteOutcome DeleteOne(Document doc, DeleteSource source, Func<Element, string> recheck, DeleteRequest request)
        {
            var element = doc.GetElement(request.Id);
            if (element == null)
                return DeleteOutcome.Skip(request.Name, "it no longer exists.");

            string now = source.ReadName(element);
            if (!string.Equals(now, request.Name, StringComparison.Ordinal))
                return DeleteOutcome.Skip(request.Name, $"it is now called '{now}'. Reload the list and try again.");

            string blocked = recheck(element) ?? WorksharingGuard.OwnerLock(doc, request.Id);
            if (blocked != null)
                return DeleteOutcome.Skip(request.Name, blocked);

            var plan = source.Plan(doc, element);
            if (plan == null)
                return DeleteOutcome.Skip(request.Name, "it cannot be deleted.");

            using var st = new SubTransaction(doc);
            st.Start();
            try
            {
                var removed = doc.Delete(plan.Target);
                int others = removed?.Count(id => !plan.Expected.Contains(id)) ?? 0;
                if (others > 0)
                {
                    st.RollBack();
                    return DeleteOutcome.Skip(request.Name,
                        $"deleting it would also delete {others} other element(s) that depend on it.");
                }

                st.Commit();
                return DeleteOutcome.Done(request.Name);
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
                                          || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                return DeleteOutcome.Skip(request.Name, $"Revit refused: {ex.Message}");
            }
        }
    }
}
