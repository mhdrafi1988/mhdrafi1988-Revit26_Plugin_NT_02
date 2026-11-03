using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Revit26_Plugin_Revised.FloorPointComparison.V001.Infrastructure.Helpers;
using Revit26_Plugin_Revised.FloorPointComparison.V001.UI.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorPointComparison.V001.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorComparisonCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet elems) =>
            ToolGuard.RunCommand(GetType(), data, ref msg, elems, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData data, ref string msg, ElementSet elems)
        {
            UIDocument uidoc = data.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            var filter = new FloorSelectionFilter();

            Reference refA;
            try
            {
                refA = uidoc.Selection.PickObject(ObjectType.Element, filter, "Select Floor A (first floor to compare)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            Floor floorA = doc.GetElement(refA) as Floor;
            if (floorA == null)
            {
                TaskDialog.Show("Compare Floors", "Selected element is not a valid floor.");
                return Result.Cancelled;
            }

            Reference refB;
            try
            {
                refB = uidoc.Selection.PickObject(ObjectType.Element, filter, "Select Floor B (second floor to compare)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            Floor floorB = doc.GetElement(refB) as Floor;
            if (floorB == null)
            {
                TaskDialog.Show("Compare Floors", "Selected element is not a valid floor.");
                return Result.Cancelled;
            }

            if (floorA.Id == floorB.Id)
            {
                TaskDialog.Show("Compare Floors", "Please select two different floors.");
                return Result.Cancelled;
            }

            try
            {
                var win = new FloorComparisonWindow(uidoc, data.Application, floorA.Id, floorB.Id);
                win.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("FloorPointComparison", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }
    }
}
