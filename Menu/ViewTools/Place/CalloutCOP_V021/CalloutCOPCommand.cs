using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Windows.Interop;
using Revit26_Plugin_Revised.CalloutCOP.V021.Views;
using Revit26_Plugin_Revised.Utilities;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.CalloutCOP.V021.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CalloutCOPCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ToolGuard.RunCommand(GetType(), commandData, ref message, elements, ExecuteCore);

        private Result ExecuteCore(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // FIX: window/ViewModel construction was unguarded, so any exception
            // (e.g. no active document) surfaced as Revit's generic "Command
            // Failure for External Command" dialog with no readable context.
            try
            {
                var window = new CalloutCOPWindow(commandData);
                new WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("CalloutCOPCommand", ex);
                message = ex.Message;

                var dlg = new TaskDialog("Callout COP V018")
                {
                    MainInstruction = "Could not open the tool",
                    MainContent = ex.Message,
                    ExpandedContent = ex.ToString() // full exception type, message, and stack trace
                };
                dlg.Show();

                return Result.Failed;
            }
        }
    }
}
