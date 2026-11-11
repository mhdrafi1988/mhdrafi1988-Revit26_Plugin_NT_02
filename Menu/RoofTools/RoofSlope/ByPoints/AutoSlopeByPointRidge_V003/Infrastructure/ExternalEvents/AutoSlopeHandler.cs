// =======================================================
// File: AutoSlopeHandler.cs
// Location: Infrastructure/ExternalEvents/
// Changes vs original:
//   ADDED    try/catch around AutoSlopeEngine.Execute
//   ADDED    Payload.OnCompleted(false) on unhandled exception
//            so the ViewModel always receives a response and
//            can reset HasRun to allow the user to retry.
//   NOTE     Static Payload field is kept — this is the
//            standard Revit IExternalEventHandler pattern;
//            the handler instance is created once at startup
//            by AutoSlopeEventManager and cannot accept
//            constructor parameters after creation.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.Core.Engine;
using Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.Infrastructure.ExternalEvents
{
    public class AutoSlopeHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static AutoSlopePayload Payload;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;

            // Capture local ref so Payload can be cleared/overwritten safely
            // by the next invocation while this one is still running.
            AutoSlopePayload current = Payload;

            // Esc / Cancel with a Yes-No confirm; the engine reports the cancel to the window, then the group is rolled back.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.AutoSlopeByPointRidge, app);

            using (TransactionGroup tg = new TransactionGroup(
                app.ActiveUIDocument.Document, "AutoSlope"))
            {
                tg.Start();

                try
                {
                    AutoSlopeEngine.Execute(app, current);
                    tg.Assimilate();
                }
                catch (OperationCanceledException)
                {
                    tg.RollBack(); // the engine already told the window; ToolGuard treats this as a user cancel
                    throw;
                }
                catch (Exception ex)
                {
                    tg.RollBack();

                    // Always notify the ViewModel so it can reset HasRun
                    // and re-enable the Run button for a retry.
                    current.Log?.Invoke(new LogEntry(LogLevel.Error,
                        $"[AutoSlopeHandler] Unhandled exception: {ex.Message}"));
                    current.OnCompleted?.Invoke(new AutoSlopeResult
                    {
                        Success      = false,
                        ErrorMessage = $"Unhandled exception: {ex.Message}"
                    });
                }
            }
        }

        public string GetName() => "AutoSlope Handler";
    }
}
