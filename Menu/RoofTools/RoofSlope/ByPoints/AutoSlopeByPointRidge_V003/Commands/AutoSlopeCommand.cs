// =======================================================
// File: AutoSlopeCommand.cs
// Location: Commands/
// Changes vs 04.00:
//   FIX B1   AutoSlopeEventManager.Init() removed from here.
//            Must be called once in IExternalApplication.OnStartup.
//
//   FIX I5   Drain picks validated: each picked point's host element
//            must be the selected roof.
//
//   FIX ESC  Shape editing is rolled back when the user presses  ESC
//            during drain point selection, or when validation fails.
//            SlabShapeEditor has no Disable() method — the rollback is
//            achieved by wrapping Enable() in a TransactionGroup and
//            calling RollBack() on it so Revit undoes the Enable as well.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.UI.Views;
using System;
using System.Collections.Generic;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AutoSlopeByPointRidge.V003.Commands

{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoSlopeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet elems) =>
            ToolGuard.RunCommand(GetType(), data, ref msg, elems, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData data, ref string msg, ElementSet elems)
        {
            UIDocument uidoc = data.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            // ── 1. Pick the roof ─────────────────────────────────────────────
            Reference roofRef;
            try
            {
                roofRef = uidoc.Selection.PickObject(
                    ObjectType.Element, "Select a footprint roof");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // Nothing modified yet — clean cancel.
                return Result.Cancelled;
            }

            RoofBase roof = doc.GetElement(roofRef) as RoofBase;
            if (roof == null)
            {
                TaskDialog.Show("AutoSlope", "Selected element is not a valid roof.");
                return Result.Cancelled;
            }

            // ── 2 + 3. Enable shape editing AND pick drains inside one group ──
            // Wrapping both steps in a TransactionGroup means that if the user
            // cancels during drain selection (or validation fails), we roll back
            // the entire group — including the Enable() call — leaving the roof
            // exactly as it was before the command ran.
            // SlabShapeEditor has no Disable() method in the Revit API, so a
            // group rollback is the only clean way to undo Enable().
            IList<Reference> picks = null;

            using (TransactionGroup tg = new TransactionGroup(doc, "AutoSlope Setup"))
            {
                tg.Start();

                // Enable shape editing
                using (Transaction tx = new Transaction(doc, "Enable Shape Editing"))
                {
                    tx.Start();
                    try
                    {
                        roof.GetSlabShapeEditor().Enable();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("AutoSlopeCommand", ex);
                        tx.RollBack();
                        tg.RollBack();
                        TaskDialog.Show("AutoSlope",
                            "Cannot enable shape editing:\n" + ex.Message);
                        return Result.Failed;
                    }
                    tx.Commit();
                }

                var editor = roof.GetSlabShapeEditor();
                if (!editor.IsEnabled)
                {
                    tg.RollBack();
                    TaskDialog.Show("AutoSlope",
                        "Shape editing could not be enabled. " +
                        "Roof type may not support shape editing.");
                    return Result.Failed;
                }

                // Pick drain points — ESC here throws OperationCanceledException
                try
                {
                    picks = uidoc.Selection.PickObjects(
                        ObjectType.PointOnElement,
                        "Pick drain points on the roof, then click Finish");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // FIX ESC: rolling back the group also undoes Enable().
                    tg.RollBack();
                    return Result.Cancelled;
                }

                // FIX I5: validate every pick is on the selected roof
                var drains = new List<XYZ>();
                foreach (var r in picks)
                {
                    if (r.ElementId != roof.Id)
                    {
                        tg.RollBack();
                        TaskDialog.Show("AutoSlope",
                            "One or more drain points were not picked on the selected roof.\n" +
                            "Please re-run the command and click only on the roof surface.");
                        return Result.Cancelled;
                    }
                    drains.Add(r.GlobalPoint);
                }

                if (drains.Count == 0)
                {
                    tg.RollBack();
                    TaskDialog.Show("AutoSlope", "No drain points were selected.");
                    return Result.Cancelled;
                }

                // Everything is good — assimilate the group so Enable() is kept.
                tg.Assimilate();

                // ── 4. Open the AutoSlope window ──────────────────────────────
                // NOTE: AutoSlopeEventManager.Init() is NOT called here.
                // It must be called once from IExternalApplication.OnStartup.
                var win = new AutoSlopeByPointWindow(uidoc, data.Application, roof.Id, drains);
                win.Show();
            }

            return Result.Succeeded;
        }
    }
}