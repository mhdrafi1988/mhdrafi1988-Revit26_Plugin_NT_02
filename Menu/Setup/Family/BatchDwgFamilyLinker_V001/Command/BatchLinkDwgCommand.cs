using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using BatchDwgFamilyLinker.UI;
using BatchDwgFamilyLinker.ViewModels;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace BatchDwgFamilyLinker.Command
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchLinkDwgCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
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
                Logger.Error("BatchLinkDwgCommand", ex);
                message = ex.Message;
                TaskDialog.Show("Batch DWG Family Linker", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;

            // --------------------------------------------
            // Basic safety checks
            // --------------------------------------------
            if (uiDoc == null || uiDoc.Document == null)
            {
                TaskDialog.Show(
                    "Batch DWG Family Linker",
                    "Please open a Revit project before running this command.");
                return Result.Cancelled;
            }

            // --------------------------------------------
            // Create ViewModel
            // --------------------------------------------
            var viewModel = new BatchLinkViewModel(uiApp);

            // --------------------------------------------
            // Create Window
            // --------------------------------------------
            var window = new BatchLinkWindow(viewModel);

            // IMPORTANT: set Revit as owner (prevents focus issues)
            var helper = new WindowInteropHelper(window)
            {
                Owner = uiApp.MainWindowHandle
            };

            // --------------------------------------------
            // Show UI (modeless is REQUIRED for ExternalEvent)
            // --------------------------------------------
            window.Show();

            return Result.Succeeded;
        }
    }
}
