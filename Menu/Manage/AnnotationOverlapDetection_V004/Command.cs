using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V004.ViewModels;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V004.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V004
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return ExecuteInternal(commandData, ref message, elements);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                Logger.Error("Command", ex);
                message = ex.Message;
                TaskDialog.Show("Annotation Overlap Detection", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View activeView = uiDoc.ActiveView;

            // Step 1: view validation - exit cleanly, no error dialog, if not a plan view
            if (activeView.ViewType != ViewType.FloorPlan)
            {
                TaskDialog.Show("Annotation Overlap Detection",
                    "Please run this command from a floor plan view.");
                return Result.Cancelled;
            }

            // External event must be created here in Execute(), not from a UI event handler
            var zoomHandler = new ZoomToElementEventHandler();
            ExternalEvent zoomEvent = ExternalEvent.Create(zoomHandler);

            var viewModel = new AnnotationOverlapViewModel(doc, activeView, zoomEvent, zoomHandler);

            if (viewModel.AnnotationFamilies.Count == 0)
            {
                TaskDialog.Show("Annotation Overlap Detection", "No annotations in this view.");
                return Result.Cancelled;
            }

            var panel = new AnnotationOverlapPanel(viewModel);
            panel.Show(); // modeless, so the user can still interact with the Revit model/view for zoom

            return Result.Succeeded;
        }
    }
}
