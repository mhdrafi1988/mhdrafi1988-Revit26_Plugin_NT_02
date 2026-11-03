// =======================================================
// File: MultiplePointsCommand.cs
// Location: Commands/
// Picks a floor, extracts its edges synchronously (still inside this
// Execute() call's own valid Revit API context), then shows the window
// modeless per this suite's standing convention — Apply goes through
// MultiplePointsEventManager/Handler (see Infrastructure/ExternalEvents).
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorMultiplePoints.V001.Core.Services;
using Revit26_Plugin_Revised.FloorMultiplePoints.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorMultiplePoints.V001.UI.Views;
using System;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorMultiplePoints.V001.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MultiplePointsCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument    uidoc = uiapp.ActiveUIDocument;
            Document      doc   = uidoc.Document;

            try
            {
                Reference pickedRef = uidoc.Selection.PickObject(
                    ObjectType.Element, new MultiplePointsFloorSelectionFilter(),
                    "Select a floor to add edge points");
                if (pickedRef == null) return Result.Cancelled;

                Floor floor = doc.GetElement(pickedRef) as Floor;
                if (floor == null)
                {
                    TaskDialog.Show("Multiple Points", "Selected element is not a floor.");
                    return Result.Failed;
                }

                // Floor port: refuse floors that can't be changed (group, read-only,
                // borrowed by another user, no shape-editing support).
                string blockReason = FloorShapeEditChecks.GetBlockReason(floor);
                if (blockReason != null)
                {
                    TaskDialog.Show("Floor Multiple Points", $"Floor {floor.Name} (Id {floor.Id.Value}) can't be changed: {blockReason}.");
                    return Result.Cancelled;
                }

                var service = new EdgePointService();
                var initialEdges = service.ExtractEdges(floor);

                var vm = new MultiplePointsViewModel(floor.Id, initialEdges);
                var window = new MultiplePointsWindow { DataContext = vm };
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
                window.Show();

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                Logger.Error("MultiplePointsCommand", ex);
                TaskDialog.Show("Multiple Points — Exception", ex.Message);
                return Result.Failed;
            }
        }
    }

    public class MultiplePointsFloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Floor && (elem.GroupId == null || elem.GroupId == ElementId.InvalidElementId);
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
