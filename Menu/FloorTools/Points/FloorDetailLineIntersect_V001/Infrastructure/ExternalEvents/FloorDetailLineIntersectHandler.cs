// =======================================================
// File: FloorDetailLineIntersectHandler.cs
// Location: Infrastructure/ExternalEvents/
// Changes vs V008: previously a private inner class on the ViewModel,
// holding Document/Floor/List&lt;DetailLine&gt; directly. Now a
// standalone Handler matching every other tool's Infrastructure/
// ExternalEvents convention — the floor and detail lines are re-resolved
// from ElementIds each run (FloorDetailLineIntersectEngine.Execute already
// opens its own single Transaction with correct rollback on failure, so
// no outer TransactionGroup is needed here).
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Infrastructure.ExternalEvents
{
    public class FloorDetailLineIntersectHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static FloorDetailLineIntersectPayload Payload;

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>

        public void Execute(UIApplication app) =>

            ToolGuard.RunHandler(GetType(), app, ExecuteCore);


        private void ExecuteCore(UIApplication app)
        {
            if (Payload == null) return;

            FloorDetailLineIntersectPayload current = Payload;
            Document doc = app.ActiveUIDocument.Document;

            // Esc / Cancel with a Yes-No confirm; the engine's single transaction rolls back on cancel.
            using ToolCancellation cancel = ToolCancellation.BeginFor(ToolCatalog.FloorDetailLineIntersect, app);
            try
            {
                var floor = doc.GetElement(current.FloorId) as Floor;
                if (floor == null)
                {
                    const string msg = "Floor element not found (it may have been deleted).";
                    current.Log?.Invoke(new LogEntry(LogLevel.Error, msg));
                    current.OnCompleted?.Invoke(new FloorDetailLineIntersectResult { Success = false, ErrorMessage = msg });
                    return;
                }

                List<DetailLine> detailLines = current.DetailLineIds
                    .Select(id => doc.GetElement(id) as DetailLine)
                    .Where(dl => dl != null)
                    .ToList();

                var result = FloorDetailLineIntersectEngine.Execute(
                    doc, floor, detailLines, current.Log, current.IncludeLineToLineIntersections);
                current.OnCompleted?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Warning, "Cancelled by user. Nothing was changed."));
                current.OnCompleted?.Invoke(new FloorDetailLineIntersectResult
                {
                    Success = false,
                    ErrorMessage = "Cancelled by user."
                });
                throw; // ToolGuard treats it as a user cancel
            }
            catch (Exception ex)
            {
                current.Log?.Invoke(new LogEntry(LogLevel.Error,
                    $"[FloorDetailLineIntersectHandler] Unhandled exception: {ex.Message}"));
                current.OnCompleted?.Invoke(new FloorDetailLineIntersectResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        public string GetName() => "Floor Detail Line Intersect Handler";
    }
}
