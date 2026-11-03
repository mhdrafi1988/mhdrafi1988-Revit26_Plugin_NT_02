// =======================================================
// File: AutoSlopeHandler.cs
// Location: Infrastructure/ExternalEvents/
// Changes vs V028:
//   - Payload type changed from AutoSlopePayload to MultiSlopePayload,
//     and the engine call switched to MultiSlopeVariantEngine.Execute,
//     which owns its own single outer Transaction per floor internally
//     (see MultiSlopeVariantEngine). The outer TransactionGroup here
//     still exists as a top-level safety net for exceptions raised
//     outside that Transaction (e.g. before it opens).
//   NOTE     Static Payload field is kept — this is the
//            standard Revit IExternalEventHandler pattern;
//            the handler instance is created once at startup
//            by AutoSlopeEventManager and cannot accept
//            constructor parameters after creation.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Infrastructure.ExternalEvents
{
    public class AutoSlopeHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static MultiSlopePayload Payload;

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>
        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private static void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;

            // Capture local ref so Payload can be cleared/overwritten safely
            // by the next invocation while this one is still running.
            MultiSlopePayload current = Payload;

            using (TransactionGroup tg = new TransactionGroup(
                app.ActiveUIDocument.Document, "Multi-Slope Floor Variants"))
            {
                tg.Start();

                try
                {
                    MultiSlopeVariantEngine.Execute(app, current);
                    tg.Assimilate();
                }
                catch (Exception ex)
                {
                    tg.RollBack();
                    ToolGuard.Report(typeof(AutoSlopeHandler), ex);

                    // Always notify the ViewModel so it can reset HasRun
                    // and re-enable the Run button for a retry.
                    current.Log?.Invoke(new LogEntry(LogLevel.Error,
                        $"[AutoSlopeHandler] Unhandled exception: {ex.Message}"));
                    current.OnCompleted?.Invoke(new MultiSlopeRunSummary
                    {
                        Success      = false,
                        ErrorMessage = $"Unhandled exception: {ex.Message}"
                    });
                }
            }
        }

        public string GetName() => "Floor Slope By Point (Multi Copies) Handler";
    }
}
