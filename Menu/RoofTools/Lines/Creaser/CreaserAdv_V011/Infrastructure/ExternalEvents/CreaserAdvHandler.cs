// =======================================================
// File: CreaserAdvHandler.cs
// Location: Infrastructure/ExternalEvents/
// Mirrors InnerLoopDividerHandler's pattern — Run() mutates the document,
// so it's wrapped in a TransactionGroup so a failure rolls back cleanly.
// CreaserAdvEngine opens its own inner Transaction only for the placement
// step (standard nested Transaction/TransactionGroup pattern).
//
// plugin 6.16.1:
//   FIX  Execute runs through ToolGuard again (lost in V011). Before, an
//        error outside the inner try (no project open, the transaction
//        group failing to start, or the window's callback failing inside
//        the catch) went straight into Revit.
//   FIX  Refuses to run when the active project is not the one the window
//        was opened for (it used to run the old ids against the new one).
//   FIX  The window is told the result exactly once, and a failure there is
//        logged instead of thrown, so Run is never left locked.
// =======================================================

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.CreaserAdv.V011.Core.Models;
using Revit26_Plugin_Revised.Shared.Services;
using System;

namespace Revit26_Plugin_Revised.CreaserAdv.V011.Infrastructure.ExternalEvents
{
    /// <summary>
    /// Runs one Creaser Adv request (<see cref="Payload"/>) in Revit's API context and
    /// reports the result back to the window through <see cref="CreaserAdvPayload.OnCompleted"/>.
    /// </summary>
    public class CreaserAdvHandler : IExternalEventHandler
    {
        /// <summary>
        /// Set by the ViewModel immediately before raising the ExternalEvent.
        /// Static because IExternalEventHandler instances are created once by
        /// Revit and cannot receive constructor arguments per-invocation.
        /// </summary>
        public static CreaserAdvPayload Payload;

        /// <summary>
        /// Runs the pending request through <see cref="ToolGuard.RunHandler"/>, so no exception reaches Revit.
        /// </summary>
        public void Execute(UIApplication app) => ToolGuard.RunHandler(GetType(), app, ExecuteUnguarded);

        /// <summary>Name Revit shows for this external event.</summary>
        public string GetName() => "Creaser Adv Handler";

        private static void ExecuteUnguarded(UIApplication app)
        {
            // Take the payload and clear the slot so a stray second Raise() can
            // never re-run a request that has already been handled.
            CreaserAdvPayload current = Payload;
            Payload = null;

            if (current == null) return;

            CreaserAdvResult result = null;
            try
            {
                result = Run(app, current);
            }
            catch (Exception ex)
            {
                // The transaction group is already rolled back by its using block.
                current.Log?.Error($"[CreaserAdvHandler] Unexpected {ex.GetType().Name}: {ex.Message}");
                current.Log?.Debug(ex.StackTrace ?? "(no stack trace)");
                result = Fail(ex.Message);
                throw; // ToolGuard writes the error log and shows the error
            }
            finally
            {
                Notify(current, result ?? Fail("The run stopped unexpectedly."));
            }
        }

        private static CreaserAdvResult Run(UIApplication app, CreaserAdvPayload payload)
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return Fail("No project is open. Open the project and start Creaser Adv again.");

            Document windowDoc = payload.Document;
            if (windowDoc == null || !windowDoc.IsValidObject)
                return Fail("The project this window was opened for has been closed. Close this window and start Creaser Adv again.");

            if (!windowDoc.Equals(doc))
                return Fail($"The active project is '{doc.Title}', but this window works on '{windowDoc.Title}'. " +
                            "Switch back to that project, or close this window and start Creaser Adv in this one.");

            if (doc.IsReadOnly)
                return Fail($"'{doc.Title}' is read-only, so no detail items can be placed.");

            using (TransactionGroup tg = new TransactionGroup(doc, "Creaser Advanced"))
            {
                tg.Start();

                CreaserAdvResult result = Core.Engine.CreaserAdvEngine.Execute(doc, payload);

                // Nothing placed => nothing to merge; an empty group is rolled back
                // rather than assimilated.
                if (result.Success && result.Created > 0)
                    tg.Assimilate();
                else
                    tg.RollBack();

                return result;
            }
        }

        /// <summary>Hands the result to the window. Never throws, so it is safe in a finally block.</summary>
        private static void Notify(CreaserAdvPayload payload, CreaserAdvResult result)
        {
            try
            {
                payload.OnCompleted?.Invoke(result);
            }
            catch (Exception ex)
            {
                payload.Log?.Error($"[CreaserAdvHandler] Could not report the result to the window: {ex.Message}");
                ToolGuard.Report(typeof(CreaserAdvHandler), ex);
            }
        }

        private static CreaserAdvResult Fail(string message)
            => new CreaserAdvResult { Success = false, ErrorMessage = message };
    }
}
