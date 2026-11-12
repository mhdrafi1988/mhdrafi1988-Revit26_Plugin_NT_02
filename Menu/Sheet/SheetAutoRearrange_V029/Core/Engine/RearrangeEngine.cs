using Revit26_Plugin_Revised.Shared.Services;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Models;
using Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Services;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V029.Core.Engine
{
    /// <summary>
    /// Orchestrates a full Run: deletes unticked viewports, packs ticked
    /// viewports, and moves each viewport to its new center. One Transaction
    /// per Run (per suite convention — no transaction-in-loop). Rolls back
    /// entirely on failure.
    ///
    /// V009 CHANGE: algorithm selection removed from the UI — Sheet Order is
    /// now the only algorithm, called directly (no more switch/dispatch on
    /// a RearrangeAlgorithm parameter).
    ///
    /// V021 CLEANUP: RearrangeAlgorithm enum and ReadingOrderPackingService
    /// (confirmed unused since V009, per the note above) deleted outright.
    ///
    /// V009 CHANGE: with the Placeable Area UI (including manual override)
    /// removed, a null region at Run time can ONLY mean "no title block
    /// found, or its geometry could not be read" — the caller
    /// (SheetAutoRearrangeEventHandler.ExecuteRun) already checks
    /// MultipleTitleBlocksFound separately BEFORE calling Run, so that case
    /// never reaches here. Message updated accordingly.
    ///
    /// V026 CHANGE: Run() gains enablePriorityGroups/priorityOrder, forwarded
    /// straight through to SheetOrderPackingService.Pack() — see that
    /// class's BuildPriorityGroups/PackGroup remarks for the actual grouping
    /// logic. Off by default, so an existing caller passing false/empty gets
    /// identical behavior to every version before V026.
    ///
    /// V029 CHANGE: Run() gains alignDistribute (Align &amp; Distribute options), forwarded
    /// to SheetOrderPackingService.Pack(); its notes (applied / skipped) go to the log.
    /// Null or all Off = the V028 result.
    /// </summary>
    public class RearrangeEngine
    {
        private readonly SheetOrderPackingService _sheetOrderService = new();

        public RunResult Run(
            Document doc,
            ViewSheet sheet,
            List<ViewOnSheetItem> allItems,
            OverflowHandlingMode overflowMode,
            GapSettings gapSettings,
            double rowToleranceMm,
            RowAlignment rowAlignment,
            BlockAlignmentH columnAlignH,
            BlockAlignmentV columnAlignV,
            RowFillStrategy fillStrategy,
            LeftoverGroupMode leftoverGroupMode,
            double columnToleranceMm,
            BlockAlignmentH withinColumnAlign,
            bool enablePriorityGroups,
            List<ViewType> priorityOrder,
            AlignDistributeSettings? alignDistribute,
            PlaceableRegion? region,
            ObservableCollection<LogEntry> log)
        {
            var result = new RunResult { TotalViews = allItems.Count };

            if (region == null)
            {
                result.Success = false;
                result.ErrorMessage = "No title block detected on this sheet (or its geometry could not be read) — Run skipped, no changes made.";
                log.Add(new LogEntry(LogLevel.Warning, result.ErrorMessage));
                return result;
            }

            var ticked = allItems.Where(i => i.IsChecked).ToList();
            var unticked = allItems.Where(i => !i.IsChecked).ToList();
            result.Selected = ticked.Count;

            if (overflowMode != OverflowHandlingMode.PlaceWhatsPlaceable)
            {
                log.Add(new LogEntry(LogLevel.Warning,
                    $"Overflow mode '{overflowMode}' is not yet implemented — falling back to Place What's Placeable."));
                overflowMode = OverflowHandlingMode.PlaceWhatsPlaceable;
            }

            log.Add(new LogEntry(LogLevel.Info, $"Row fill strategy: {fillStrategy}."));

            if (enablePriorityGroups && priorityOrder.Count > 0)
                log.Add(new LogEntry(LogLevel.Info, $"Priority groups enabled — placement order: {string.Join(" → ", priorityOrder)}."));

            log.Add(new LogEntry(LogLevel.Info,
                $"Run started — {ticked.Count} selected, {unticked.Count} to remove."));

            using var tx = new Transaction(doc, "Sheet Auto Rearrange");
            try
            {
                var txStatus = tx.Start();
                if (txStatus != TransactionStatus.Started)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Transaction failed to start (status: {txStatus}).";
                    log.Add(new LogEntry(LogLevel.Error, result.ErrorMessage));
                    return result;
                }

                foreach (var item in unticked)
                {
                    ToolCancellation.CheckCurrent();
                    try
                    {
                        doc.Delete(item.ViewportId);
                        item.FitStatus = ViewFitStatus.NotSelected;
                        result.Removed++;
                        log.Add(new LogEntry(LogLevel.Info, $"Removed from sheet: {item.ViewName} (viewport {item.ViewportId.Value})."));
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // user cancel: never swallowed as a failure
                    }
                    catch (Exception ex)
                    {
                        log.Add(new LogEntry(LogLevel.Warning, $"Could not remove '{item.ViewName}': {ex.Message}"));
                    }
                }

                var packNotes = new List<PackNote>();
                List<PackedViewPlacement> placements = _sheetOrderService.Pack(
                    ticked, region, gapSettings,
                    rowToleranceMm, rowAlignment, columnAlignH, columnAlignV,
                    fillStrategy, leftoverGroupMode, columnToleranceMm, withinColumnAlign,
                    enablePriorityGroups, priorityOrder, alignDistribute, packNotes);

                foreach (var note in packNotes)
                    log.Add(new LogEntry(note.IsWarning ? LogLevel.Warning : LogLevel.Info, note.Message));

                log.Add(new LogEntry(LogLevel.Info, $"Packing complete — {placements.Count(p => p.Fits)} fit, {placements.Count(p => !p.Fits)} overflow."));

                foreach (var placement in placements)
                {
                    ToolCancellation.CheckCurrent();
                    try
                    {
                        if (doc.GetElement(placement.Item.ViewportId) is Viewport vp)
                        {
                            XYZ currentCenter = vp.GetBoxCenter();
                            XYZ delta = placement.NewCenter - currentCenter;
                            ElementTransformUtils.MoveElement(doc, vp.Id, delta);

                            if (placement.Fits)
                            {
                                placement.Item.FitStatus = ViewFitStatus.Fits;
                                result.PlacedSuccessfully++;
                                log.Add(new LogEntry(LogLevel.Success, $"Placed: {placement.Item.ViewName}."));
                            }
                            else
                            {
                                placement.Item.FitStatus = ViewFitStatus.Overflow;
                                result.FailedToFit++;
                                log.Add(new LogEntry(LogLevel.Warning, $"'{placement.Item.ViewName}' does not fit — placed below the sheet (Place What's Placeable)."));
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // user cancel: never swallowed as a failure
                    }
                    catch (Exception ex)
                    {
                        placement.Item.FitStatus = ViewFitStatus.Overflow;
                        result.FailedToFit++;
                        log.Add(new LogEntry(LogLevel.Error, $"Failed to place '{placement.Item.ViewName}': {ex.Message}"));
                    }
                }

                var commitStatus = tx.Commit();
                if (commitStatus != TransactionStatus.Committed)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Transaction failed to commit (status: {commitStatus}).";
                    log.Add(new LogEntry(LogLevel.Error, result.ErrorMessage));
                    return result;
                }

                result.Success = true;
                log.Add(new LogEntry(LogLevel.Success,
                    $"Run complete — {result.PlacedSuccessfully} placed | {result.Removed} removed | {result.FailedToFit} failed."));
                return result;
            }
            catch (OperationCanceledException)
            {
                throw; // user cancel: never swallowed as a failure
            }
            catch (Exception ex)
            {
                if (tx.HasStarted() && !tx.HasEnded())
                    tx.RollBack();

                result.Success = false;
                result.ErrorMessage = ex.Message;
                log.Add(new LogEntry(LogLevel.Error, $"Run failed, rolled back: {ex.Message}"));
                return result;
            }
        }
    }
}
