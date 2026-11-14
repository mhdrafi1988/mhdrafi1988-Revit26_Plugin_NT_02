using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.UI.ViewModels;
using Revit26_Plugin_Revised.ExportDwgToFolder.V004.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ExportDwgToFolder.V004.Commands
{
    /// <summary>
    /// Opens Export DWG to Folder (modeless). Nothing is scanned on open: the user picks a scope,
    /// scans, ticks the DWGs to export and exports them.
    /// </summary>
    // V003 (2026-10-14): version bump of V002 — new folder + namespace, no behaviour change.
    //
    // V004 (2026-10-17, per Rafi's confirmed decisions): behaviour change, so a new folder +
    // namespace per Claude/TOOL_VERSIONING.md.
    //   · Layer fidelity — a DWG export setup saved in the model can be chosen
    //     (ExportDWGSettings.ListNames + DWGExportOptions.GetPredefinedOptions), the
    //     PropOverrideMode is now an explicit three-way choice, and a CAD link with a resolvable
    //     source file is copied rather than re-exported (byte-for-byte layers).
    //   · Full destination path on every row, shown in a hover tooltip; new Layers column.
    //   · Output-name pattern, run-manifest CSV and a dry-run Check button.
    //   · The post-export delete prompt is now PostExportReviewWindow — a per-row Keep / Delete /
    //     Convert to import / Replace with link to exported file, with Open capped at five files.
    // Settings format bumped to version 4; a version 3 file still loads, new fields take defaults.
    //
    // [Transaction(ReadOnly)] still holds: every model change goes through the external event
    // handler, including the review's deletes and conversions.
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
