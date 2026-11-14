// =======================================================
// File: MultiplePointsHandler.cs
// Location: Infrastructure/ExternalEvents/
// Apply modifies the document, so it's wrapped in a TransactionGroup so
// a failure rolls back cleanly — EdgePointService still opens its own
// inner Transaction, the standard nested Transaction/TransactionGroup
// pattern used across this suite.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.MultiplePoints.V003.Core.Engine;
using Revit26_Plugin_Revised.MultiplePoints.V003.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.MultiplePoints.V003.Infrastructure.ExternalEvents
{
    public class MultiplePointsHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static MultiplePointsPayload Payload;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;

            MultiplePointsPayload current = Payload;

            // Esc / Cancel with a Yes-No confirm; the transaction group below is rolled back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.MultiplePoints, app);

            using (TransactionGroup tg = new TransactionGroup(
                app.ActiveUIDocument.Document, "Multiple Edge Points"))
            {
                tg.Start();

                try
                {
                    var result = MultiplePointsEngine.Execute(app, current);

                    if (result.Success)
                        tg.Assimilate();
                    else
                        tg.RollBack();

                    current.OnCompleted?.Invoke(result);
                }
                catch (OperationCanceledException)
                {
                    if (tg.GetStatus() == TransactionStatus.Started) tg.RollBack();
                    current.Log?.Invoke(new LogEntry(LogLevel.Warning, "Cancelled by user. Nothing was changed."));
                    current.OnCompleted?.Invoke(new MultiplePointsResult
                    {
                        Success = false,
                        ErrorMessage = "Cancelled by user."
                    });
                    throw; // ToolGuard treats it as a user cancel
                }
                catch (Exception ex)
                {
                    tg.RollBack();
                    current.Log?.Invoke(new LogEntry(LogLevel.Error,
                        $"[MultiplePointsHandler] Unhandled exception: {ex.Message}"));
                    current.OnCompleted?.Invoke(new MultiplePointsResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }
        }

        public string GetName() => "Multiple Edge Points Handler";
    }
}
