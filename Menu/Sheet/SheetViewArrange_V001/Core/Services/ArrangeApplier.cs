using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using System;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetViewArrange.V001.Core.Services
{
    /// <summary>Outcome of <see cref="ArrangeApplier.Apply"/>.</summary>
    public sealed class ApplyResult
    {
        /// <summary>True when the transaction committed.</summary>
        public bool Success { get; init; }

        /// <summary>Viewports moved.</summary>
        public int Moved { get; init; }

        /// <summary>Failure reason when <see cref="Success"/> is false.</summary>
        public string Error { get; init; }
    }

    /// <summary>
    /// Moves the viewports of an <see cref="ArrangePlan"/> in one transaction. Any failure rolls
    /// the whole arrangement back — a sheet is never left half-arranged.
    /// </summary>
    public static class ArrangeApplier
    {
        /// <summary>Transaction name shown in Revit's Undo list.</summary>
        public const string TransactionName = "Sheet View Arrange";

        /// <summary>Applies <paramref name="plan"/>. The caller must have checked <see cref="ArrangePlan.CanApply"/>.</summary>
        public static ApplyResult Apply(Document doc, ArrangePlan plan)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.CanApply)
                return new ApplyResult { Error = plan.Blocker ?? "Nothing to move." };
            if (doc.IsReadOnly)
                return new ApplyResult { Error = "The document is read-only." };

            var moves = plan.Moves.Where(m => m.NeedsMove).ToList();

            using var tx = new Transaction(doc, TransactionName);
            try
            {
                if (tx.Start() != TransactionStatus.Started)
                    return new ApplyResult { Error = "Could not start the transaction." };

                foreach (var move in moves)
                {
                    ToolCancellation.CheckCurrent();
                    if (doc.GetElement(move.Viewport.ViewportId) is not Viewport vp)
                        throw new InvalidOperationException(
                            $"View '{move.Viewport.ViewName}' is no longer on the sheet — press Refresh and try again.");

                    bool wasPinned = vp.Pinned;
                    if (wasPinned)
                        vp.Pinned = false;

                    vp.SetBoxCenter(vp.GetBoxCenter() + new XYZ(move.DeltaX, move.DeltaY, 0));

                    if (wasPinned)
                        vp.Pinned = true;
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    return new ApplyResult { Error = "Revit did not commit the change; nothing was moved." };

                return new ApplyResult { Success = true, Moved = moves.Count };
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                return new ApplyResult { Error = ex.Message + " Nothing was moved." };
            }
        }
    }
}
