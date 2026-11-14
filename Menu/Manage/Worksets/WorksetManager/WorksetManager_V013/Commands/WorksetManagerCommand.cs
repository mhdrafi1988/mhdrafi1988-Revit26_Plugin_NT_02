using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.WorksetManager.V013.UI.ViewModels;
using Revit26_Plugin_Revised.WorksetManager.V013.UI.Views;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.WorksetManager.V013.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorksetManagerCommand : IExternalCommand
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
                Logger.Error("WorksetManagerCommand", ex);
                message = ex.Message;
                TaskDialog.Show("Workset Manager", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;

            if (doc == null || !doc.IsWorkshared)
            {
                TaskDialog.Show("Workset Manager",
                    "This command requires an active workshared document.");
                return Result.Cancelled;
            }

            var viewModel = new WorksetsViewModel(commandData);
            var window = new WorksetSelectorWindow(viewModel);

            // Parent to Revit's actual main window handle. System.Windows.Application.Current
            // is not guaranteed to exist inside Revit's process, so System.Windows.Application.Current.MainWindow
            // can NullReferenceException, or resolve to the wrong window and break
            // WindowStartupLocation="CenterOwner" / z-order against Revit.
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;

            window.Show();
            return Result.Succeeded;
        }
    }
}

