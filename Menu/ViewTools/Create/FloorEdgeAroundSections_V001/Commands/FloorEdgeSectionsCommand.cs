using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// Entry point: reads the user's pre-launch selection, splits it into
    /// Floor elements vs everything else (skipped, per confirmed spec point 2),
    /// and opens the modeless FloorEdgeSectionsWindow.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorEdgeAroundSectionsCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();

            if (selectedIds.Count == 0)
            {
                TaskDialog.Show("Floor Edge Sections",
                    "Select one or more floors before launching this tool.");
                return Result.Cancelled;
            }

            var allSelected = selectedIds.Select(id => doc.GetElement(id)).ToList();
            var floors = allSelected.Where(e => e is Floor).ToList();
            var nonFloors = allSelected.Where(e => e is not Floor).ToList();

            if (floors.Count == 0)
            {
                TaskDialog.Show("Floor Edge Sections",
                    "No floor elements found in the current selection. Select at least one floor and try again.");
                return Result.Cancelled;
            }

            try
            {
                var handler = new FloorEdgeSectionsEventHandler();
                ExternalEvent externalEvent = ExternalEvent.Create(handler);

                var viewModel = new FloorEdgeSectionsViewModel(doc, externalEvent, handler, floors, nonFloors);

                var window = new FloorEdgeSectionsWindow(viewModel, uiApp, uiApp.MainWindowHandle);
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("FloorEdgeAroundSections", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
