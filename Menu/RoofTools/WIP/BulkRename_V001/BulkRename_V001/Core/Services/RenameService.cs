using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BulkRename.V001.Core.Models;
using Revit26_Plugin_Revised.BulkRename.V001.Core.Sources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.BulkRename.V001.Core.Services
{
    /// <summary>
    /// Renames items in the model. Every kind of item is renamed the same way, by setting the element's
    /// <c>Name</c>, so one service covers line styles, line patterns, arrowheads and fill patterns.
    /// The service writes no log lines: it returns one outcome per item, and the caller reports them once
    /// the transaction is over, so the log never shows a rename that was rolled back.
    /// </summary>
    public sealed class RenameService
    {
        private const string TempPrefix = "__bulk_rename_";

        /// <summary>
        /// Renames the items in <paramref name="requests"/> in one transaction, so one Ctrl+Z undoes the whole
        /// run. Each item is re-checked first: one that no longer exists, was renamed outside the window, or is
        /// now owned by another user is skipped and left unchanged. A name Revit refuses skips only that item.
        /// Swaps, chains and case-only changes go through a temporary name (see <see cref="RenamePlanner"/>).
        /// Throws on any other error; the transaction's using block then rolls everything back. Must run in a
        /// Revit API context.
        /// </summary>
        /// <param name="doc">Document to change.</param>
        /// <param name="source">Kind of item being renamed; reads the names that are re-checked.</param>
        /// <param name="requests">The renames the user confirmed, with validated, trimmed new names.</param>
        /// <param name="transactionName">Name of the undo step in Revit.</param>
        public RenameResult Apply(Document doc, RenameSource source, IReadOnlyList<RenameRequest> requests, string transactionName)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (source == null) throw new ArgumentNullException(nameof(source));

            var outcomes = new List<RenameOutcome>();
            var work = new List<RenameRequest>();

            foreach (var request in requests ?? Array.Empty<RenameRequest>())
            {
                string reason = WhyLeaveAlone(doc, source, request);
                if (reason != null) outcomes.Add(RenameOutcome.Skip(request.CurrentName, request.NewName, reason));
                else work.Add(request);
            }

            if (work.Count == 0) return new RenameResult(outcomes);

            var changes = work.Select((w, i) => new RenameChange(i, w.CurrentName, w.NewName)).ToList();
            var steps = RenamePlanner.Plan(changes, () => TempPrefix + Guid.NewGuid().ToString("N"));

            using var t = new Transaction(doc, transactionName);
            var started = t.Start();
            if (started != TransactionStatus.Started)
                throw new InvalidOperationException($"Revit did not start the rename (status: {started}).");

            var failed = new HashSet<int>();
            var onTempName = new HashSet<int>();
            int renamed = 0;

            foreach (var step in steps)
            {
                if (failed.Contains(step.Key)) continue;

                var request = work[step.Key];
                var element = doc.GetElement(request.Id);

                try
                {
                    element.Name = step.Name;

                    if (step.IsTemporary)
                    {
                        onTempName.Add(step.Key);
                    }
                    else
                    {
                        renamed++;
                        outcomes.Add(RenameOutcome.Done(request.CurrentName, request.NewName));
                    }
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
                                              || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    failed.Add(step.Key);
                    outcomes.Add(RenameOutcome.Skip(request.CurrentName, request.NewName,
                        $"Revit refused the name '{step.Name}': {ex.Message}"));

                    // Put an item that already moved to its temporary name back where it was.
                    if (onTempName.Remove(step.Key))
                        RestoreName(element, request.CurrentName, ex);
                }
            }

            if (renamed == 0)
            {
                t.RollBack();
                return new RenameResult(outcomes);
            }

            var status = t.Commit();
            if (status != TransactionStatus.Committed)
                throw new InvalidOperationException($"Revit did not commit the renames (status: {status}).");

            return new RenameResult(outcomes);
        }

        // Why the item must be left as it is, or null when it can be renamed.
        private static string WhyLeaveAlone(Document doc, RenameSource source, RenameRequest request)
        {
            var element = doc.GetElement(request.Id);
            if (element == null) return "it no longer exists.";

            string now = source.ReadName(element);
            if (!string.Equals(now, request.CurrentName, StringComparison.Ordinal))
                return $"it is now called '{now}'. Reload the list and try again.";

            return WorksharingGuard.OwnerLock(doc, request.Id);
        }

        // If the original name cannot be restored the model would be left half-renamed, so give up:
        // the exception unwinds past the transaction's using block and rolls everything back.
        private static void RestoreName(Element element, string original, Exception cause)
        {
            try
            {
                element.Name = original;
            }
            catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
                                          || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Could not put '{original}' back after Revit refused its new name ({cause.Message}). " +
                    "Nothing was renamed.", ex);
            }
        }
    }
}
