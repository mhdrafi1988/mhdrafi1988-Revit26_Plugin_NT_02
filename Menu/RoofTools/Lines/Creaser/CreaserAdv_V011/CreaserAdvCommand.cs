// =======================================================
// File: CreaserAdvCommand.cs
// Namespace: Revit26_Plugin_Revised.CreaserAdv.V011.Commands
// Changes vs V009:
//   FIX  Window shown modeless (.Show()) instead of modal (.ShowDialog()),
//        parented to Revit's main window — the convention the other roof
//        tools already follow. Run goes through an ExternalEvent
//        (CreaserAdvHandler), and Revit only services an ExternalEvent when
//        it is idle; while a modal dialog holds this Execute() open the
//        event is not reliably serviced, so Run could sit queued until the
//        window was closed (the sibling tools made the same fix).
//   NEW  Logs the log-file path and the picked roof's name/id.
// plugin 6.16.1:
//   FIX  Execute runs through ToolGuard again (lost in V011), so an error
//        is reported instead of reaching Revit. The tool's log file is
//        closed when the window closes.
// =======================================================

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.CreaserAdv.V011.Services;
using Revit26_Plugin_Revised.CreaserAdv.V011.ViewModels;
using Revit26_Plugin_Revised.CreaserAdv.V011.Views;
using Revit26_Plugin_Revised.Shared.Services;
using System.Windows.Interop;

namespace Revit26_Plugin_Revised.CreaserAdv.V011.Commands
{
    /// <summary>
    /// Ribbon entry point for Creaser Adv: picks one roof in a plan view and opens the
    /// modeless tool window. The run itself happens later in <c>CreaserAdvHandler</c>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreaserAdvCommand : IExternalCommand
    {
        /// <summary>
        /// Runs the command through <see cref="ToolGuard.RunCommand"/>, so no exception reaches Revit.
        /// </summary>
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteUnguarded);

        private Result ExecuteUnguarded(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument    uiDoc = uiApp.ActiveUIDocument;

            if (uiDoc?.Document == null)
            {
                TaskDialog.Show("Creaser Advanced", "No active document found.");
                return Result.Cancelled;
            }

            if (uiDoc.ActiveView is not ViewPlan)
            {
                TaskDialog.Show("Creaser Advanced", "Please run this command from a Plan View.");
                return Result.Cancelled;
            }

            var logger = new LoggingService("CreaserAdv");
            CreaserAdvWindow window;

            try
            {
                logger.Info("Creaser Advanced V011 started.");
                logger.Info($"Log file: {logger.LogFilePath}");

                Element roof;
                try
                {
                    roof = new RoofSelectionService().SelectSingleRoof(uiDoc);
                    logger.Info($"Roof selected: {roof.Name} (id {roof.Id.Value})");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    logger.Warning("Roof selection cancelled by user.");
                    logger.Dispose();
                    return Result.Cancelled;
                }

                var viewModel = new CreaserAdvViewModel(uiApp, roof, logger);
                window = new CreaserAdvWindow(viewModel);
            }
            catch
            {
                logger.Dispose();
                throw; // ToolGuard reports it
            }

            window.Closed += (_, _) => logger.Dispose();

            new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;
            window.Show(); // modeless — Run is serviced by the ExternalEvent

            return Result.Succeeded;
        }
    }
}
