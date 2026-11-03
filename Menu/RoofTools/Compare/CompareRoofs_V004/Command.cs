using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofPointElevationSync.V004
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            // Use roofs already selected in the model (pre-command selection) so the user
            // doesn't have to pick again once the window is open.
            var preselected = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<RoofBase>()
                .Take(2)
                .ToList();

            RoofBase roofA = preselected.Count >= 1 ? preselected[0] : null;
            RoofBase roofB = preselected.Count >= 2 ? preselected[1] : null;

            // Prompt for whichever roof is still missing, before the window ever appears.
            try
            {
                if (roofA == null)
                {
                    var refA = uiDoc.Selection.PickObject(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        new RoofSelectionFilter(),
                        "Select Roof A");
                    roofA = doc.GetElement(refA) as RoofBase;
                }

                if (roofB == null)
                {
                    var refB = uiDoc.Selection.PickObject(
                        Autodesk.Revit.UI.Selection.ObjectType.Element,
                        new RoofSelectionFilter(),
                        "Select Roof B");
                    roofB = doc.GetElement(refB) as RoofBase;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User cancelled a pick — open the window anyway so they can pick manually with
                // the Roof A / Roof B buttons instead.
            }

            try
            {
                var window = new MainWindow(uiDoc, roofA, roofB) { Owner = null };
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("CompareRoofs", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
