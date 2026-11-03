using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Services;
using Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.ViewModels;
using Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorRidgeCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiDoc = commandData.Application.ActiveUIDocument;
                if (uiDoc == null)
                {
                    message = "No active document found.";
                    return Result.Failed;
                }

                // 1. Select Floor
                Floor selectedFloor = null;
                try
                {
                    var floorRef = uiDoc.Selection.PickObject(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        new FloorSelectionFilter(),
                        "Select a floor element");
                    selectedFloor = uiDoc.Document.GetElement(floorRef) as Floor;
                    if (selectedFloor == null)
                    {
                        message = "Selected element is not a valid floor.";
                        return Result.Failed;
                    }

                    // Floor port: refuse floors that can't be changed (group, read-only,
                    // borrowed by another user, no shape-editing support).
                    string blockReason = FloorShapeEditChecks.GetBlockReason(selectedFloor);
                    if (blockReason != null)
                    {
                        TaskDialog.Show("Floor Crease Lines By Points", $"Floor {selectedFloor.Name} (Id {selectedFloor.Id.Value}) can't be changed: {blockReason}.");
                        return Result.Cancelled;
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                // 2. Select Drain Points
                List<XYZ> drainPoints;
                try
                {
                    drainPoints = DrainPointPicker.PickDrainPoints(uiDoc, selectedFloor);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
                catch (Exception ex)
                {
                    Logger.Error("FloorRidgeCommand", ex);
                    message = $"Drain selection failed: {ex.Message}";
                    return Result.Failed;
                }

                if (drainPoints.Count < 2)
                {
                    message = "At least two drain points are required.";
                    return Result.Failed;
                }

                // 3. Launch UI
                // The window is modeless, so every later Revit API call (transactions,
                // PickObject/PickObjects) is routed through the dispatcher's ExternalEvent,
                // which must be created here while still in API context.
                var dispatcher = new RevitApiDispatcher();
                var viewModel = new FloorRidgeViewModel(uiDoc, dispatcher, selectedFloor, drainPoints);
                var view = new FloorRidgeView(viewModel);
                viewModel.SetOwnerWindow(view);
                view.Closed += (_, __) => dispatcher.Dispose();
                view.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("FloorRidgeCommand", ex);
                message = $"Floor Ridge Command failed: {ex.Message}";
                return Result.Failed;
            }
        }

        private class FloorSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element elem) =>
                elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
