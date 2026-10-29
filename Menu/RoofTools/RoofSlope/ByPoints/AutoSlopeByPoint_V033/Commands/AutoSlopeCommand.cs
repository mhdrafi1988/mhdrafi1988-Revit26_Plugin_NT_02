// =======================================================
// File: AutoSlopeCommand.cs
// Namespace: Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Commands
// Ribbon string: "Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Commands.AutoSlopeCommand"
// Changes vs V030:
//   - Body runs through ToolGuard.RunCommand (CLAUDE.md: whole body guarded).
//   - If the window is already open it is brought to the front instead of
//     opening a second one (plan 1.1).
//   - A roof that is already selected is used without asking.
//   - Selection filters: only roofs, then only points on that roof (1.2),
//     so V030's "re-run the command" validation dead end is gone.
//   - Shape editing is enabled only when it is off (no needless undo step).
//     ESC during drain picking still rolls back the Enable (the group's
//     using block disposes and rolls back when the cancel propagates).
//   - The window opens after the TransactionGroup, not inside it.
//   - Stale "Init() must be called from OnStartup" comment removed: the
//     ViewModel calls Init(), as everywhere in this repo (ARCHITECTURE §2).
// =======================================================

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Models;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Infrastructure.RoofOps;
using Revit26_Plugin_Revised.AutoSlopeByPoint.V033.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoSlopeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private static Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (AutoSlopeByPointWindow.TryActivateOpenWindow())
                return Result.Succeeded;

            UIApplication app = commandData.Application;
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("Auto Slope By Point", "Open a project first.");
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;

            // ── 1. Roof: use the selection if it is exactly one roof ───────
            RoofBase roof = null;
            ICollection<ElementId> selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 1)
                roof = doc.GetElement(selected.First()) as RoofBase;

            if (roof == null)
            {
                Reference roofRef = uidoc.Selection.PickObject(
                    ObjectType.Element, new RoofSelectionFilter(), "Select the roof to slope");
                roof = doc.GetElement(roofRef) as RoofBase;
            }
            if (roof == null)
            {
                TaskDialog.Show("Auto Slope By Point", "The selected element is not a roof.");
                return Result.Cancelled;
            }

            // ── 2. Shape editing + drain picks in one group ────────────────
            List<Point3> drains;
            using (var group = new TransactionGroup(doc, "AutoSlope Setup"))
            {
                group.Start();

                SlabShapeEditor editor = roof.GetSlabShapeEditor();
                if (editor == null)
                {
                    group.RollBack();
                    TaskDialog.Show("Auto Slope By Point", "This roof type does not support shape editing.");
                    return Result.Failed;
                }

                if (!editor.IsEnabled)
                {
                    using (var tx = new Transaction(doc, "Enable Shape Editing"))
                    {
                        tx.Start();
                        editor.Enable();
                        tx.Commit();
                    }

                    if (!roof.GetSlabShapeEditor().IsEnabled)
                    {
                        group.RollBack();
                        TaskDialog.Show("Auto Slope By Point",
                            "Shape editing could not be turned on. The roof type may not support it.");
                        return Result.Failed;
                    }
                }

                // ESC throws OperationCanceledException: the using block rolls the
                // group back (undoing Enable) and ToolGuard returns Cancelled.
                IList<Reference> picks = uidoc.Selection.PickObjects(
                    ObjectType.PointOnElement,
                    new PointOnRoofFilter(roof.Id),
                    "Pick drain points on the roof, then click Finish");

                drains = picks.Where(r => r?.GlobalPoint != null).Select(r => r.GlobalPoint.ToPoint3()).ToList();
                if (drains.Count == 0)
                {
                    group.RollBack();
                    TaskDialog.Show("Auto Slope By Point", "No drain points were picked.");
                    return Result.Cancelled;
                }

                group.Assimilate();
            }

            // ── 3. Window (modeless, owned by Revit) ───────────────────────
            var window = new AutoSlopeByPointWindow(app, roof, drains);
            window.Show();
            return Result.Succeeded;
        }
    }
}
