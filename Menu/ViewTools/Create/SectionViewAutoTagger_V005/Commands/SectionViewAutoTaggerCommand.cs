using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.SectionViewAutoTagger.V005
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SectionViewAutoTaggerCommand : IExternalCommand
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
                Logger.Error("SectionViewAutoTaggerCommand", ex);
                message = ex.Message;
                TaskDialog.Show("Section View Auto Tagger", $"An unexpected error occurred: {ex.Message}");
                return Result.Failed;
            }
        }

        private Result ExecuteInternal(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;

            var viewModel = new SectionViewAutoTaggerViewModel(uiApp);
            var window = new SectionViewAutoTaggerWindow(viewModel);

            var helper = new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = uiApp.MainWindowHandle
            };

            window.Show();

            return Result.Succeeded;
        }
    }
}
