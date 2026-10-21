using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.WorksetManager.V014.UI.ViewModels;
using Revit26_Plugin_Revised.WorksetManager.V014.UI.Views;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Shared.Services;
using Revit26_Plugin_Revised.Utilities;

namespace Revit26_Plugin_Revised.WorksetManager.V014.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorksetManagerCommand : IExternalCommand
    {
        /// <summary>Opens the Workset Manager window; the body runs through ToolGuard.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteInternal);

        private static Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
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

