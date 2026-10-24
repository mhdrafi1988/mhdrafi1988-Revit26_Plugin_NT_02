using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.DetailLineManager.V002.UI.ViewModels;
using Revit26_Plugin_Revised.DetailLineManager.V002.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Commands
{
    /// <summary>
    /// Opens the Detail Line Manager window (modeless): find detail lines by scope and filters,
    /// delete duplicates, short lines and unused line styles, and change line weights.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DetailLineManagerCommand : IExternalCommand
    {
        /// <inheritdoc/>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var doc = uiApp.ActiveUIDocument?.Document;
            if (doc == null)
            {
                TaskDialog.Show(ToolCatalog.DetailLineManagerV002.Title, "Open a project first.");
                return Result.Cancelled;
            }

            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.DetailLineManagerV002.Title, "Detail Line Manager works in projects, not in the Family Editor.");
                return Result.Cancelled;
            }

            if (doc.IsReadOnly)
            {
                TaskDialog.Show(ToolCatalog.DetailLineManagerV002.Title,
                    "The active document is read-only. You can search, but changes will fail until you open an editable copy.");
            }

            var viewModel = new MainViewModel(uiApp);
            var window = new MainWindow(viewModel);
            new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
            window.Show();
            return Result.Succeeded;
        }
    }
}
