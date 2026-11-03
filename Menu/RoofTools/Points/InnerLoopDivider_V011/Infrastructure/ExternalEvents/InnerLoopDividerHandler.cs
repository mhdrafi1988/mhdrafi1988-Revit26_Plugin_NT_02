// =======================================================
// File: InnerLoopDividerHandler.cs
// Location: Infrastructure/ExternalEvents/
// New in V009. Analyze is read-only (no transaction). Apply modifies
// the document, so it's wrapped in a TransactionGroup so a failure
// rolls back cleanly — LoopDivisionService still opens its own inner
// Transaction, the standard nested Transaction/TransactionGroup pattern.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.InnerLoopDivider.V011.Core.Engine;
using Revit26_Plugin_Revised.InnerLoopDivider.V011.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.InnerLoopDivider.V011.Infrastructure.ExternalEvents
{
    public class InnerLoopDividerHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static InnerLoopDividerPayload Payload;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;

            InnerLoopDividerPayload current = Payload;

            if (current.Operation == InnerLoopDividerOperation.Analyze)
            {
                try
                {
                    var result = InnerLoopDividerEngine.Execute(app, current);
                    current.OnCompleted?.Invoke(result);
                }
                catch (Exception ex)
                {
                    current.Log?.Invoke(new LogEntry(LogLevel.Error,
                        $"[InnerLoopDividerHandler] Unhandled exception: {ex.Message}"));
                    current.OnCompleted?.Invoke(new InnerLoopDividerResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message,
                        Operation = current.Operation
                    });
                }
                return;
            }

            using (TransactionGroup tg = new TransactionGroup(
                app.ActiveUIDocument.Document, "Inner Loop Divider"))
            {
                tg.Start();

                try
                {
                    var result = InnerLoopDividerEngine.Execute(app, current);

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
                        $"[InnerLoopDividerHandler] Unhandled exception: {ex.Message}"));
                    current.OnCompleted?.Invoke(new InnerLoopDividerResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message,
                        Operation = current.Operation
                    });
                }
            }
        }

        public string GetName() => "Inner Loop Divider Handler";
    }
}
