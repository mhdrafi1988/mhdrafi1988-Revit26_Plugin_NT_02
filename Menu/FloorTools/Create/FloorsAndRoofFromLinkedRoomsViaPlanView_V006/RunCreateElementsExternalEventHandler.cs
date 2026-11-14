using System;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRoomsViaPlanView.V006
{
    /// <summary>Runs either a floor-creation pass or a roof-creation pass, depending on
    /// which button the ViewModel set on CreateRunRequest.Mode. Same per-room
    /// dedupe/trim/validate boundary logic feeds both.
    ///
    /// V005 fixes (carried forward from V011):
    /// - Both doc.Regenerate() calls removed (Revit regenerates on commit; faster runs).
    /// - FIX: processed count now advances on EVERY room outcome (success, failure,
    ///   exception) — the previous continue on invalid boundaries skipped ReportProgress,
    ///   so the progress bar undercounted on failed rooms.
    /// - FIX: roof inner-loop count now combines BOTH skip sources — valid inner loops
    ///   (unsupported by NewFootPrintRoof) AND loops that failed validation.
    /// - Cancel now records how many rooms were never reached (RunSummary.NotProcessedCount);
    ///   already-created elements are kept.</summary>
    public class RunCreateElementsExternalEventHandler : IExternalEventHandler
    {
        public MainViewModel ViewModel { get; set; }
        public CreateRunRequest PendingRequest { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            var doc = app.ActiveUIDocument.Document;
            var request = PendingRequest;
            if (request == null) return;

            // Esc / Cancel with a Yes-No confirm; a cancel undoes every element this run created.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.FloorsAndRoofFromLinkedRoomsViaPlanView, app);

            bool isRoof = request.Mode == CreationMode.Roof;
            string verb = isRoof ? "roof" : "floor";

            var summary = new RunSummary();
            bool wasCancelled = false;
            int total = request.Rooms.Count;
            int processed = 0;

            ViewModel.AddLog(LogLevel.Info,
                $"Run started — mode: {request.Mode}, {total} room(s), type id {request.TypeId.Value}.");

            using var tx = new Transaction(doc, isRoof ? "Create roofs from linked rooms" : "Create floors from linked rooms");
            var failureOptions = tx.GetFailureHandlingOptions();
            failureOptions.SetFailuresPreprocessor(new FloorFailuresPreprocessor());
            tx.SetFailureHandlingOptions(failureOptions);
            tx.Start();

            try
            {
            foreach (var candidate in request.Rooms)
            {
                ToolCancellation.CheckCurrent(); // outside the per-room try, so a cancel is never counted as a failed room

                using (var subTx = new SubTransaction(doc))
                {
                    subTx.Start();
                    try
                    {
                        var boundary = RoomBoundaryService.BuildLoops(candidate.RoomElement, request.LinkTransform);

                        if (!boundary.OuterValid)
                        {
                            subTx.RollBack();
                            summary.FailedCount++;
                            ViewModel.AddLog(LogLevel.Warning, $"{candidate.DisplayName} — skipped: {boundary.FailureReason}");
                        }
                        else
                        {
                            if (isRoof)
                                RoofCreationService.Create(doc, boundary.Loops[0], request.TypeId, request.TargetLevel);
                            else
                                FloorCreationService.Create(doc, boundary.Loops, request.TypeId, request.TargetLevel);

                            subTx.Commit();
                            summary.SuccessCount++;

                            if (boundary.WasTrimmedOrFixed)
                            {
                                summary.TrimmedFixedCount++;
                                ViewModel.AddLog(LogLevel.Warning, $"{candidate.DisplayName} — {verb} created (boundary trimmed/fixed)");
                            }
                            else
                            {
                                ViewModel.AddLog(LogLevel.Success, $"{candidate.DisplayName} — {verb} created");
                            }

                            // Inner-loop accounting: for roofs, BOTH valid inner loops
                            // (unsupported by NewFootPrintRoof) AND validation-failed loops
                            // are skipped; for floors, only validation-failed ones are.
                            int skippedInner = isRoof
                                ? (boundary.Loops.Count - 1) + boundary.InnerLoopsSkipped
                                : boundary.InnerLoopsSkipped;

                            if (skippedInner > 0)
                            {
                                summary.InnerLoopsSkippedCount += skippedInner;
                                ViewModel.AddLog(LogLevel.Warning, isRoof
                                    ? $"{candidate.DisplayName} — {skippedInner} inner loop(s) not supported for roofs, outer boundary used"
                                    : $"{candidate.DisplayName} — {skippedInner} inner loop(s) skipped, outer boundary used");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (subTx.GetStatus() == TransactionStatus.Started)
                            subTx.RollBack();

                        summary.FailedCount++;
                        ViewModel.AddLog(LogLevel.Warning, $"{candidate.DisplayName} — skipped: {ex.Message}");
                    }
                }

                // Every room outcome advances the progress count — no continue paths bypass this.
                processed++;
                ViewModel.ReportProgress(processed);

            }

            ToolCancellation.CheckCurrent(); // last chance to cancel before the commit
            var status = tx.Commit();
            if (status != TransactionStatus.Committed)
            {
                ViewModel.AddLog(LogLevel.Error, $"Transaction did not commit cleanly: {status}");
                ViewModel.ShowCriticalError(
                    $"The transaction ended with status '{status}' instead of committing. " +
                    "Check the model for overlapping or invalid geometry and try again.");
            }

            }
            catch (OperationCanceledException)
            {
                // Undo-all: the transaction is rolled back, so no element of this run stays.
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                var cancelledSummary = new RunSummary { NotProcessedCount = total };
                ViewModel.AddLog(LogLevel.Warning,
                    $"Run cancelled by user: {summary.SuccessCount} of {total} room(s) had been processed; all of them were undone.");
                ViewModel.OnRunComplete(request.Mode, cancelledSummary, true);
                throw; // ToolGuard treats it as a user cancel
            }

            ViewModel.OnRunComplete(request.Mode, summary, wasCancelled);
        }

        public string GetName() => "Floors and Roofs From Linked Rooms — run handler";
    }
}
