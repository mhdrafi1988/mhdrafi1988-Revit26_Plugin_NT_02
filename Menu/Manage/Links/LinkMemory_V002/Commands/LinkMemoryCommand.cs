using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.LinkMemory.V002.UI.ViewModels;
using Revit26_Plugin_Revised.LinkMemory.V002.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.LinkMemory.V002.Commands
{
    /// <summary>
    /// Opens Link Memory V002: remembers which links the active view (and its related views) or a
    /// print set shows, loads them back, and exports the list to Excel. The window is modal, so its
    /// buttons run in this command's Revit API context.
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
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.LinkMemoryV002.Title, "Open a project before running Link Memory.");
                return Result.Cancelled;
            }

            var viewModel = new LinkMemoryViewModel(doc, uidoc.ActiveView);
            var window = new LinkMemoryWindow(viewModel);
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.ShowDialog();
            viewModel.SaveSettings();
            return Result.Succeeded;
        }
    }
}
