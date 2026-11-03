// =======================================================
// File: FloorSlopeByPointRidgeCommand.cs
// Namespace: Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Commands
// Ribbon string: "Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Commands.FloorSlopeByPointRidgeCommand"
// Floor port of Auto Slope By Point (Ridge) V003:
//   - Body runs through ToolGuard.RunCommand.
//   - A floor that is already selected is used without asking; otherwise the
//     user picks a host-model floor that is not in a group, then drain points
//     on it (only points on that floor can be picked).
//   - The floor is checked first (FloorShapeEditChecks): shape-editing
//     support, group, read-only, borrowed by another user.
//   - A floor type with no Variable layer asks Continue / Cancel (shape
//     editing would tilt the whole slab).
//   - Shape editing is enabled only when it is off; ESC during drain picking
//     rolls the Enable back with the TransactionGroup (SlabShapeEditor has no
//     Disable()).
// =======================================================

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Commands
{
    /// <summary>
    /// Floor Slope By Point (Ridge): slopes a floor's shape points down to picked drain points,
    /// lifting the ridges between drain groups so every point drains at the given slope.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorSlopeByPointRidgeCommand : IExternalCommand
    {
        private const string Title = "Floor Slope By Point (Ridge)";

        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private static Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication app = commandData.Application;
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show(Title, "Open a project first.");
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;

            // ── 1. Floor: use the selection if it is exactly one floor ───────
            Floor floor = null;
            ICollection<ElementId> selected = uidoc.Selection.GetElementIds();
            if (selected.Count == 1)
                floor = doc.GetElement(selected.First()) as Floor;

            if (floor == null)
            {
                // ESC throws OperationCanceledException; ToolGuard returns Cancelled.
                Reference floorRef = uidoc.Selection.PickObject(
                    ObjectType.Element, new FloorSelectionFilter(), "Select the floor to slope");
                floor = doc.GetElement(floorRef) as Floor;
            }
            if (floor == null)
            {
                TaskDialog.Show(Title, "The selected element is not a floor.");
                return Result.Cancelled;
            }

            string blockReason = FloorShapeEditChecks.GetBlockReason(floor);
            if (blockReason != null)
            {
                TaskDialog.Show(Title, $"Floor {floor.Name} (Id {floor.Id.Value}) cannot be sloped: {blockReason}.");
                return Result.Cancelled;
            }

            if (!FloorShapeEditChecks.HasVariableLayer(floor) &&
                FloorShapeEditChecks.AskNoVariableLayer(floor, Title, "Cancel") == NoVariableLayerChoice.Skip)
                return Result.Cancelled;

            // ── 2. Shape editing + drain picks in one group ────────────────
            var drains = new List<XYZ>();
            using (var group = new TransactionGroup(doc, "Floor Slope (Ridge) Setup"))
            {
                group.Start();

                SlabShapeEditor editor = floor.GetSlabShapeEditor();
                if (editor == null)
                {
                    group.RollBack();
                    TaskDialog.Show(Title, "This floor type does not support shape editing.");
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

                    if (!floor.GetSlabShapeEditor().IsEnabled)
                    {
                        group.RollBack();
                        TaskDialog.Show(Title,
                            "Shape editing could not be turned on. The floor type may not support it.");
                        return Result.Failed;
                    }
                }

                // ESC throws OperationCanceledException: the using block rolls the
                // group back (undoing Enable) and ToolGuard returns Cancelled.
                IList<Reference> picks = uidoc.Selection.PickObjects(
                    ObjectType.PointOnElement,
                    new PointOnFloorFilter(floor.Id),
                    "Pick drain points on the floor, then click Finish");

                drains.AddRange(picks.Where(r => r?.GlobalPoint != null).Select(r => r.GlobalPoint));
                if (drains.Count == 0)
                {
                    group.RollBack();
                    TaskDialog.Show(Title, "No drain points were picked.");
                    return Result.Cancelled;
                }

                group.Assimilate();
            }

            // ── 3. Window (modeless) ───────────────────────────────────────
            var window = new FloorSlopeByPointRidgeWindow(uidoc, app, floor.Id, drains);
            window.Show();
            return Result.Succeeded;
        }
    }
}
