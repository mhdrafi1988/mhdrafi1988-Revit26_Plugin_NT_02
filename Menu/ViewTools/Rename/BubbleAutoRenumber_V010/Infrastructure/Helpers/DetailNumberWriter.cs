using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Models;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Core.Services;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V010.Infrastructure.Helpers
{
    /// <summary>
    /// Writes a RenumberPlan to the model in two passes. Must be called inside an
    /// open transaction — the caller owns it, so the whole run is one undo step.
    ///
    /// Pass 1 stamps every target with its placeholder; pass 2 writes the finals.
    /// Revit refuses two viewports on one sheet holding the same detail number, so
    /// writing finals directly over the old numbers collides whenever the sequence
    /// overlaps what is already there — which is every re-run. See
    /// DetailNumberAllocator for the full account of the V008 behaviour this replaces.
    /// </summary>
    internal static class DetailNumberWriter
    {
        /// <summary>
        /// Applies <paramref name="plan"/>, filling <paramref name="summary"/> as it goes.
        /// <paramref name="onProgress"/> is called with (done, total) over both passes.
        /// </summary>
        public static void Apply(
            Document doc,
            RenumberPlan plan,
            RenumberSummary summary,
            Action<int, int> onProgress)
        {
            if (doc == null || plan == null || summary == null) return;

            var failures = new Dictionary<long, string>();
            var outcomes = new Dictionary<long, RenumberOutcome>();

            int total = plan.Assignments.Count * 2;
            int done = 0;

            // ── Pass 1 — placeholders ────────────────────────────────────────────
            foreach (RenumberAssignment assignment in plan.Assignments)
            {
                try
                {
                    Set(doc, assignment.ViewId, assignment.TempNumber);
                }
                catch (Exception ex)
                {
                    // This view keeps its original number. That number was not in the
                    // reserved set (the plan expected to overwrite it), so another
                    // view's final may now collide with it — that write then fails in
                    // pass 2 and is reported there rather than silently mis-numbering.
                    failures[assignment.ViewId] = ex.Message;
                    outcomes[assignment.ViewId] = RenumberOutcome.Failed;
                }

                onProgress?.Invoke(++done, total);
            }

            // ── Pass 2 — final numbers ───────────────────────────────────────────
            foreach (RenumberAssignment assignment in plan.Assignments)
            {
                if (failures.ContainsKey(assignment.ViewId))
                {
                    onProgress?.Invoke(++done, total);
                    continue;
                }

                try
                {
                    Set(doc, assignment.ViewId, assignment.FinalNumber);

                    outcomes[assignment.ViewId] = assignment.IsUnchanged
                        ? RenumberOutcome.Unchanged
                        : RenumberOutcome.Renumbered;
                }
                catch (Exception ex)
                {
                    failures[assignment.ViewId] = ex.Message;
                    outcomes[assignment.ViewId] = RenumberOutcome.Failed;
                }

                onProgress?.Invoke(++done, total);
            }

            // ── Report, in plan order so it reads top-left to bottom-right ───────
            foreach (RenumberAssignment assignment in plan.Assignments)
            {
                RenumberOutcome outcome = outcomes.TryGetValue(assignment.ViewId, out RenumberOutcome found)
                    ? found
                    : RenumberOutcome.Failed;

                failures.TryGetValue(assignment.ViewId, out string message);

                summary.Results.Add(new RenumberResult
                {
                    ViewName = assignment.ViewName,
                    KindLabel = assignment.KindLabel,
                    PreviousNumber = assignment.PreviousNumber,
                    NewNumber = outcome == RenumberOutcome.Failed
                        ? assignment.PreviousNumber
                        : assignment.FinalNumber,
                    Outcome = outcome,
                    Message = message ?? string.Empty
                });

                switch (outcome)
                {
                    case RenumberOutcome.Renumbered: summary.Renumbered++; break;
                    case RenumberOutcome.Unchanged: summary.Unchanged++; break;
                    default: summary.Failed++; break;
                }
            }

            foreach (RenumberSkip skip in plan.Skips)
            {
                summary.Skipped++;

                summary.Results.Add(new RenumberResult
                {
                    ViewName = skip.ViewName,
                    KindLabel = skip.KindLabel,
                    PreviousNumber = skip.CurrentNumber,
                    NewNumber = RenumberPlanner.NoNumber,
                    Outcome = RenumberOutcome.Skipped,
                    Message = skip.Reason
                });
            }
        }

        private static void Set(Document doc, long viewId, string number)
        {
            if (doc.GetElement(new ElementId(viewId)) is not View view)
                throw new InvalidOperationException("The view no longer exists in the model.");

            Parameter parameter = view.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER);

            if (parameter == null)
                throw new InvalidOperationException("The view has no detail number parameter.");

            if (parameter.IsReadOnly)
                throw new InvalidOperationException(RenumberPlanner.ReadOnlyReason + ".");

            if (!parameter.Set(number))
                throw new InvalidOperationException($"Revit refused the detail number \"{number}\".");
        }
    }
}
