using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ExportDwgToFolder.V003.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.ExportDwgToFolder.V003.UI.ViewModels;
using Revit26_Plugin_Revised.ExportDwgToFolder.V003.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V003.Commands
{
    /// <summary>
    /// Opens Export DWG to Folder (modeless). Nothing is scanned on open: the user picks a scope,
    /// scans, ticks the DWGs to export and exports them.
    /// </summary>
    // V003 (2026-10-14): version bump of V002 — new folder + namespace, no behaviour change.
    // Settings file and format (version 3) are shared with V002, so saved options carry over.
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportDwgToFolderCommand : IExternalCommand
    {
        /// <summary>Revit entry point — delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            if (uiApp.ActiveUIDocument?.Document == null)
            {
                TaskDialog.Show(ToolCatalog.ExportDwgToFolder.Title, "Open a project first.");
                return Result.Cancelled;
            }

            var handler = new DwgExportEventHandler();
            var externalEvent = ExternalEvent.Create(handler);

            var viewModel = new ExportDwgToFolderViewModel(handler, externalEvent);
            var window = new ExportDwgToFolderWindow(viewModel);
            new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
            window.Show();

            return Result.Succeeded;
        }
    }
}
