using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services
{
    /// <summary>Outcome of <see cref="AlignApplier.Apply"/>.</summary>
    public sealed class ApplyResult
    {
        /// <summary>True when the transaction committed.</summary>
        public bool Success { get; init; }

        /// <summary>Elements moved.</summary>
        public int Moved { get; init; }

        /// <summary>Failure reason when <see cref="Success"/> is false.</summary>
        public string Error { get; init; }
    }

    /// <summary>
    /// Moves the elements of an <see cref="AlignPlan"/> in one transaction, so the sheet is either
    /// aligned or untouched — never half-aligned — and the user gets a single Undo step.
    /// </summary>
    public static class AlignApplier
    {
        /// <summary>Transaction name shown in Revit's Undo list.</summary>
        public const string TransactionName = "Sheet Align & Distribute";

        /// <summary>Applies <paramref name="plan"/>. The caller must have checked <see cref="AlignPlan.CanApply"/>.</summary>
        public static ApplyResult Apply(Document doc, AlignPlan plan)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.CanApply)
                return new ApplyResult { Error = plan.Blocker ?? "Every element is already in place." };
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
                    Move(doc, move);
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    return new ApplyResult { Error = "Revit did not commit the change; nothing was moved." };

                return new ApplyResult { Success = true, Moved = moves.Count };
            }
            catch (OperationCanceledException)
            {
                throw; // a user cancel is never reported as a failure; the using block rolls back
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();
                return new ApplyResult { Error = ex.Message + " Nothing was moved." };
            }
        }

        /// <summary>
        /// Moves one element by its planned delta. A viewport moves by its box centre and a schedule
        /// by its placement point; both are offsets, so the same delta works for either. A pinned
        /// element is unpinned for the move and pinned again straight after.
        /// </summary>
        private static void Move(Document doc, AlignMove move)
        {
            Element element = doc.GetElement(move.Element.ElementId);
            if (element == null)
                throw new InvalidOperationException(
                    $"'{move.Element.Name}' is no longer on the sheet — press Refresh and try again.");

            var delta = new XYZ(move.DeltaX, move.DeltaY, 0);
            bool wasPinned = element.Pinned;
            if (wasPinned) element.Pinned = false;

            try
            {
                switch (element)
                {
                    case Viewport viewport:
                        viewport.SetBoxCenter(viewport.GetBoxCenter() + delta);
                        break;

                    case ScheduleSheetInstance schedule:
                        schedule.Point += delta;
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"'{move.Element.Name}' is no longer a kind of element this tool can move.");
                }
            }
            finally
            {
                // Re-pin even if the move threw: the rollback restores the position, not the pin flag.
                if (wasPinned && element.IsValidObject) element.Pinned = true;
            }
        }
    }
}
