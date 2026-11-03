// =======================================================
// File: InnerLoopsAndPerpendicularHandler.cs
// Location: Infrastructure/ExternalEvents/
// New in V005. Analyze and Generate are read-only (no transaction).
// Apply modifies the document, so it's wrapped in a TransactionGroup so
// a failure rolls back cleanly — PerpendicularPointService still opens
// its own inner Transaction, the standard nested Transaction/
// TransactionGroup pattern.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Infrastructure.ExternalEvents
{
    public class InnerLoopsAndPerpendicularHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static InnerLoopsAndPerpendicularPayload Payload;

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>

        public void Execute(UIApplication app) =>

            ToolGuard.RunHandler(GetType(), app, ExecuteCore);


        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;

            InnerLoopsAndPerpendicularPayload current = Payload;

            if (current.Operation != InnerLoopsAndPerpendicularOperation.Apply)
            {
                try
                {
                    var result = InnerLoopsAndPerpendicularEngine.Execute(app, current);
                    current.OnCompleted?.Invoke(result);
                }
                catch (Exception ex)
                {
                    current.Log?.Invoke(new LogEntry(LogLevel.Error,
                        $"[InnerLoopsAndPerpendicularHandler] Unhandled exception: {ex.Message}"));
                    current.OnCompleted?.Invoke(new InnerLoopsAndPerpendicularResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message,
                        Operation = current.Operation
                    });
                }
                return;
            }

            using (TransactionGroup tg = new TransactionGroup(
                app.ActiveUIDocument.Document, "Inner Loops And Perpendicular"))
            {
                tg.Start();

                try
                {
                    var result = InnerLoopsAndPerpendicularEngine.Execute(app, current);

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
                        $"[InnerLoopsAndPerpendicularHandler] Unhandled exception: {ex.Message}"));
                    current.OnCompleted?.Invoke(new InnerLoopsAndPerpendicularResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message,
                        Operation = current.Operation
                    });
                }
            }
        }

        public string GetName() => "Inner Loops And Perpendicular Handler";
    }
}
