// =======================================================
// File: InnerLoopDividerHandler.cs
// Location: Infrastructure/ExternalEvents/
// Analyze is read-only (no transaction). Apply runs inside a
// TransactionGroup; ShapePointWriter opens the one inner Transaction.
// Changes vs V011: the group is opened on the floor's own document (V011
// used the active document), and Payload is cleared after use so a stray
// Raise() can't replay an old request.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Engine;
using Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Core.Models;
using Revit26_Plugin_Revised.Shared.Models;
using System;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Infrastructure.ExternalEvents
{
    public class InnerLoopDividerHandler : IExternalEventHandler
    {
        /// <summary>Set by the ViewModel immediately before raising the ExternalEvent.</summary>
        public static InnerLoopDividerPayload Payload;

        /// <summary>Revit entry point; delegates to ToolGuard.RunHandler.</summary>

        public void Execute(UIApplication app) =>

            ToolGuard.RunHandler(GetType(), app, ExecuteCore);


        private void ExecuteCore(UIApplication app)
        {
            InnerLoopDividerPayload current = Payload;
            Payload = null;
            if (current == null) return;

            if (current.Operation == InnerLoopDividerOperation.Analyze)
            {
                Run(app, current, null);
                return;
            }

            Document doc = InnerLoopDividerEngine.ResolveDocument(app, current);
            if (doc == null)
            {
                Run(app, current, null); // engine reports the closed document
                return;
            }

            using (var tg = new TransactionGroup(doc, "Inner Loop Divider"))
            {
                tg.Start();
                Run(app, current, tg);
            }
        }

        private static void Run(UIApplication app, InnerLoopDividerPayload current, TransactionGroup tg)
        {
            InnerLoopDividerResult result;
            try
            {
                result = InnerLoopDividerEngine.Execute(app, current);
                if (tg != null)
                {
                    if (result.Success) tg.Assimilate();
                    else tg.RollBack();
                }
            }
            catch (Exception ex)
            {
                if (tg != null && tg.GetStatus() == TransactionStatus.Started)
                    tg.RollBack();

                current.Log?.Invoke(new LogEntry(LogLevel.Error, $"[InnerLoopDividerHandler] Unhandled exception: {ex.Message}"));
                result = new InnerLoopDividerResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Operation = current.Operation,
                    PointsAdded = -1
                };
            }

            current.OnCompleted?.Invoke(result);
        }

        public string GetName() => "Inner Loop Divider Handler";
    }
}
