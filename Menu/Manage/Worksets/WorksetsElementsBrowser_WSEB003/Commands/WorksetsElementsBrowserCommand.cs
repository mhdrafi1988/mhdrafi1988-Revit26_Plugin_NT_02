using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB003.UI.ViewModels;
using Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB003.UI.Views;
using System.Windows.Interop;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.WorksetsElementsBrowser.WSEB003.Commands
{
    /// <summary>
    /// Ribbon entry point for the Worksets &amp; Elements Browser tool. Opens a
    /// modeless window (matches this project's window-lifecycle convention)
    /// listing every workset, category and type with tri-state checkboxes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WorksetsElementsBrowserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiDoc = commandData.Application.ActiveUIDocument;
                if (uiDoc == null)
                {
                    message = "No active document.";
                    return Result.Failed;
                }

                var viewModel = new WorksetsElementsBrowserViewModel(uiDoc, commandData.Application.MainWindowHandle);
                var window = new WorksetsElementsBrowserWindow
                {
                    DataContext = viewModel
                };

                // Parent to Revit's actual main window handle. System.Windows.Application.Current
                // is not guaranteed to exist inside Revit's process, so System.Windows.Application.Current.MainWindow
                // can NullReferenceException, or resolve to the wrong window and break z-order
                // against Revit â€” same fix applied in WorksetManagerCommand.
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;

                viewModel.CloseRequested += window.Close;

                window.Show(); // modeless â€” matches our Close-only modeless dialog convention

                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                Logger.Error("WorksetsElementsBrowserCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

