using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.FloorTypeManager.V001.UI.ViewModels;
using Revit26_Plugin_Revised.FloorTypeManager.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.FloorTypeManager.V001.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FloorTypeManagerCommand : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to ToolGuard.RunCommand.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp  = commandData.Application;
            var vm     = new FloorTypeManagerViewModel(uiApp);
            var window = new FloorTypeManagerWindow { DataContext = vm };

            new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
            window.Show();

            return Result.Succeeded;
        }
    }
}
