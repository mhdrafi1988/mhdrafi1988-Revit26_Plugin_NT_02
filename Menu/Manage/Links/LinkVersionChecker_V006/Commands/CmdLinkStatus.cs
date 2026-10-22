using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.LinkVersionChecker.V006.UI.ViewModels;
using Revit26_Plugin_Revised.LinkVersionChecker.V006.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.LinkVersionChecker.V006.Commands
{
    /// <summary>
    /// Shows the status of every Revit link in the open model that comes from Desktop Connector:
    /// loaded or not, its sync state on this PC, and whether the loaded copy is the latest.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CmdLinkStatus : IExternalCommand
    {
        /// <summary>Revit entry point; delegates to <see cref="ToolGuard.RunCommand"/>.</summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(typeof(CmdLinkStatus), commandData, ref message, elements, Run);

        private static Result Run(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            Document doc = uiapp.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show(ToolCatalog.LinkVersionCheckerV006.Name, "Open a project before running Link Version Checker.");
                return Result.Cancelled;
            }

            var vm = new LinkStatusViewModel(doc);
            var win = new LinkStatusWindow(vm);
            new WindowInteropHelper(win) { Owner = uiapp.MainWindowHandle };

            // Modal: Reload / Unload run from the window's buttons and must stay in Revit's API context.
            win.ShowDialog();
            return Result.Succeeded;
        }
    }
}
