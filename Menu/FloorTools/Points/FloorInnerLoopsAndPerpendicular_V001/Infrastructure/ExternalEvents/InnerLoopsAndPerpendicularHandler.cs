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
                using ToolCancellation analyzeCancel = ToolCancellation.BeginFor(ToolCatalog.FloorInnerLoopsAndPerpendicular, app);
                try
                {
                    var result = InnerLoopsAndPerpendicularEngine.Execute(app, current);
                    current.OnCompleted?.Invoke(result);
                }
                catch (OperationCanceledException)
                {
                    current.Log?.Invoke(new LogEntry(LogLevel.Warning, "Cancelled by user."));
                    current.OnCompleted?.Invoke(new InnerLoopsAndPerpendicularResult
                    {
                        Success = false,
                        ErrorMessage = "Cancelled by user.",
                        Operation = current.Operation
                    });
                    throw; // ToolGuard treats it as a user cancel
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

            // Esc / Cancel with a Yes-No confirm; the transaction group below is rolled back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.FloorInnerLoopsAndPerpendicular, app);

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
                catch (OperationCanceledException)
                {
                    if (tg.GetStatus() == TransactionStatus.Started) tg.RollBack();
                    current.Log?.Invoke(new LogEntry(LogLevel.Warning, "Cancelled by user. Nothing was changed."));
                    current.OnCompleted?.Invoke(new InnerLoopsAndPerpendicularResult
                    {
                        Success = false,
                        ErrorMessage = "Cancelled by user.",
                        Operation = current.Operation
                    });
                    throw; // ToolGuard treats it as a user cancel
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
