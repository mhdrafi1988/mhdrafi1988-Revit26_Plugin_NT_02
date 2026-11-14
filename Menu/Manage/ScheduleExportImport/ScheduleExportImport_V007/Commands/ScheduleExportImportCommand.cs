using System;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit26_Plugin_Revised.ScheduleExportImport.V007.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.ScheduleExportImport.V007.UI.ViewModels;
using Revit26_Plugin_Revised.ScheduleExportImport.V007.UI.Views;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.ScheduleExportImport.V007.Commands
{
    // Import is two-step (Analyze → preview → Apply) and writes only ticked, changed,
    // instance-parameter values; per Rafi's confirmed decision (2026-09-30).
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleExportImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = commandData.Application;
                var doc = uiApp.ActiveUIDocument.Document;

                var handler = new ScheduleImportEventHandler(doc);
                var externalEvent = ExternalEvent.Create(handler);

                var viewModel = new ScheduleExportImportViewModel(handler, externalEvent);
                var view = new ScheduleExportImportWindow(viewModel);
                new WindowInteropHelper(view).Owner = uiApp.MainWindowHandle;
                view.Show();

                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Schedule Export/Import", $"Could not open the tool:\n{ex.Message}");
                return Result.Failed;
            }
        }
    }
}
