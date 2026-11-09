// =======================================================
// File: RoofDetailLineIntersectHandler.cs
// Location: Infrastructure/ExternalEvents/
// Changes vs V008: previously a private inner class on the ViewModel,
// holding Document/FootPrintRoof/List&lt;DetailLine&gt; directly. Now a
// standalone Handler matching every other tool's Infrastructure/
// ExternalEvents convention — the roof and detail lines are re-resolved
// from ElementIds each run (RoofDetailLineIntersectEngine.Execute already
// opens its own single Transaction with correct rollback on failure, so
// no outer TransactionGroup is needed here).
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RoofDetailLineIntersect.V014.Core.Engine;
using Revit26_Plugin_Revised.RoofDetailLineIntersect.V014.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofDetailLineIntersect.V014.Infrastructure.ExternalEvents
{
    public class RoofDetailLineIntersectHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static RoofDetailLineIntersectPayload Payload;

        public void Execute(UIApplication app) =>
            ToolGuard.RunHandler(GetType(), app, ExecuteCore);

        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;

            RoofDetailLineIntersectPayload current = Payload;
            Document doc = app.ActiveUIDocument.Document;

            // Esc / Cancel with a Yes-No confirm; the engine's single transaction rolls back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.RoofDetailLineIntersect, app);
            try
            {
                var roof = doc.GetElement(current.RoofId) as FootPrintRoof;
                if (roof == null)
                {
                    const string msg = "Roof element not found (it may have been deleted).";
                    current.Log?.Invoke(new LogEntry(LogLevel.Error, msg));
                    current.OnCompleted?.Invoke(new RoofDetailLineIntersectResult { Success = false, ErrorMessage = msg });
                    return;
                }

                List<DetailLine> detailLines = current.DetailLineIds
                    .Select(id => doc.GetElement(id) as DetailLine)
                    .Where(dl => dl != null)
                    .ToList();

                var result = RoofDetailLineIntersectEngine.Execute(
                    doc, roof, detailLines, current.Log, current.IncludeLineToLineIntersections);
                current.OnCompleted?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Warning, "Cancelled by user. Nothing was changed."));
                current.OnCompleted?.Invoke(new RoofDetailLineIntersectResult
                {
                    Success = false,
                    ErrorMessage = "Cancelled by user."
                });
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Error,
                    $"[RoofDetailLineIntersectHandler] Unhandled exception: {ex.Message}"));
                current.OnCompleted?.Invoke(new RoofDetailLineIntersectResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        public string GetName() => "Roof Detail Line Intersect Handler";
    }
}
