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
using Revit26_Plugin_Revised.FloorMultiplePoints.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorMultiplePoints.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorMultiplePoints.V001.Infrastructure.ExternalEvents
{
    public class MultiplePointsHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static MultiplePointsPayload Payload;

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>

        public void Execute(UIApplication app) =>

            ToolGuard.RunHandler(GetType(), app, ExecuteCore);


        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;

            MultiplePointsPayload current = Payload;

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
