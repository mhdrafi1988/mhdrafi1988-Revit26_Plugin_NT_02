using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Helpers;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.ViewModels;
using Revit26_Plugin_Revised.AnnotationOverlapDetection.V006.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.AnnotationOverlapDetection.V006
{
    /// <summary>
    /// Annotation Overlap Detection V006: lists overlapping annotations in the active 2D view (plan, section,
    /// elevation, detail, drafting, legend or sheet) and
    /// resolves them (Move Up / Move Down / Distribute). The command itself only opens the modeless
    /// window; the moves run later in <see cref="MoveAnnotationsEventHandler"/>.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command : IExternalCommand
    {
        /// <summary>Entry point called by Revit; delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
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

            // Step 1: view validation - exit cleanly, no error dialog, in an unsupported view.
            // Runs in every 2D view since V005 (V004: floor plans only).
            if (!ViewSupport.IsSupported(activeView))
            {
                TaskDialog.Show("Annotation Overlap Detection",
                    "This view type is not supported. Run the tool from one of these views: " + ViewSupport.SupportedList + ".");
                return Result.Cancelled;
            }

            // External events must be created here in Execute(), not from a UI event handler
            var zoomHandler = new ZoomToElementEventHandler();
            ExternalEvent zoomEvent = ExternalEvent.Create(zoomHandler);

            // Moves clashing annotations (Move Up / Move Down / Distribute / By priority)
            var moveHandler = new MoveAnnotationsEventHandler();
            ExternalEvent moveEvent = ExternalEvent.Create(moveHandler);

            var viewModel = new AnnotationOverlapViewModel(doc, activeView, zoomEvent, zoomHandler, moveEvent, moveHandler);

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
