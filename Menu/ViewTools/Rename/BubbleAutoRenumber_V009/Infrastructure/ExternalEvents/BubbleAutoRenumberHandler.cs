using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Models;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Core.Services;
using Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.BubbleAutoRenumber.V009.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Does the model mutation on Revit's API thread: re-reads the sheet, plans the
    /// run through Core, and writes it inside one transaction.
    ///
    /// CHANGED in V009:
    /// - Routes through ToolGuard.RunHandler, so a failure is logged to
    ///   %AppData%\Revit26_Plugin_Revised\Logs and shown in the standard error dialog instead
    ///   of being folded into a log line, as V008's own try/catch did.
    /// - Re-reads the sheet here rather than trusting what the window displayed. The
    ///   preview can be minutes old; the plan that gets written is built from the model
    ///   as it is at the moment of the run.
    /// - Reports progress back as it goes, which is what finally drives the shell's
    ///   status strip (V008 left IsRunning / Progress / SummaryText unset).
    /// </summary>
    internal class BubbleAutoRenumberHandler : IExternalEventHandler
    {
        /// <summary>Set by the ViewModel immediately before Raise().</summary>
        public RenumberRequest Request { get; set; }

        /// <summary>Called once per run, on the Revit thread — always, including on failure,
        /// so the window never stays locked behind a run that threw.</summary>
        public Action<RenumberSummary> Completed { get; set; }

        /// <summary>(done, total) across both write passes.</summary>
        public Action<int, int> ProgressChanged { get; set; }

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(typeof(BubbleAutoRenumberHandler), app, Run);

        public string GetName() => "Bubble Auto Renumber V009";

        private void Run(UIApplication app)
        {
            RenumberRequest request = Request;

            if (request?.Sheet == null || request.Options == null) return;

            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null) return;

            var summary = new RenumberSummary
            {
                SheetNumber = request.Sheet.SheetNumber,
                SheetName = request.Sheet.Name
            };

            try
            {
                var rows = ViewportReader.Read(doc, request.Sheet);
                summary.OnSheet = rows.Count;

                RenumberPlan plan = RenumberPlanner.Plan(rows, request.Options);
                summary.InScope = plan.Assignments.Count + plan.Skips.Count;

                if (plan.Assignments.Count == 0)
                {
                    // Nothing writable in scope — skips are still worth reporting, and
                    // opening a transaction that changes nothing would add an empty
                    // undo step.
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

                    return;
                }

                // One transaction for both passes = one Ctrl+Z for the whole run.
                using var transaction = new Transaction(doc, "Bubble Auto Renumber");
                transaction.Start();

                DetailNumberWriter.Apply(doc, plan, summary, ProgressChanged);

                transaction.Commit();
            }
            catch (Exception ex)
            {
                // Recorded so the window can show it, then rethrown so ToolGuard logs it
                // and tells the user. The transaction's using block has already rolled
                // back by the time this is reached.
                summary.ErrorMessage = ex.Message;
                throw;
            }
            finally
            {
                Completed?.Invoke(summary);
            }
        }
    }
}
