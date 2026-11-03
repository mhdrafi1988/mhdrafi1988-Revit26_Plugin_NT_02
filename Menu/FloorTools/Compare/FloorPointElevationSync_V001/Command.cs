using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorPointElevationSync.V001
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            // Use floors already selected in the model (pre-command selection) so the user
            // doesn't have to pick again once the window is open.
            var preselected = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Floor>()
                .Take(2)
                .ToList();

            Floor floorA = preselected.Count >= 1 ? preselected[0] : null;
            Floor floorB = preselected.Count >= 2 ? preselected[1] : null;

            // Prompt for whichever floor is still missing, before the window ever appears.
            try
            {
                if (floorA == null)
                {
                    var refA = uiDoc.Selection.PickObject(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        new FloorSelectionFilter(),
                        "Select Floor A");
                    floorA = doc.GetElement(refA) as Floor;
                }

                if (floorB == null)
                {
                    var refB = uiDoc.Selection.PickObject(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        new FloorSelectionFilter(),
                        "Select Floor B");
                    floorB = doc.GetElement(refB) as Floor;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User cancelled a pick — open the window anyway so they can pick manually with
                // the Floor A / Floor B buttons instead.
            }

            try
            {
                var window = new MainWindow(uiDoc, floorA, floorB) { Owner = null };
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("CompareFloors", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
