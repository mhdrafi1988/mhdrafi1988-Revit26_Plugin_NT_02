using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.RoofTools.LineAndPoints.RoofRidgeLines.V059.Services;
using Revit26_Plugin_Revised.RoofTools.LineAndPoints.RoofRidgeLines.V059.ViewModels;
using Revit26_Plugin_Revised.RoofTools.LineAndPoints.RoofRidgeLines.V059.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofTools.LineAndPoints.RoofRidgeLines.V059.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RoofRidgeCommand : IExternalCommand
    {
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

                // 1. Select Roof
                RoofBase selectedRoof = null;
                try
                {
                    var roofRef = uiDoc.Selection.PickObject(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        new RoofSelectionFilter(),
                        "Select a roof element");
                    selectedRoof = uiDoc.Document.GetElement(roofRef) as RoofBase;
                    if (selectedRoof == null)
                    {
                        message = "Selected element is not a valid roof.";
                        return Result.Failed;
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
                    drainPoints = DrainPointPicker.PickDrainPoints(uiDoc, selectedRoof);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
                catch (Exception ex)
                {
                    Logger.Error("RoofRidgeCommand", ex);
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
                var viewModel = new RoofRidgeViewModel(uiDoc, dispatcher, selectedRoof, drainPoints);
                var view = new RoofRidgeView(viewModel);
                viewModel.SetOwnerWindow(view);
                view.Closed += (_, __) => dispatcher.Dispose();
                view.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("RoofRidgeCommand", ex);
                message = $"Roof Ridge Command failed: {ex.Message}";
                return Result.Failed;
            }
        }

        private class RoofSelectionFilter : Autodesk.Revit.UI.Selection.ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is RoofBase;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
