using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// Entry point: no pre-launch selection is required (per confirmed spec) —
    /// the modeless window itself has a "Pick Floors" button. Creates the
    /// ExternalEvent/handler, constructs the ViewModel, and opens the window.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorEdgeElementSectionsCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiApp = commandData.Application;
                UIDocument uiDoc = uiApp.ActiveUIDocument;
                Document doc = uiDoc.Document;

                var handler = new FloorEdgeElementSectionsEventHandler();
                ExternalEvent externalEvent = ExternalEvent.Create(handler);

                var viewModel = new FloorEdgeElementSectionsViewModel(doc, uiDoc, externalEvent, handler);

                var window = new FloorEdgeElementSectionsWindow(viewModel, uiApp, uiApp.MainWindowHandle);
                window.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("FloorEdgeElementSections", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
