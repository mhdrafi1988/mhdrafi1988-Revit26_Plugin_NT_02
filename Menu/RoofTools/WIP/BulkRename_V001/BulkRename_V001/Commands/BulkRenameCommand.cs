using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.BulkRename.V001.UI.ViewModels;
using Revit26_Plugin_Revised.BulkRename.V001.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.BulkRename.V001.Commands
{
    /// <summary>
    /// Opens the Bulk Rename window, which renames many line styles, line patterns, arrowheads or
    /// fill patterns at once.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BulkRenameCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.BulkRename.Title, "Open a project first.");
                return Result.Cancelled;
            }

            if (doc.IsReadOnly)
            {
                TaskDialog.Show(ToolCatalog.BulkRename.Title, "The active document is read-only.");
                return Result.Cancelled;
            }

            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.BulkRename.Title, "Bulk Rename works on project documents, not families.");
                return Result.Cancelled;
            }

            // Created here, inside the command: the view model's external event needs a Revit API context.
            var viewModel = new BulkRenameViewModel(doc);
            var window = new BulkRenameWindow(viewModel);
            new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
            window.Show();
            return Result.Succeeded;
        }
    }
}
