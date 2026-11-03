using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorsAndRoofFromLinkedRoomsViaPlanView.V005
{
    [Transaction(TransactionMode.Manual)]
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
            catch (Exception ex)
            {
                Logger.Error("Command", ex);
                message = ex.Message;
                TaskDialog.Show("Floors and Roofs From Linked Rooms", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;

            if (doc.ActiveView is not ViewPlan planView)
            {
                TaskDialog.Show(
                    "Floors and Roofs From Linked Rooms",
                    "This tool only runs from a plan view. Open a floor plan and try again.");
                return Result.Cancelled;
            }

            if (planView.GenLevel == null)
            {
                TaskDialog.Show(
                    "Floors and Roofs From Linked Rooms",
                    "This plan view has no associated level (e.g. an area plan or certain site/structural plans aren't supported). Open a standard floor plan and try again.");
                return Result.Cancelled;
            }

            var handler = new RunCreateElementsExternalEventHandler();
            var externalEvent = ExternalEvent.Create(handler);

            var viewModel = new MainViewModel(doc, planView, handler, externalEvent);
            handler.ViewModel = viewModel;

            var window = new FloorsFromLinkedRoomsWindow { DataContext = viewModel };
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show();

            return Result.Succeeded;
        }
    }
}
