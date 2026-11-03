// =======================================================
// File: CurveDividerHandler.cs
// Location: Infrastructure/ExternalEvents/
// New in V004. Apply modifies the document, so it's wrapped in a
// TransactionGroup so a failure rolls back cleanly — CurveDivisionService
// still opens its own inner Transaction, the standard nested
// Transaction/TransactionGroup pattern.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.OuterCurveDivider.V006.Core.Engine;
using Revit26_Plugin_Revised.OuterCurveDivider.V006.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.OuterCurveDivider.V006.Infrastructure.ExternalEvents
{
    public class CurveDividerHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static CurveDividerPayload Payload;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;

            CurveDividerPayload current = Payload;

            using (TransactionGroup tg = new TransactionGroup(
                app.ActiveUIDocument.Document, "Curve Point Divider"))
            {
                tg.Start();

                try
                {
                    var result = CurveDividerEngine.Execute(app, current);

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
                        $"[CurveDividerHandler] Unhandled exception: {ex.Message}"));
                    current.OnCompleted?.Invoke(new CurveDividerResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }
        }

        public string GetName() => "Curve Point Divider Handler";
    }
}
