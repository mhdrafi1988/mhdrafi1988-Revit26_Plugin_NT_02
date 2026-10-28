using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.LinkMemory.V001.UI.ViewModels;
using Revit26_Plugin_Revised.LinkMemory.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.LinkMemory.V001.Commands
{
    /// <summary>
    /// Opens the Link Memory window, which remembers which Revit links are loaded and loads /
    /// unloads them back to a saved set. The window is modal, so its buttons run in this
    /// command's Revit API context.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LinkMemoryCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.LinkMemory.Title, "Open a project before running Link Memory.");
                return Result.Cancelled;
            }

            var viewModel = new LinkMemoryViewModel(doc);
            var window = new LinkMemoryWindow(viewModel);
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.ShowDialog();
            viewModel.SaveSettings();
            return Result.Succeeded;
        }
    }
}
